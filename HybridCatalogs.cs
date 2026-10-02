using System.IO;
using System.Text.Json;

namespace PocketDeadlock;

public sealed class HybridCatalogs
{
    public IReadOnlyDictionary<string,IModCatalog> Sources {get;}
    readonly Dictionary<string,HybridCatalog> catalogs;
    readonly object unifiedGate=new();
    List<UnifiedEntry>? unifiedCache;
    readonly bool allowRemoteSearch;
    public event Action<string>? Changed;
    public event Action<string>? Status;
    public bool IsRefreshing {get;private set;}
    public HybridCatalogs(string root):this(root,true) { }
    internal HybridCatalogs(string root,bool allowRemoteSearch)
    {
        this.allowRemoteSearch=allowRemoteSearch;
        var providers=new Catalogs();catalogs=providers.Sources.ToDictionary(x=>x.Key,x=>new HybridCatalog(x.Key,x.Value,Path.Combine(root,"catalog-index")));
        Sources=catalogs.ToDictionary(x=>x.Key,x=>(IModCatalog)x.Value);
        foreach(var (name,catalog) in catalogs) catalog.Changed+=()=>{lock(unifiedGate) unifiedCache=null;Changed?.Invoke(name);};
    }
    public IModCatalog Get(string name)=>Sources.TryGetValue(name,out var value)?value:Sources["GameBanana"];
    public List<string> Categories(string name,string kind)=>catalogs.TryGetValue(name,out var c)?c.Categories(kind):[];
    public Task<CatalogPage> Query(string name,string kind,string search,int page,string category,int sort,CancellationToken token)=>catalogs.GetValueOrDefault(name,catalogs["GameBanana"]).Query(kind,search,page,category,sort,token);
    List<UnifiedEntry> Unified() {lock(unifiedGate) return unifiedCache??=UnifiedCatalog.Merge(catalogs.Values.SelectMany(x=>x.Snapshot()));}
    public List<string> UnifiedCategories(string kind)=>UnifiedCatalog.Categories(Unified(),kind);
    public List<CatalogFilter> HeroFilters(string kind)=>CatalogTaxonomy.HeroFilters(Unified(),kind);
    public List<CatalogFilter> TypeFilters(string kind,string hero="")=>CatalogTaxonomy.TypeFilters(Unified().Where(x=>hero=="" || (hero=="__general"?x.Heroes.Count==0:x.Heroes.Contains(hero))),kind);
    public Task<CatalogPage> QueryUnified(string kind,string search,int page,string category,int sort,CancellationToken token,string hero="")
    {
        token.ThrowIfCancellationRequested();if(allowRemoteSearch) catalogs["GameBanana"].EnrichSearch(kind,CatalogTaxonomy.Search(search).RemoteText,token);
        return Task.FromResult(UnifiedCatalog.Query(Unified(),kind,search,page,category,sort,hero:hero));
    }
    public async Task Warm(CancellationToken token)
    {
        if(IsRefreshing) return;IsRefreshing=true;int errors=0;
        Status?.Invoke("Обновляем каталог в фоне…");
        var jobs=catalogs.Select(async pair=>
        {
            try
            {
                if(pair.Key=="GameBanana")
                {
                    var page=await new GameBanana().Browse("Mod","",1,token);await Task.Run(()=>pair.Value.Merge(page.Items),token);
                }
                else
                {
                    var provider=(CommunityCatalog)pair.Value.Origin;
                    var rows=await provider.Index(token);await Task.Run(()=>pair.Value.Merge(rows),token);
                    if(pair.Key=="DeadlockMods") await Task.Run(()=>catalogs["GameBanana"].Merge(rows.Where(x=>x.Id>0).Select(x=>x with {Provider="GameBanana",Key="",Url="https://gamebanana.com/"+(x.Kind=="Sound"?"sounds/":"mods/")+x.Id}).ToList()),token);
                }
            }
            catch(OperationCanceledException) { }
            catch(Exception) {Interlocked.Increment(ref errors);}
        });
        await Task.WhenAll(jobs);
        IsRefreshing=false;
        Status?.Invoke(errors==0?"Каталог обновлен. Поиск использует сохраненный индекс.":"Часть каталога пока недоступна. Используем сохраненные записи.");
        Changed?.Invoke("All");
    }
}
public sealed class HybridCatalog : IModCatalog
{
    public IModCatalog Origin {get;}
    readonly string source,file;
    readonly object gate=new();
    List<CatalogItem> rows=[];
    readonly Dictionary<string,(DateTimeOffset At,List<CatalogItem> Items)> remoteQueries=new(StringComparer.OrdinalIgnoreCase);
    CancellationTokenSource? request;
    string pendingQuery="";
    public event Action? Changed;
    static string Identity(CatalogItem item)=>item.Kind+":"+(item.Id>0?item.Id.ToString():item.Key);
    public HybridCatalog(string source,IModCatalog origin,string directory)
    {
        this.source=source;Origin=origin;Directory.CreateDirectory(directory);file=Path.Combine(directory,source+".json");
        try {if(File.Exists(file) && new FileInfo(file).Length<40*1024*1024) rows=JsonSerializer.Deserialize<List<CatalogItem>>(File.ReadAllText(file))??[];}catch {rows=[];}
    }
    public void Merge(List<CatalogItem> incoming)
    {
        lock(gate)
        {
            var merged=rows.GroupBy(Identity).ToDictionary(x=>x.Key,x=>x.Last());foreach(var row in incoming) merged[Identity(row)]=row;
            rows=merged.Values.Take(30000).ToList();ModStorage.AtomicWrite(file,JsonSerializer.SerializeToUtf8Bytes(rows));
        }
        Changed?.Invoke();
    }
    public List<string> Categories(string kind) {lock(gate) return rows.Where(x=>x.Kind==kind).Select(x=>x.Category).Where(x=>x!="").Distinct().OrderBy(x=>x).ToList();}
    public List<CatalogItem> Snapshot() {lock(gate) return rows.ToList();}
    public HashSet<string> RemoteMatches(string kind,string search) {lock(gate) return (remoteQueries.GetValueOrDefault(kind+":"+search.Trim()).Items??[]).Select(UnifiedCatalog.Identity).ToHashSet();}
    public void EnrichSearch(string kind,string search,CancellationToken token)
    {
        if(source!="GameBanana") return;
        string term=search.Trim(),query=kind+":"+term;
        if(term=="") {request?.Cancel();pendingQuery="";return;}
        lock(gate)
            if(remoteQueries.TryGetValue(query,out var hit) && hit.At>DateTimeOffset.UtcNow.AddMinutes(-10) || pendingQuery.Equals(query,StringComparison.OrdinalIgnoreCase) && request?.IsCancellationRequested==false) return;
        request?.Cancel();request?.Dispose();request=CancellationTokenSource.CreateLinkedTokenSource(token);pendingQuery=query;
        _ = Enrich(kind,term,query,request.Token);
    }
    public Task<CatalogPage> Browse(string kind,string search,int page,CancellationToken token)=>Query(kind,search,page,"",0,token);
    public async Task<CatalogPage> Query(string kind,string search,int page,string category,int sort,CancellationToken token)
    {
        string term=search.Trim(),query=kind+":"+term;
        bool empty;lock(gate) empty=rows.Count==0;
        if(empty) {var initial=await Origin.Browse(kind,term,page,token);await Task.Run(()=>Merge(initial.Items),token);}
        else if(source=="GameBanana" && term!="")
        {
            bool fresh;lock(gate) fresh=remoteQueries.TryGetValue(query,out var hit) && hit.At>DateTimeOffset.UtcNow.AddMinutes(-10);
            if(!fresh)
            {
                request?.Cancel();request?.Dispose();request=CancellationTokenSource.CreateLinkedTokenSource(token);
                _ = Enrich(kind,term,query,request.Token);
            }
        }
        List<CatalogItem> snapshot,remote;lock(gate) {snapshot=rows.ToList();remote=remoteQueries.GetValueOrDefault(query).Items??[];}
        var forced=remote.Select(Identity).ToHashSet();
        IEnumerable<CatalogItem> found=snapshot.Where(x=>x.Kind==kind && (term=="" || (x.Name+" "+x.Category+" "+x.Author).Contains(term,StringComparison.OrdinalIgnoreCase) || forced.Contains(Identity(x))));
        if(category!="") found=found.Where(x=>x.Category==category);
        found=sort switch {1=>found.OrderBy(x=>x.Name,StringComparer.CurrentCultureIgnoreCase),2=>found.OrderByDescending(x=>x.Downloads),3=>found.OrderByDescending(x=>x.Likes),_=>found.OrderByDescending(x=>x.Modified)};
        var all=found.ToList();return new(all.Skip((Math.Max(1,page)-1)*24).Take(24).ToList(),all.Count,24);
    }
    async Task Enrich(string kind,string term,string query,CancellationToken token)
    {
        try
        {
            var fresh=await Origin.Browse(kind,term,1,token);token.ThrowIfCancellationRequested();
            lock(gate) remoteQueries[query]=(DateTimeOffset.UtcNow,fresh.Items);
            await Task.Run(()=>Merge(fresh.Items),token);
        }
        catch(OperationCanceledException) { }
        catch { /* Keep instant local results when the remote search is temporarily unavailable. */ }
        finally {if(pendingQuery==query) pendingQuery="";}
    }
    public Task<ModDetails> Details(string kind,long id,CancellationToken token)=>Origin.Details(kind,id,token);
    public Task<ModDetails> Details(CatalogItem item,CancellationToken token)=>Origin.Details(item,token);
    public Task Download(RemoteFile file,string destination,IProgress<string> progress,CancellationToken token)=>Origin.Download(file,destination,progress,token);
}
