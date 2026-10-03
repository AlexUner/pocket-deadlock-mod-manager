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
#if DIAGNOSTICS
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
        if(panel.Columns<2 || CatalogList.Items.Count!=total || total<=24 || tiles.Count>=total || tiles.Count>40) throw new IOException("Infinite feed virtualization failed");
        if(details!=null || CatalogList.SelectedIndex!=-1 || lastDetail!=null) throw new IOException("Details loaded without a card selection");
        await WaitFor(()=>tiles.Take(panel.Columns*2).Count(x=>x.HasImage)>=2,"Visible covers did not load");
        await WaitFor(()=>tiles.Skip(panel.Columns*2).Any(x=>x.HasImage),"Upcoming covers were not prefetched");
        SaveControl(Workspace,preview+".start.png");
        var viewer=Descendants<ScrollViewer>(CatalogList).First();
        viewer.ScrollToVerticalOffset(panel.RowStride*16);
        await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ContextIdle);
        int middle=(int)(panel.VerticalOffset/panel.RowStride)*panel.Columns;
        if(middle<=24 || CatalogList.ItemContainerGenerator.ContainerFromIndex(middle)==null || CatalogList.ItemContainerGenerator.ContainerFromIndex(0)!=null) throw new IOException("Continuous scroll did not cross the old page boundary");
        int realized=Descendants<CatalogThumbnail>(CatalogList).Count();
        if(realized>40) throw new IOException("Scrolling created too many tile containers");
        var anchor=UnifiedCatalog.Identity((CatalogItem)CatalogList.Items[middle]);
        var sample=((CatalogItem)CatalogList.Items[0]) with {Id=999999001,Name="Paige feed indexing fixture",Modified=long.MaxValue};
        ((HybridCatalog)sources.Sources["GameBanana"]).Merge([sample]);
        await RefreshIndexedResults();await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ContextIdle);
        int anchored=(int)(panel.VerticalOffset/panel.RowStride)*panel.Columns;
        if(!CatalogList.Items.Cast<CatalogItem>().Skip(anchored).Take(panel.Columns).Any(x=>UnifiedCatalog.Identity(x)==anchor) || details!=null) throw new IOException("Background indexing moved the feed or loaded details");
        viewer.ScrollToBottom();await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ContextIdle);
        if(CatalogList.ItemContainerGenerator.ContainerFromIndex(CatalogList.Items.Count-1)==null) throw new IOException("Last result is not reachable without pagination");
        await WaitFor(()=>Descendants<CatalogThumbnail>(CatalogList).Any(x=>x.HasImage),"Scrolling did not load the last covers");
        SaveControl(Workspace,preview+".end.png");
        ((HybridCatalog)sources.Sources["GameBanana"]).Merge([sample with {Name="Feed indexing fixture",Hero="Haze",Category="Haze"}]);
        SearchBox.Text="zz-no-feed-results";await SearchNow();
        if(CatalogList.Items.Count!=0 || EmptyLabel.Visibility!=Visibility.Visible) throw new IOException("Empty feed search failed");
        SearchBox.Text="page";await SearchNow();await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ContextIdle);
        if(panel.VerticalOffset!=0 || CatalogList.SelectedIndex!=-1 || details!=null) throw new IOException("New search did not reset the feed and details");
        var input=PresentationSource.FromVisual(CatalogList)!;
        CatalogList.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,input,0,Key.Right){RoutedEvent=Keyboard.PreviewKeyDownEvent});
        if(CatalogList.SelectedIndex!=1) throw new IOException("Right arrow tile selection failed");
        CatalogList.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,input,0,Key.Down){RoutedEvent=Keyboard.PreviewKeyDownEvent});
        if(CatalogList.SelectedIndex!=1+panel.Columns) throw new IOException("Down arrow tile selection failed");
        CatalogList.SelectedIndex=0;viewer.ScrollToTop();
        if(lastDetail!=null) await lastDetail;
        await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ContextIdle);
        if(details==null || FilesBox.Items.Count==0) throw new IOException("Card selection did not load download variants");
        return "PASS: infinite feed exposes all "+total+" indexed results\nPASS: adaptive tile columns "+panel.Columns+"\nPASS: bounded visual tree after scrolling: "+realized+" tiles\nPASS: next-row covers are prefetched\nPASS: scroll crosses the old page boundary and reaches the final result\nPASS: indexing preserves the visible anchor and does not fetch details\nPASS: empty search and new search reset scroll\nPASS: Right/Down keyboard tile selection\nPASS: details and variants load only after selection\n";
    }
#endif
}
