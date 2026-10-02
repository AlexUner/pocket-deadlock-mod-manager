using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PocketDeadlock;

public sealed class Catalogs
{
    public IReadOnlyDictionary<string,IModCatalog> Sources { get; }=new Dictionary<string,IModCatalog>
    {
        ["GameBanana"]=new GameBanana(),["Deadlocker"]=new CommunityCatalog("Deadlocker"),["DeadlockMods"]=new CommunityCatalog("DeadlockMods")
    };
    public IModCatalog Get(string source)=>Sources.TryGetValue(source,out var value)?value:Sources["GameBanana"];
    public static bool SafePage(string value)=>Uri.TryCreate(value,UriKind.Absolute,out var u) && u.Scheme=="https" && u.IsDefaultPort && u.UserInfo=="";
    public static bool SafeImage(string value)=>GameBanana.TrustedUrl(value) ||
        SafePage(value) && new[]{"deadlocker.net","deadlockmods.com","cdn.discordapp.com","media.discordapp.net"}.Contains(new Uri(value).Host);
}

public sealed class CommunityCatalog(string source) : IModCatalog
{
    static readonly HttpClient Http=new() { Timeout=TimeSpan.FromSeconds(45) };
    readonly GameBanana origin=new();
    List<(CatalogItem Item,string Description)>? cache;
    DateTimeOffset expires;
    internal async Task<List<CatalogItem>> Index(CancellationToken token)
    {
        if(cache==null || expires<DateTimeOffset.UtcNow) await Refresh(token);
        return cache!.Select(x=>x.Item).ToList();
    }
    static string S(JsonElement e,string k)=>GameBanana.Text(e,k);
    static bool B(JsonElement e,string k)=>GameBanana.Flag(e,k) || GameBanana.Number(e,k)==1;
    static long Time(string value)=>DateTimeOffset.TryParse(value,out var d)?d.ToUnixTimeSeconds():0;
    public async Task<CatalogPage> Browse(string kind,string search,int page,CancellationToken token)
    {
        if(cache==null || expires<DateTimeOffset.UtcNow) await Refresh(token);
        IEnumerable<CatalogItem> items=cache!.Select(x=>x.Item).Where(x=>x.Kind==kind);
        if(!string.IsNullOrWhiteSpace(search)) items=items.Where(x=>(x.Name+" "+x.Category+" "+x.Author).Contains(search.Trim(),StringComparison.OrdinalIgnoreCase));
        var all=items.OrderByDescending(x=>x.Modified).ToList();
        return new(all.Skip((Math.Max(1,page)-1)*24).Take(24).ToList(),all.Count,24);
    }
    async Task<JsonElement> Json(string url,CancellationToken token,string? bearer=null)
    {
        using var request=new HttpRequestMessage(HttpMethod.Get,url);
        request.Headers.UserAgent.ParseAdd("PocketDeadlock/0.2");
        if(bearer!=null) request.Headers.Authorization=new("Bearer",bearer);
        using var response=await Http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,token);
        if(!response.IsSuccessStatusCode) throw new IOException($"{source}: HTTP {(int)response.StatusCode}. Повторите позже.");
        using var input=await response.Content.ReadAsStreamAsync(token); using var output=new MemoryStream();
        await GameBanana.CopyLimited(input,output,40*1024*1024,token);
        using var doc=JsonDocument.Parse(output.ToArray()); return doc.RootElement.Clone();
    }
    async Task Refresh(CancellationToken token)
    {
        JsonElement root;
        if(source=="Deadlocker")
        {
            // This is the public guest initialization used by Deadlocker's own website, no Steam login.
            var guest=await Json("https://deadlocker.net/api/v1/getAccessToken",token);
            root=GameBanana.Child(await Json("https://deadlocker.net/api/v1/mods?nsfw=false",token,S(guest,"access_token")),"mods");
        }
        else root=await Json("https://api.deadlockmods.app/mods",token);
        if(root.ValueKind!=JsonValueKind.Array) throw new IOException(source+": неизвестный формат каталога.");
        var items=new List<(CatalogItem,string)>();
        foreach(var x in root.EnumerateArray())
        {
            if(B(x,"is_nsfw") || B(x,"isNSFW") || B(x,"isTrashed") || B(x,"isObsolete") || B(x,"isBlacklisted") || S(x,"deleted_at")!="") continue;
            if(source=="Deadlocker")
            {
                string key=S(x,"id"),link=S(x,"mod_link"); long id=GameBanana.Number(x,"gamebanana_id");
                string kind=link.Contains("/sounds/") || S(x,"mod_type").Contains("sound",StringComparison.OrdinalIgnoreCase)?"Sound":"Mod";
                var item=new CatalogItem(id,kind,S(x,"title"),"Deadlocker",S(x,"gb_category")+" · "+S(x,"character_name"),
                    S(x,"main_image"),"https://deadlocker.net/mod/"+Uri.EscapeDataString(key),Math.Max(Time(S(x,"gb_updated_at")),Time(S(x,"updated_at"))),source,key);
                items.Add((item,S(x,"description")));
            }
            else
            {
                if(!long.TryParse(S(x,"remoteId"),out var id)) continue;
                var images=GameBanana.Child(x,"images"); string image=images.ValueKind==JsonValueKind.Array && images.GetArrayLength()>0?images[0].GetString()??"":"";
                var item=new CatalogItem(id,B(x,"isAudio")?"Sound":"Mod",S(x,"name"),S(x,"author"),S(x,"category")+" · "+S(x,"hero"),image,S(x,"remoteUrl"),Time(S(x,"remoteUpdatedAt")),source,S(x,"id"));
                items.Add((item,S(x,"description")));
            }
        }
        cache=items; expires=DateTimeOffset.UtcNow.AddMinutes(15);
    }
    public Task<ModDetails> Details(string kind,long id,CancellationToken token)=>origin.Details(kind,id,token);
    public async Task<ModDetails> Details(CatalogItem item,CancellationToken token)
    {
        if(item.Id>0)
        {
            var details=await origin.Details(item.Kind,item.Id,token);
            return details with { Item=details.Item with {Provider=source,Key=item.Key} };
        }
        if(cache==null) await Refresh(token);
        var data=cache!.FirstOrDefault(x=>x.Item.Key==item.Key);
        return new(item,GameBanana.Plain(data.Description??""),"Файл распространяется через сообщество. Откройте страницу источника, скачайте мод и импортируйте VPK или архив.",[]);
    }
    public Task Download(RemoteFile file,string destination,IProgress<string> progress,CancellationToken token)=>origin.Download(file,destination,progress,token);
    internal static async Task<HttpResponseMessage> GetImage(string url,CancellationToken token)
    {
        if(!Catalogs.SafeImage(url)) throw new IOException("Неподдерживаемый адрес изображения.");
        if(GameBanana.TrustedUrl(url)) return await GameBanana.Get(url,token);
        // Redirects are blocked so a public preview cannot reach an unrelated host.
        using var client=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false}) { Timeout=TimeSpan.FromSeconds(20) };
        var response=await client.GetAsync(url,HttpCompletionOption.ResponseHeadersRead,token);
        response.EnsureSuccessStatusCode();
        // Buffer while this client is alive; cap prevents large responses.
        using var input=await response.Content.ReadAsStreamAsync(token); using var buffer=new MemoryStream();
        await GameBanana.CopyLimited(input,buffer,8*1024*1024,token); response.Dispose();
        return new HttpResponseMessage(System.Net.HttpStatusCode.OK){Content=new ByteArrayContent(buffer.ToArray())};
    }
}
