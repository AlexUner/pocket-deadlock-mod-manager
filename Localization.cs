using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;
using Microsoft.Win32;

namespace PocketDeadlock;

public static class L
{
    static readonly string SystemLanguage=CultureInfo.CurrentUICulture.TwoLetterISOLanguageName=="ru"?"ru":"en";
    public static string Language {get;private set;}=SystemLanguage;
    static readonly Dictionary<string,string> resources=[];
    static readonly (string Ru,string En)[] Pairs=Translations.Data.Split('\n',StringSplitOptions.RemoveEmptyEntries).Select(x=>x.TrimEnd('\r').Split('|',2)).Where(x=>x.Length==2).Select(x=>(x[0],x[1])).OrderByDescending(x=>x.Item1.Length).ToArray();
    static readonly (string Ru,string En)[] EnglishPairs=Pairs.OrderByDescending(x=>x.En.Length).ToArray();
    public static string T(string text)
    {
        if(Language=="ru") return text;
        foreach(var (ru,en) in Pairs) text=text.Replace(ru,en,StringComparison.Ordinal);
        return text;
    }
    // Only manager-authored status text can be stored in either interface language.
    public static string UiStatus(string text)
    {
        if(Language!="ru") return T(text);
        foreach(var (ru,en) in EnglishPairs) text=text.Replace(en,ru,StringComparison.Ordinal);
        return text;
    }
    public static void Set(string preference)
    {
        Language=preference is "ru" or "en"?preference:SystemLanguage;
        CultureInfo.CurrentCulture=CultureInfo.GetCultureInfo(Language=="ru"?"ru-RU":"en-US");
        CultureInfo.CurrentUICulture=CultureInfo.CurrentCulture;
        if(Application.Current!=null) foreach(var (key,text) in resources) Application.Current.Resources[key]=T(text);
    }
    public static string Resource(string text)
    {
        string key="loc."+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..20];
        resources[key]=text;Application.Current.Resources[key]=T(text);return key;
    }
    public static void Theme(string preference)
    {
        bool light=preference=="light" || preference=="system" && (Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize","AppsUseLightTheme",0) as int?)==1;
        var colors=light?new[]{"#F0F3F0","#FAFCFA","#6B806F","#19261D","#4D6251","#CFE19A","#E1E8DF","#E7EDDF"}:new[]{"#151A19","#1E2623","#485A4E","#F0EFE6","#B8C3B9","#D5E998","#101613","#303D2D"};
        string[] keys=["Canvas","Panel","Line","Ink","Muted","Accent","Input","Selection"];
        for(int i=0;i<keys.Length;i++) Application.Current.Resources[keys[i]]=new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[i]));
    }
}
[MarkupExtensionReturnType(typeof(object))]
public sealed class I18nExtension : MarkupExtension
{
    public string Text {get;set;}="";
    public override object ProvideValue(IServiceProvider provider)
    {
        var target=(IProvideValueTarget?)provider.GetService(typeof(IProvideValueTarget));
        if(target?.TargetProperty is not DependencyProperty property || property.Name=="Name" && property!=System.Windows.Automation.AutomationProperties.NameProperty) return L.T(Text);
        return new DynamicResourceExtension(L.Resource(Text)).ProvideValue(provider);
    }
}
