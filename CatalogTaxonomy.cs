using System.Text;
using System.Text.RegularExpressions;

namespace PocketDeadlock;

public sealed record HeroDefinition(string Name,string Russian,string Aliases);
public sealed record ModTypeDefinition(string Id,string Russian,string English,string Aliases);
public sealed record CatalogSearch(string Hero,List<string> Words,string RemoteText);
public sealed record CatalogFilter(string Value,string Name,int Count)
{
    public string Caption=>Count<0?Name:$"{Name} · {Count}";
}

public static class CatalogTaxonomy
{
    static readonly HeroDefinition[] Heroes=[
        new("Abrams","Абрамс","эйбрамс"),new("Apollo","Аполло","аполлон"),new("Bebop","Бибоп","бебоп"),
        new("Billy","Билли",""),new("Calico","Калико","калико"),new("Celeste","Селеста","целеста celest"),
        new("Doorman","Привратник","the doorman|дурман|дворецкий"),new("Drifter","Скиталец","дрифтер"),new("Dynamo","Динамо","динамо"),
        new("Graves","Грейвс","грейвз"),new("Grey Talon","Серый Коготь","gray talon|грей талон|серый коготь|талон"),
        new("Haze","Хейз","хэйз хейз"),new("Holliday","Холлидей","holiday холлидай"),new("Infernus","Инфернус","инфернус"),
        new("Ivy","Айви","иви"),new("Kelvin","Кельвин","кельвин"),new("Lady Geist","Леди Гайст","lady geist|леди гайст|леди гейст|гайст|гейст"),
        new("Lash","Лэш","леш лэш лаш"),new("McGinnis","Макгиннис","макгиннис макгинис"),new("Mina","Мина","мина"),
        new("Mirage","Мираж","мираж"),new("Mo & Krill","Мо и Крилл","mo and krill|mo krill|мо и крилл|мо крилл|мокрилл"),
        new("Paige","Пейдж","page пейдж пэйдж пейж пэйж"),new("Paradox","Парадокс","парадокс"),new("Pocket","Покет","покет поккет"),
        new("Rem","Рем","рем рэм"),new("Seven","Севен","седьмой севен семерка 7"),new("Shiv","Шив","шив"),
        new("Silver","Сильвер","сильвер"),new("Sinclair","Синклер","синклер синклайр"),new("Venator","Венатор","венатор"),
        new("Victor","Виктор","виктор"),new("Vindicta","Виндикта","виндикта"),new("Viscous","Вискус","вязкий вискус"),
        new("Vyper","Вайпер","viper вайпер"),new("Warden","Варден","варден надзиратель"),new("Wraith","Рейф","рэйф рейф врейф врайт"),new("Yamato","Ямато","ямато")
    ];
    static readonly ModTypeDefinition[] Types=[
        new("appearance","Скины и модели","Skins and models","skins|skin|model replacement|models|cosmetics|скины|модели"),
        new("interface","Интерфейс","Interface","hud|ui|interface|icons|portraits|crosshair|интерфейс|иконки|прицел"),
        new("effects","Эффекты и способности","Effects and abilities","abilities|particles|effects|эффекты|способности"),
        new("animations","Анимации","Animations","animations|animation|анимации"),
        new("weapons","Оружие","Weapons","weapons|weapon|оружие"),
        new("music","Музыка","Music","in game music|music|музыка"),
        new("voices","Озвучка","Voice","vos|voice|voices|voice lines|озвучка|голос"),
        new("sounds","Звуковые эффекты","Sound effects","item sounds|gameplay events|sounds|sound|audio|звуки"),
        new("qol","Удобство игры","Quality of life","quality of life|fixes|gameplay modifications|qol|исправления|удобство"),
        new("maps","Карты","Maps","maps|map|карты"),new("other","Другое","Other","other|misc|другое")
    ];
    static readonly Dictionary<string,string> HeroAliases=BuildAliases();
    public static string Normalize(string text)=>Regex.Replace(text.Normalize(NormalizationForm.FormKC).ToLowerInvariant().Replace('ё','е'),@"[^\p{L}\p{N}]+"," ").Trim();
    static Dictionary<string,string> BuildAliases()
    {
        var result=new Dictionary<string,string>(StringComparer.Ordinal);
        foreach(var hero in Heroes)
        {
            result[Normalize(hero.Name)]=hero.Name;result[Normalize(hero.Russian)]=hero.Name;
            foreach(string alias in hero.Aliases.Contains('|')?hero.Aliases.Split('|'):hero.Aliases.Split(' ',StringSplitOptions.RemoveEmptyEntries)) result[Normalize(alias)]=hero.Name;
        }
        return result;
    }
    public static string ResolveHero(string value,bool typo=false)
    {
        string input=Normalize(value);if(HeroAliases.TryGetValue(input,out var hero)) return hero;
        if(typo && input.Length>=4 && !input.Contains(' '))
        {
            var close=Heroes.Where(x=>OneEdit(input,Normalize(x.Name))).Select(x=>x.Name).ToList();if(close.Count==1) return close[0];
        }
        return "";
    }
    static bool OneEdit(string a,string b)
    {
        if(Math.Abs(a.Length-b.Length)>1) return false;
        int i=0,j=0,difference=0;
        while(i<a.Length && j<b.Length)
        {
            if(a[i]==b[j]) {i++;j++;continue;}
            if(++difference>1) return false;
            if(a.Length>=b.Length) i++;if(b.Length>=a.Length) j++;
        }
        return difference+(a.Length-i)+(b.Length-j)<=1;
    }
    public static CatalogSearch Search(string input)
    {
        string normalized=Normalize(input),exact=ResolveHero(normalized,true);
        if(exact!="") return new(exact,[],exact);
        foreach(var alias in HeroAliases.OrderByDescending(x=>x.Key.Length))
        {
            if(!Regex.IsMatch(normalized,@"(?:^| )"+Regex.Escape(alias.Key)+@"(?: |$)")) continue;
            string remainder=Regex.Replace(normalized,@"(?<!\S)"+Regex.Escape(alias.Key)+@"(?!\S)"," ");
            var words=remainder.Split(' ',StringSplitOptions.RemoveEmptyEntries).Where(x=>x is not ("for" or "on" or "mod" or "mods" or "на" or "для" or "мод" or "моды")).ToList();
            return new(alias.Value,words,string.Join(' ',new[]{alias.Value}.Concat(words)));
        }
        return new("",normalized.Split(' ',StringSplitOptions.RemoveEmptyEntries).ToList(),input.Trim());
    }
    public static List<string> EntryHeroes(List<CatalogItem> aliases)
    {
        var explicitNames=aliases.SelectMany(x=>x.Hero.Split(',').Concat(Regex.Split(x.Category,@"\s*(?:·|/|>)\s*"))).Select(x=>ResolveHero(x)).Where(x=>x!="").Distinct().ToList();
        if(explicitNames.Count>0) return explicitNames;
        var titles=aliases.Select(x=>" "+Normalize(x.Name)+" ").ToList();
        return Heroes.Where(hero=>titles.Any(x=>x.Contains(" "+Normalize(hero.Name)+" "))).Select(x=>x.Name).ToList();
    }
    public static string EntryType(List<CatalogItem> aliases)
    {
        foreach(var item in aliases.OrderBy(x=>x.Provider=="GameBanana"?0:1))
        {
            foreach(string raw in new[]{item.ModType,item.Category})
            {
                string value=Normalize(raw);var type=Types.FirstOrDefault(x=>x.Id!="other" && (x.Id==raw || x.Aliases.Split('|').Any(a=>value==a || value.StartsWith(a+" "))));
                if(type!=null) return item.Kind=="Sound" && type.Id=="effects"?"sounds":type.Id;
            }
        }
        return "other";
    }
    public static string HeroLabel(string name)
    {
        var hero=Heroes.FirstOrDefault(x=>x.Name==name);return L.Language=="ru" && hero!=null?$"{hero.Russian} ({hero.Name})":name;
    }
    public static string TypeLabel(string id)
    {
        var type=Types.FirstOrDefault(x=>x.Id==id)??Types[^1];return L.Language=="ru"?type.Russian:type.English;
    }
    public static string SearchText(List<CatalogItem> aliases,string type)
    {
        var definition=Types.First(x=>x.Id==type);
        return Normalize(string.Join(' ',aliases.Select(x=>x.Name+" "+x.Category+" "+x.Author))+" "+definition.English+" "+definition.Russian+" "+definition.Aliases);
    }
    public static bool MatchesHeroChoice(string name,string term)=>Normalize(HeroLabel(name)).Contains(Normalize(term)) || ResolveHero(term,true)==name || HeroAliases.Any(x=>x.Value==name && x.Key.Contains(Normalize(term)));
    public static bool MatchesTypeChoice(string id,string term)
    {
        var type=Types.FirstOrDefault(x=>x.Id==id);string value=Normalize(term);
        return type!=null && Normalize(type.Russian+" "+type.English+" "+type.Aliases).Contains(value);
    }
    public static List<CatalogFilter> HeroFilters(IEnumerable<UnifiedEntry> rows,string kind)
    {
        var items=rows.Where(x=>kind=="" || x.Item.Kind==kind).ToList();
        var result=items.SelectMany(x=>x.Heroes.Select(hero=>new{hero,x.Item})).GroupBy(x=>x.hero).Select(x=>new CatalogFilter(x.Key,HeroLabel(x.Key),x.Count())).OrderBy(x=>x.Name).ToList();
        int general=items.Count(x=>x.Heroes.Count==0);if(general>0) result.Insert(0,new("__general",L.T("Без привязки к герою"),general));return result;
    }
    public static List<CatalogFilter> TypeFilters(IEnumerable<UnifiedEntry> rows,string kind)=>rows.Where(x=>kind=="" || x.Item.Kind==kind).GroupBy(x=>x.Type).Select(x=>new CatalogFilter("type:"+x.Key,TypeLabel(x.Key),x.Count())).OrderBy(x=>x.Name).ToList();
    public static string Caption(CatalogItem item)
    {
        var heroes=item.Hero==""?EntryHeroes([item]):item.Hero.Split(',').ToList();string type=item.ModType!="" && Types.Any(x=>x.Id==item.ModType)?item.ModType:EntryType([item]);
        return string.Join(" / ",new[]{TypeLabel(type)}.Concat(heroes.Select(HeroLabel)).Concat(item.Author==""?[]:new[]{item.Author}));
    }
}
