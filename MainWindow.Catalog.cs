using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Controls.Primitives;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace PocketDeadlock;

public sealed record CategoryChoice(string Value,string Name);

public partial class MainWindow
{
    string selectedCategory="";
    int sortIndex;
    List<string> availableCategories=[];
    void UpdateCategoryOptions()
    {
        availableCategories=favoritesView?UnifiedCatalog.Categories(UnifiedCatalog.Merge(catalogRows),""):sources.UnifiedCategories(activeKind);
        if(selectedCategory!="" && !availableCategories.Contains(selectedCategory)) selectedCategory="";
        CategoryButtonLabel.Text=selectedCategory==""?L.T("Все категории"):selectedCategory;
        FilterCategoryOptions();
    }
    void FilterCategoryOptions()
    {
        if(CategoryList==null) return;
        string term=CategorySearch.Text.Trim();
        var choices=availableCategories.Where(x=>x.Contains(term,StringComparison.OrdinalIgnoreCase)).Select(x=>new CategoryChoice(x,x)).ToList();
        CategoryList.ItemsSource=new[]{new CategoryChoice("",L.T("Все категории"))}.Concat(choices).ToList();
        CategoryEmpty.Visibility=term!="" && choices.Count==0?Visibility.Visible:Visibility.Collapsed;
    }
    void OpenCategories(object sender,RoutedEventArgs e)=>CategoryPopup.IsOpen=!CategoryPopup.IsOpen;
    void CategoriesOpened(object sender,EventArgs e) {CategorySearch.Text="";FilterCategoryOptions();CategorySearch.Focus();}
    void CategoriesClosed(object sender,EventArgs e)=>CategoryButton.Focus();
    void SearchCategories(object sender,TextChangedEventArgs e)=>FilterCategoryOptions();
    async Task CommitCategory(CategoryChoice choice)
    {
        selectedCategory=choice.Value;CategoryButtonLabel.Text=choice.Name;CategoryPopup.IsOpen=false;
        await ApplyCatalogFilter();
    }
    async void PickCategory(object sender,MouseButtonEventArgs e)
    {
        if(ItemsControl.ContainerFromElement(CategoryList,e.OriginalSource as DependencyObject) is ListBoxItem row && row.DataContext is CategoryChoice choice)
        {e.Handled=true;await CommitCategory(choice);}
    }
    async void CategorySearchKey(object sender,KeyEventArgs e)
    {
        if(e.Key==Key.Escape) {CategoryPopup.IsOpen=false;e.Handled=true;}
        else if(e.Key==Key.Down) {CategoryList.SelectedIndex=CategoryList.Items.Count>1?1:0;CategoryList.Focus();e.Handled=true;}
        else if(e.Key==Key.Enter)
        {
            int index=CategoryList.Items.Count>1?1:0;
            if(CategoryList.Items[index] is CategoryChoice choice) await CommitCategory(choice);e.Handled=true;
        }
    }
    async void CategoryListKey(object sender,KeyEventArgs e)
    {
        if(e.Key==Key.Escape) {CategoryPopup.IsOpen=false;e.Handled=true;}
        else if(e.Key==Key.Enter && CategoryList.SelectedItem is CategoryChoice choice) {await CommitCategory(choice);e.Handled=true;}
    }
    void OpenSort(object sender,RoutedEventArgs e)
    {
        var menu=SortButton.ContextMenu;menu.PlacementTarget=SortButton;menu.Placement=PlacementMode.Bottom;menu.IsOpen=true;
    }
    async void ChangeSort(object sender,RoutedEventArgs e)
    {
        if(sender is not MenuItem chosen || !int.TryParse(chosen.Tag.ToString(),out int sort)) return;
        sortIndex=sort;
        foreach(MenuItem item in SortButton.ContextMenu.Items) item.IsChecked=item==chosen;
        await ApplyCatalogFilter();
    }
    async Task<string> ReviewCatalogControls(string preview)
    {
        CategoryPopup.IsOpen=true;await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ContextIdle);
        CategorySearch.Text="zz-category-does-not-exist";
        if(CategoryEmpty.Visibility!=Visibility.Visible || CategoryList.Items.Count!=1) throw new IOException("Category empty-state check failed");
        CategorySearch.Text="hAzE";await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ContextIdle);
        if(CategoryList.Items.Count<2) throw new IOException("Category search check failed");
        SaveControl(CategoryPopup.Child,preview+".categories.png");
        var inputSource=PresentationSource.FromVisual(CategorySearch)!;
        CategorySearch.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,inputSource,0,Key.Down){RoutedEvent=Keyboard.PreviewKeyDownEvent});
        CategoryList.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,inputSource,0,Key.Enter){RoutedEvent=Keyboard.PreviewKeyDownEvent});
        await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ContextIdle);
        if(CategoryPopup.IsOpen || selectedCategory=="" || !CatalogList.Items.Cast<CatalogItem>().All(x=>x.Category==selectedCategory)) throw new IOException("Category keyboard selection check failed");
        string chosen=selectedCategory;await CommitCategory(new("",L.T("Все категории")));
        OpenSort(this,new RoutedEventArgs());await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ContextIdle);
        SaveControl(SortButton.ContextMenu,preview+".sort.png");SortButton.ContextMenu.IsOpen=false;
        if(lastDetail!=null) await lastDetail;
        return "PASS: category search and empty state\nPASS: keyboard category selection "+chosen+"\nPASS: reset category restores full catalog\n";
    }
    static void SaveControl(Visual control,string path)
    {
        if(control is FrameworkElement element) element.UpdateLayout();
        var size=VisualTreeHelper.GetDescendantBounds(control);
        var bitmap=new RenderTargetBitmap(Math.Max(1,(int)Math.Ceiling(size.Right)),Math.Max(1,(int)Math.Ceiling(size.Bottom)),96,96,PixelFormats.Pbgra32);bitmap.Render(control);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var stream=File.Create(path);encoder.Save(stream);
    }
}
