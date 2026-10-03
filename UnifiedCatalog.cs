using System.Text.RegularExpressions;

namespace PocketDeadlock;

public sealed record UnifiedEntry(CatalogItem Item,List<CatalogItem> Aliases)
{
    public List<string> Heroes {get;init;}=[];
    public string Type {get;init;}="other";
    public string SearchText {get;init;}="";
}

public static class UnifiedCatalog
{
    public static string Identity(CatalogItem item)=>item.Kind+":"+(item.Id>0?"gb:"+item.Id:item.Provider+":"+(item.Key!=""?item.Key:item.Url));
    public static string Category(string value)
    {
        var parts=new List<string>();
        foreach(string part in Regex.Split(value,@"\s*(?:·|/|>)\s*").Select(x=>x.Trim()).Where(x=>x!=""))
            if(parts.Count==0 || !parts[^1].Equals(part,StringComparison.OrdinalIgnoreCase)) parts.Add(part);
        return string.Join(" / ",parts);
    }
    static int Preference(CatalogItem item)=>item.Provider switch {"GameBanana"=>0,"DeadlockMods"=>1,_=>2};
    public static List<UnifiedEntry> Merge(IEnumerable<CatalogItem> records)=>records.GroupBy(Identity).Select(group=>
    {
        var aliases=group.ToList();var preferred=aliases.OrderBy(Preference).ThenByDescending(x=>x.Modified).First();
        var heroes=CatalogTaxonomy.EntryHeroes(aliases);string type=CatalogTaxonomy.EntryType(aliases);
        var item=preferred with {Category=Category(preferred.Category),Downloads=aliases.Max(x=>x.Downloads),Likes=aliases.Max(x=>x.Likes),DownloadsKnown=aliases.Any(x=>x.HasDownloads),LikesKnown=aliases.Any(x=>x.HasLikes),Hero=string.Join(',',heroes),ModType=type,
            ContentRatings=string.Join(", ",aliases.Select(x=>x.ContentRatings).Where(x=>x!="").Distinct()),
            InitialVisibility=preferred.InitialVisibility!=""?preferred.InitialVisibility:aliases.Any(x=>x.InitialVisibility=="hide")?"hide":aliases.Any(x=>x.InitialVisibility=="warn")?"warn":""};
        return new UnifiedEntry(item,aliases){Heroes=heroes,Type=type,SearchText=CatalogTaxonomy.SearchText(aliases,type)};
    }).ToList();
    public static List<string> Categories(IEnumerable<UnifiedEntry> rows,string kind)=>rows.Where(x=>kind=="" || x.Item.Kind==kind).Select(x=>x.Item.Category).Where(x=>x!="").Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x=>x,StringComparer.CurrentCultureIgnoreCase).ToList();
    public static CatalogPage Query(IEnumerable<UnifiedEntry> entries,string kind,string search,int page,string category,int sort,HashSet<string>? remote=null,int perPage=24,string hero="")
    {
        var plan=CatalogTaxonomy.Search(search);
        var found=entries.Where(x=>(kind=="" || x.Item.Kind==kind) && (category=="" || (category.StartsWith("type:")?x.Type==category[5..]:x.Item.Category.Equals(category,StringComparison.OrdinalIgnoreCase))) &&
            (hero=="" || (hero=="__general"?x.Heroes.Count==0:x.Heroes.Contains(hero))) &&
            (plan.Hero=="" || x.Heroes.Contains(plan.Hero)) &&
            plan.Words.All(word=>x.SearchText.Contains(word))).Select(x=>x.Item);
        var ordered=sort switch {1=>found.OrderBy(x=>x.Name,StringComparer.CurrentCultureIgnoreCase),2=>found.OrderByDescending(x=>x.Downloads),3=>found.OrderByDescending(x=>x.Likes),_=>found.OrderByDescending(x=>x.Modified)};
        var all=ordered.ThenBy(x=>Identity(x),StringComparer.Ordinal).ToList();
        return new(all.Skip((Math.Max(1,page)-1)*perPage).Take(perPage).ToList(),all.Count,perPage);
    }
}
