using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PocketDeadlock;

// A provider boundary: add another catalog by implementing this interface.
public interface IModCatalog
{
    Task<CatalogPage> Browse(string kind, string search, int page, CancellationToken token);
    Task<ModDetails> Details(string kind, long id, CancellationToken token);
    Task<ModDetails> Details(CatalogItem item,CancellationToken token)=>Details(item.Kind,item.Id,token);
    Task Download(RemoteFile file, string destination, IProgress<string> progress, CancellationToken token);
}

public sealed class GameBanana : IModCatalog
{
    static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromMinutes(10) };
    const string Api = "https://gamebanana.com/apiv11/";
    public static bool TrustedUrl(string value)
        => Uri.TryCreate(value, UriKind.Absolute, out var u) && u.Scheme == "https" && u.IsDefaultPort && string.IsNullOrEmpty(u.UserInfo)
        && (u.Host.Equals("gamebanana.com", StringComparison.OrdinalIgnoreCase) || u.Host.EndsWith(".gamebanana.com", StringComparison.OrdinalIgnoreCase));

    public static async Task<HttpResponseMessage> Get(string url, CancellationToken token)
    {
        for (int i = 0; i < 6; i++)
        {
            if (!TrustedUrl(url)) throw new IOException("Источник загрузки не принадлежит GameBanana.");
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd("PocketDeadlock/0.2");
            var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            if ((int)response.StatusCode is >= 300 and < 400)
            {
                var location = response.Headers.Location;
                response.Dispose();
                if (location == null) throw new IOException("У сервера отсутствует адрес перенаправления.");
                url = new Uri(new Uri(url), location).AbsoluteUri;
                continue;
            }
            if (!response.IsSuccessStatusCode)
            {
                int status = (int)response.StatusCode; response.Dispose();
                throw new IOException($"GameBanana: HTTP {status}. Попробуйте повторить запрос позже.");
            }
            return response;
        }
        throw new IOException("Слишком много перенаправлений.");
    }
    async Task<JsonElement> Json(string route, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(40));
        using var response = await Get(Api + route, timeout.Token);
        using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
        using var buffer = new MemoryStream();
        await CopyLimited(stream, buffer, 12 * 1024 * 1024, timeout.Token);
        using var doc = JsonDocument.Parse(buffer.ToArray());
        if (doc.RootElement.TryGetProperty("_sErrorCode", out var error)) throw new IOException("GameBanana: " + error.GetString());
        return doc.RootElement.Clone();
    }
    static void Kind(string kind) { if (kind is not ("Mod" or "Sound")) throw new IOException("Неподдерживаемый каталог."); }
    public async Task<CatalogPage> Browse(string kind, string search, int page, CancellationToken token)
    {
        Kind(kind);
        var route = string.IsNullOrWhiteSpace(search)
            ? $"{kind}/Index?_nPerpage=24&_aFilters%5BGeneric_Game%5D=20948&_nPage={page}"
            : $"Util/Search/Results?_sSearchString={Uri.EscapeDataString(search.Trim())}&_idGameRow=20948&_sModelName={kind}&_nPage={page}";
        var root = await Json(route, token);
        var records = Child(root, "_aRecords");
        if (records.ValueKind != JsonValueKind.Array) throw new IOException("GameBanana вернул неизвестный формат каталога.");
        var items = records.EnumerateArray().Where(x => Number(Child(x, "_aGame"), "_idRow") == 20948)
            .Where(x => Text(x, "_sInitialVisibility") != "hide" && !Flag(x,"_bIsObsolete"))
            .Select(x => Item(x, kind)).ToList();
        var meta = Child(root, "_aMetadata");
        return new(items, (int)Number(meta,"_nRecordCount"), Math.Max(1,(int)Number(meta,"_nPerpage")));
    }
    public async Task<ModDetails> Details(string kind, long id, CancellationToken token)
    {
        Kind(kind);
        if (id <= 0) throw new IOException("Неверный номер мода.");
        var root = await Json($"{kind}/{id}/ProfilePage", token);
        if (Number(Child(root,"_aGame"),"_idRow") != 20948) throw new IOException("Этот мод предназначен для другой игры.");
        if (Flag(root,"_bIsTrashed") || Flag(root,"_bIsWithheld")) throw new IOException("Этот мод недоступен на GameBanana.");
        var files = Child(root,"_aFiles");
        var result = new List<RemoteFile>();
        if (files.ValueKind == JsonValueKind.Array)
            foreach (var f in files.EnumerateArray())
                result.Add(new(Number(f,"_idRow"),Text(f,"_sFile"),Number(f,"_nFilesize"),Text(f,"_sDownloadUrl"),Text(f,"_sMd5Checksum"),
                    Flag(f,"_bContainsExe") || Text(f,"_sAvResult") is "infected" or "malicious" || Text(f,"_sAnalysisResult") is "bad" or "malicious",Text(f,"_sDescription"),Number(f,"_tsDateAdded")));
        var req = Child(root,"_aRequirements");
        var requirements = req.ValueKind is JsonValueKind.Array or JsonValueKind.Object ? Plain(req.ToString()) : "";
        return new(Item(root,kind),Plain(Text(root,"_sText")), requirements,result);
    }
    public async Task Download(RemoteFile file, string destination, IProgress<string> progress, CancellationToken token)
    {
        if (file.Blocked) throw new IOException("Источник пометил файл как опасный или содержащий программу.");
        const long limit = 2L * 1024 * 1024 * 1024;
        if (file.Size > limit) throw new IOException("Лимит одного архива: 2 ГБ.");
        try
        {
            using var response = await Get(file.Url, token);
            var total = response.Content.Headers.ContentLength ?? file.Size;
            if (total > limit) throw new IOException("Лимит одного архива: 2 ГБ.");
            using var input = await response.Content.ReadAsStreamAsync(token);
            using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                await CopyLimited(input, output, limit, token, done => progress.Report($"Загрузка: {done / 1048576d:0.0} / {total / 1048576d:0.0} МБ"));
            if (file.Size > 0 && new FileInfo(destination).Length != file.Size) throw new IOException("Размер загрузки не совпал с каталогом.");
            if (Regex.IsMatch(file.Md5, "^[a-fA-F0-9]{32}$"))
            {
                using var data = File.OpenRead(destination);
                var hash = Convert.ToHexString(await MD5.HashDataAsync(data, token));
                if (!hash.Equals(file.Md5, StringComparison.OrdinalIgnoreCase)) throw new IOException("Контрольная сумма загрузки не совпала с каталогом.");
            }
        }
        catch { if (File.Exists(destination)) File.Delete(destination); throw; }
    }
    internal static async Task CopyLimited(Stream input, Stream output, long limit, CancellationToken token, Action<long>? progress = null)
    {
        byte[] buffer = new byte[128 * 1024]; long done = 0; long last = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
        {
            done += read;
            if (done > limit) throw new IOException("Превышен допустимый размер файла.");
            await output.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
            if (Environment.TickCount64 - last > 150) { progress?.Invoke(done); last = Environment.TickCount64; }
        }
        progress?.Invoke(done);
    }
    static CatalogItem Item(JsonElement x,string kind)
    {
        string image = "";
        var images = Child(Child(x,"_aPreviewMedia"),"_aImages");
        if (images.ValueKind == JsonValueKind.Array && images.GetArrayLength()>0)
        {
            var first=images[0]; string name=Text(first,"_sFile530");
            if (name=="") name=Text(first,"_sFile");
            image=Text(first,"_sBaseUrl")+"/"+name;
            if (!TrustedUrl(image)) image="";
        }
        var category=Text(Child(x,"_aRootCategory"),"_sName");
        if(category=="") category=Text(Child(x,"_aCategory"),"_sName");
        var hero=Text(Child(x,"_aSubCategory"),"_sName");
        return new(Number(x,"_idRow"),kind,Text(x,"_sName"),Text(Child(x,"_aSubmitter"),"_sName"),
            category+(hero=="" ? "" : " · "+hero),image,Text(x,"_sProfileUrl"),Number(x,"_tsDateModified"),Downloads:Number(x,"_nDownloadCount"),Likes:Number(x,"_nLikeCount"));
    }
    internal static JsonElement Child(JsonElement x,string key) => x.ValueKind==JsonValueKind.Object && x.TryGetProperty(key,out var v) ? v : default;
    internal static string Text(JsonElement x,string key) => Child(x,key).ValueKind==JsonValueKind.String ? Child(x,key).GetString()! : "";
    internal static long Number(JsonElement x,string key) => Child(x,key).TryNumber();
    internal static bool Flag(JsonElement x,string key) => Child(x,key).ValueKind==JsonValueKind.True;
    internal static string Plain(string text) => WebUtility.HtmlDecode(Regex.Replace(Regex.Replace(text,@"<(br\s*/?|/p|/div|/li)>","\n",RegexOptions.IgnoreCase),"<[^>]*>",""));
}
internal static class JsonHelpers { public static long TryNumber(this JsonElement e) => e.ValueKind==JsonValueKind.Number && e.TryGetInt64(out var n) ? n : 0; }
