using System.IO;
using System.Text;

namespace PocketDeadlock;

internal static partial class SelfTests
{
    static void AccountTests(string run,Action<bool,string> assert,Action<Action,string> fails)
    {
        string root=Path.Combine(run,"account-data");Directory.CreateDirectory(root);
        var account=new GameBananaAccount(root);
        var value=new AccountCookie("session_fixture","fixture-only-secret-123", ".gamebanana.com","/",true,true,DateTime.UtcNow.AddHours(1));
        const string browserAgent="Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/154.0.0.0 Safari/537.36 Edg/154.0.0.0";
        fails(()=>account.Save([value],false),"anonymous login cannot be connected just because a guest cookie exists");
        fails(()=>account.Save([value with {Domain="example.com"}],true),"unrelated cookie domains cannot be saved as a GameBanana session");
        fails(()=>account.Save([value],true,"Browser/1.0\r\nX-Injected: bad"),"browser identity cannot inject extra HTTP headers");
        account.Save([value,value with {Name="unrelated",Domain="example.com"},value with {Name="expired",Expires=DateTime.UtcNow.AddMinutes(-1)}],true,browserAgent);
        var restored=new GameBananaAccount(root);
        assert(restored.SignedIn && restored.Header("https://gamebanana.com/apiv11/Mod/1/ProfilePage").Contains(value.Value),"GameBanana session survives restart under the same Windows account");
        assert(restored.UserAgent("https://gamebanana.com/")==browserAgent,"the browser identity is retained with its protected session across restart");
        using(var request=GameBanana.GetRequest("https://gamebanana.com/apiv13/Member/UiConfig?_sUrl=%2F",restored))
            assert(request.Headers.UserAgent.ToString()==browserAgent && request.Headers.GetValues("Cookie").Single().Contains(value.Value) && request.Headers.CacheControl?.NoCache==true,"authenticated API requests use the original browser identity and cookies without a stale guest cache");
        using(var request=GameBanana.GetRequest("https://files.gamebanana.com/file.zip",restored))
            assert(request.Headers.UserAgent.ToString()==GameBanana.DefaultUserAgent && !request.Headers.Contains("Cookie"),"CDN requests do not inherit the browser session or its request identity");
        assert(!Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(root,"gamebanana-session.dat"))).Contains(value.Value),"session file uses Windows encryption instead of plaintext cookies");
        assert(restored.Header("https://images.gamebanana.com/img.png")=="" && restored.Header("https://files.gamebanana.com/file.zip")=="" && restored.Header("https://example.com/")=="" && restored.Header("http://gamebanana.com/")=="" && restored.Header("https://gamebanana.com.evil.test/")=="","session cookies are scoped to the HTTPS GameBanana website and never sent to CDNs or other catalogs");
        assert(!restored.Header("https://gamebanana.com/").Contains("expired") && !restored.Header("https://gamebanana.com/").Contains("unrelated"),"expired and foreign cookies are discarded");
        restored.Forget();assert(!restored.SignedIn && !File.Exists(Path.Combine(root,"gamebanana-session.dat")) && restored.Header("https://gamebanana.com/")=="","logout removes the protected session and stops authenticated requests");
        var legacy=System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new[]{value});
        File.WriteAllBytes(Path.Combine(root,"gamebanana-session.dat"),System.Security.Cryptography.ProtectedData.Protect(legacy,null,System.Security.Cryptography.DataProtectionScope.CurrentUser));
        var older=new GameBananaAccount(root);
        assert(older.SignedIn && older.Header("https://gamebanana.com/").Contains(value.Value) && older.UserAgent("https://gamebanana.com/")==GameBanana.DefaultUserAgent,"older protected cookie-only sessions remain readable without invented browser metadata");
        File.WriteAllBytes(Path.Combine(root,"gamebanana-session.dat"),[1,2,3]);
        assert(!new GameBananaAccount(root).SignedIn,"invalid protected session does not crash startup or imply a login");
        var normal=new CatalogItem(1,"Mod","Regular mod","Author","Skins","","https://gamebanana.com/mods/1",1);
        var sensitive=normal with {Id=2,ContentRatings="Sensitive content",InitialVisibility="hide"};
        var warning=normal with {Id=3,InitialVisibility="warn"};
        assert(CatalogContent.Matches(normal,"safe",false) && !CatalogContent.Matches(sensitive,"all",false) && CatalogContent.Matches(sensitive,"sensitive",true),"sensitive catalog records become available with a connected account and the content filter");
        assert(!CatalogContent.Matches(sensitive,"safe",true) && !CatalogContent.Matches(normal,"sensitive",true) && CatalogContent.Matches(warning,"sensitive",false),"regular, sensitive and all-available filters remain independent of hero and mod type");
        var merged=UnifiedCatalog.Merge([normal,sensitive with {Id=1,Provider="DeadlockMods"}]).Single().Item;
        assert(CatalogContent.Sensitive(merged) && merged.InitialVisibility=="hide","provider deduplication retains sensitive-content metadata");
    }
}
