using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace PocketDeadlock;

internal static partial class SelfTests
{
    static IEnumerable<T> VisualChildren<T>(DependencyObject root) where T : DependencyObject
    {
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)
        {
            var child=VisualTreeHelper.GetChild(root,i);
            if(child is T value) yield return value;
            foreach(var nested in VisualChildren<T>(child)) yield return nested;
        }
    }
    static async Task TemplateLocalizationTests(string run,Action<bool,string> assert)
    {
        string previous=L.Language;
        var source=new MainWindow(new ModStorage(Path.Combine(run,"template-data")),Path.Combine(run,"not-rendered.png"));
        var library=(ListBox)source.FindName("LibraryList");
        var catalog=(ListBox)source.FindName("CatalogList");
        var fixture=new CatalogItem(1,"Mod","Fixture","Author","UI","","",1,Downloads:1234,Likes:27);
        var list=new ListBox {ItemTemplate=library.ItemTemplate,ItemsSource=new[]{new LibraryMod {Name="Fixture",Enabled=true}}};
        var host=new Window {Content=list,Width=480,Height=300,ShowInTaskbar=false};
        try
        {
            L.Set("ru");host.Show();host.UpdateLayout();
            await host.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ContextIdle);
            var check=VisualChildren<CheckBox>(list).Single();
            assert(AutomationProperties.GetName(check)=="Использовать этот мод" && check.IsChecked==true,"compiled library template renders localized checkbox without changing selection");
            L.Set("en");await host.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ContextIdle);
            assert(AutomationProperties.GetName(check)=="Use this mod" && (string)check.ToolTip=="Include in loadout","realized template accessibility and tooltip update to English");
            L.Set("ru");await host.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ContextIdle);
            assert(AutomationProperties.GetName(check)=="Использовать этот мод","realized template returns to Russian without a XAML exception");
            list.ItemsSource=null;list.ItemTemplate=catalog.ItemTemplate;list.ItemsSource=new[]{fixture};host.UpdateLayout();
            await host.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ContextIdle);
            var texts=VisualChildren<TextBlock>(list).ToList();
            assert(texts.Any(x=>x.Text==fixture.DownloadsDisplay) && texts.Any(x=>x.Text==fixture.LikesDisplay),"compiled catalog card displays download count and star likes count");
            assert(texts.Any(x=>AutomationProperties.GetName(x)==fixture.DownloadsHelp) && texts.Any(x=>AutomationProperties.GetName(x)==fixture.LikesHelp),"card popularity counters expose their meanings to accessibility");
            var unknown=fixture with {Downloads=0,Likes=0};list.ItemsSource=new[]{unknown};host.UpdateLayout();
            await host.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ContextIdle);
            texts=VisualChildren<TextBlock>(list).ToList();
            assert(texts.Any(x=>x.Text=="↓ —") && texts.Any(x=>x.Text=="★ —"),"compiled catalog template shows unknown counts without invented zero values");
        }
        finally {host.Close();source.Close();L.Set(previous);}
    }
}
