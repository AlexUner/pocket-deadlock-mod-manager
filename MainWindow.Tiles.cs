using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace PocketDeadlock;

public partial class MainWindow
{
    static IEnumerable<T> Descendants<T>(DependencyObject parent) where T:DependencyObject
    {
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++)
        {
            var child=VisualTreeHelper.GetChild(parent,i);if(child is T match) yield return match;
            foreach(var nested in Descendants<T>(child)) yield return nested;
        }
    }
    void CatalogTileKey(object sender,KeyEventArgs e)
    {
        if(Keyboard.Modifiers!=ModifierKeys.None || CatalogList.Items.Count==0) return;
        int columns=Descendants<CatalogTilePanel>(CatalogList).FirstOrDefault()?.Columns??1;
        int selected=Math.Max(0,CatalogList.SelectedIndex);
        int index=e.Key switch {Key.Left=>selected-1,Key.Right=>selected+1,Key.Up=>selected-columns,Key.Down=>selected+columns,Key.Home=>0,Key.End=>CatalogList.Items.Count-1,_=>-999};
        if(index==-999) return;
        index=Math.Clamp(index,0,CatalogList.Items.Count-1);CatalogList.SelectedIndex=index;CatalogList.ScrollIntoView(CatalogList.Items[index]);
        (CatalogList.ItemContainerGenerator.ContainerFromIndex(index) as ListBoxItem)?.Focus();e.Handled=true;
    }
    static async Task WaitFor(Func<bool> condition,string failure)
    {
        var deadline=DateTime.UtcNow.AddSeconds(26);
        while(!condition() && DateTime.UtcNow<deadline) await Task.Delay(100);
        if(!condition()) throw new IOException(failure);
    }
    async Task<string> ReviewTileControls(string preview)
    {
        await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ContextIdle);
        var panel=Descendants<CatalogTilePanel>(CatalogList).Single();
        var tiles=Descendants<CatalogThumbnail>(CatalogList).ToList();
        if(panel.Columns<2 || tiles.Count!=CatalogList.Items.Count) throw new IOException("Tile layout failed");
        await WaitFor(()=>tiles.Take(panel.Columns*2).Count(x=>x.HasImage)>=2,"Visible covers did not load");
        if(tiles[^1].HasImage) throw new IOException("Offscreen cover loaded before scrolling");
        var input=PresentationSource.FromVisual(CatalogList)!;
        CatalogList.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,input,0,Key.Right){RoutedEvent=Keyboard.PreviewKeyDownEvent});
        if(CatalogList.SelectedIndex!=1) throw new IOException("Right arrow tile selection failed");
        CatalogList.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,input,0,Key.Down){RoutedEvent=Keyboard.PreviewKeyDownEvent});
        if(CatalogList.SelectedIndex!=1+panel.Columns) throw new IOException("Down arrow tile selection failed");
        var viewer=Descendants<ScrollViewer>(CatalogList).First();viewer.ScrollToBottom();
        await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ContextIdle);
        await WaitFor(()=>tiles.Skip(tiles.Count-panel.Columns).Any(x=>x.HasImage),"Scrolling did not load the last covers");
        CatalogList.SelectedIndex=0;viewer.ScrollToTop();lastDetail=ShowDetail((CatalogItem)CatalogList.Items[0]);
        await lastDetail;await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ContextIdle);
        int loaded=tiles.Count(x=>x.HasImage);
        return "PASS: adaptive tile columns "+panel.Columns+"\nPASS: visible images load; offscreen images wait\nPASS: Right/Down keyboard tile selection\nPASS: scrolling loads last covers\nCovers loaded after scrolling: "+loaded+"\n";
    }
}
