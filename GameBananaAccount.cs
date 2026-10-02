using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PocketDeadlock;

public sealed record AccountCookie(string Name,string Value,string Domain,string Path,bool Secure,bool HttpOnly,DateTime Expires);

// Only our embedded GameBanana browser supplies these cookies; passwords never enter the manager.
public sealed class GameBananaAccount
{
    readonly string file;
    readonly object gate=new();
    readonly SemaphoreSlim validation=new(1);
    DateTime validated;
    CookieContainer cookies=new(100,100,8192);
    public string BrowserFolder {get;}
    public bool SignedIn {get;private set;}
    public GameBananaAccount(string root)
    {
        file=Path.Combine(root,"gamebanana-session.dat");BrowserFolder=Path.Combine(root,"gamebanana-browser");
        try
        {
            if(File.Exists(file) && new FileInfo(file).Length<128*1024)
            {
                var clear=ProtectedData.Unprotect(File.ReadAllBytes(file),null,DataProtectionScope.CurrentUser);
                try {var saved=JsonSerializer.Deserialize<List<AccountCookie>>(clear)??[];Install(saved);SignedIn=cookies.Count>0;}
                finally {CryptographicOperations.ZeroMemory(clear);}
            }
        }
        catch(IOException) { }catch(CryptographicException) { }catch(JsonException) { }
    }
    public static bool SessionHost(string url)=>Uri.TryCreate(url,UriKind.Absolute,out var uri) && uri.Scheme=="https" && uri.IsDefaultPort && uri.UserInfo=="" && uri.Host is "gamebanana.com" or "www.gamebanana.com";
    static bool CookieDomain(string domain)=>domain.TrimStart('.').ToLowerInvariant() is "gamebanana.com" or "www.gamebanana.com";
    void Install(IEnumerable<AccountCookie> values)
    {
        cookies=new CookieContainer(100,100,8192);
        foreach(var value in values.Take(100))
        {
            if(!CookieDomain(value.Domain) || value.Expires!=default && value.Expires<=DateTime.UtcNow) continue;
            try {cookies.Add(new Cookie(value.Name,value.Value,value.Path,value.Domain){Secure=value.Secure,HttpOnly=value.HttpOnly,Expires=value.Expires});}
            catch(CookieException) { }
        }
    }
    public void Save(IEnumerable<AccountCookie> values,bool loginRecognized)
    {
        if(!loginRecognized) throw new IOException(L.T("Сначала заверши вход на GameBanana."));
        lock(gate)
        {
            Install(values);if(cookies.Count==0) throw new IOException(L.T("GameBanana не передал сеанс входа. Повтори вход."));
            Persist();SignedIn=true;
        }
    }
    void Persist()
    {
        var values=cookies.GetAllCookies().Cast<Cookie>().Where(x=>CookieDomain(x.Domain) && !x.Expired)
            .Select(x=>new AccountCookie(x.Name,x.Value,x.Domain,x.Path,x.Secure,x.HttpOnly,x.Expires)).ToList();
        var clear=JsonSerializer.SerializeToUtf8Bytes(values);
        try {ModStorage.AtomicWrite(file,ProtectedData.Protect(clear,null,DataProtectionScope.CurrentUser));}
        finally {CryptographicOperations.ZeroMemory(clear);}
    }
    internal string Header(string url)
    {
        if(!SessionHost(url)) return "";
        lock(gate) return SignedIn?cookies.GetCookieHeader(new Uri(url)):"";
    }
    internal void Accept(string url,IEnumerable<string> values)
    {
        if(!SessionHost(url)) return;
        lock(gate)
        {
            if(!SignedIn) return;
            foreach(string value in values) try {cookies.SetCookies(new Uri(url),value);}catch(CookieException) { }
            Persist();
        }
    }
    public async Task<bool> Verify(CancellationToken token)
    {
        if(!SignedIn) return false;
        await validation.WaitAsync(token);
        try
        {
            if(!SignedIn) return false;
            if(validated>DateTime.UtcNow.AddMinutes(-5)) return true;
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromSeconds(25));
            using var response=await GameBanana.Get("https://gamebanana.com/apiv13/Member/UiConfig",timeout.Token);
            using var input=await response.Content.ReadAsStreamAsync(timeout.Token);using var buffer=new MemoryStream();
            await GameBanana.CopyLimited(input,buffer,2*1024*1024,timeout.Token);
            using var json=JsonDocument.Parse(buffer.ToArray());
            if(!GameBanana.Flag(json.RootElement,"_bIsLoggedIn")) {Forget();return false;}
            validated=DateTime.UtcNow;return true;
        }
        finally {validation.Release();}
    }
    public void Forget()
    {
        lock(gate) {if(File.Exists(file)) File.Delete(file);cookies=new CookieContainer(100,100,8192);SignedIn=false;validated=default;}
    }
}

public static class CatalogContent
{
    public static bool Sensitive(CatalogItem item)=>item.ContentRatings!="" || item.InitialVisibility is "warn" or "hide";
    public static bool Matches(CatalogItem item,string filter,bool signedIn)
        =>(signedIn || item.InitialVisibility!="hide") && (filter switch {"safe"=>!Sensitive(item),"sensitive"=>Sensitive(item),_=>true});
}

public sealed class GameBananaLoginRequiredException : IOException
{
    public GameBananaLoginRequiredException():base(L.T("Для этого мода нужен вход GameBanana. Нажми «Войти GameBanana» и проверь настройки контента аккаунта.")) { }
}
