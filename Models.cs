using System.Text.Json.Serialization;

namespace PocketDeadlock;

public record CatalogItem(long Id, string Kind, string Name, string Author, string Category, string Image, string Url, long Modified, string Provider="GameBanana", string Key="",long Downloads=0,long Likes=0,string Hero="",string ModType="",string ContentRatings="",string InitialVisibility="")
{
    public string Caption => CatalogTaxonomy.Caption(this);
}
public record RemoteFile(long Id, string Name, long Size, string Url, string Md5, bool Blocked, string Description="", long Added=0)
{
    public string Label => L.T($"{Name}  ({(Size<1048576?L.T($"{Size/1024d:0.#} КБ"):L.T($"{Size/1048576d:0.#} МБ"))})");
    public override string ToString()=>Label;
}
public record ModDetails(CatalogItem Item, string Description, string Requirements, List<RemoteFile> Files);
public record CatalogPage(List<CatalogItem> Items, int Total, int PerPage);
public sealed class LibraryMod
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string ContentId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "Local";
    public long RemoteId { get; set; }
    public long RemoteModified { get; set; }
    public long FileId { get; set; }
    public string SourceUrl { get; set; } = "";
    public string VariantName { get; set; } = "";
    public string VariantKey { get; set; } = "";
    public string RemoteMd5 { get; set; } = "";
    public List<string> SelectedPaths { get; set; } = [];
    public bool AutoUpdate { get; set; } = true;
    public bool AllowOverrides { get; set; }
    public List<ModRevision> Revisions { get; set; } = [];
    public List<string> Files { get; set; } = [];
    public List<string> Hashes { get; set; } = [];
    public bool Enabled { get; set; }
    public string Added { get; set; } = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm");
    public string UpdateStatus { get; set; } = "";
    public RemoteFile? AvailableUpdate { get; set; }
    [JsonIgnore] public string Caption => $"{Files.Count} VPK · {Added} · {L.UiStatus(UpdateStatus)}";
}
public sealed class AppState
{
    public string GamePath { get; set; } = "";
    public List<LibraryMod> Mods { get; set; } = [];
    public bool CheckUpdatesAutomatically { get; set; } = true;
    public bool DownloadModUpdatesAutomatically { get; set; } = true;
    public bool ApplyOnLaunch { get; set; } = true;
    public bool CheckAppUpdatesAutomatically { get; set; } = true;
    public string UpdateFeed { get; set; } = "https://github.com/AlexUner/pocket-deadlock-mod-manager/releases/latest/download/update-feed.json";
    public string LastUpdateCheck { get; set; } = "";
    public string LastAppUpdateStatus { get; set; } = L.T("Канал выпусков GitHub подключен; проверка еще не выполнена");
    public string AppUpdateState { get; set; } = "idle";
    public string AppUpdateVersion { get; set; } = "";
    public string LastAppUpdateCheck { get; set; } = "";
    public string LastAppUpdateError { get; set; } = "";
    public string LastCatalog { get; set; } = "GameBanana";
    public bool PendingApply { get; set; }
    public string Language { get; set; } = "system";
    public string Theme { get; set; } = "system";
    public List<CatalogItem> Favorites { get; set; } = [];
    public List<ModProfile> Profiles { get; set; } = [];
    public List<ActivityEntry> Activity { get; set; } = [];
}
public sealed record ProfileEntry(string Id,long RemoteId,string Kind,long FileId,string VariantKey,bool Enabled,bool AllowOverrides,List<string>? SelectedPaths=null);
public sealed record ModProfile(string Id,string Name,List<ProfileEntry> Mods);
public sealed record ActivityEntry(string Time,string Name,string Status);
public sealed record ModRevision(string Folder, string Name, long Modified, long FileId, string VariantName, string VariantKey, string Md5, List<string> Files, List<string> Hashes, List<string> SelectedPaths);
public record PreparedMod(string Folder, List<string> Files);
