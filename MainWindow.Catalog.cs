using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Controls.Primitives;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace PocketDeadlock;

public partial class MainWindow
{
    string selectedCategory="",selectedHero="";
    int sortIndex;
    bool editingHero;
    List<CatalogFilter> availableHeroes=[],availableTypes=[];
    Task? filterChange;
    void UpdateCategoryOptions()
    {
        var favorites=favoritesView?UnifiedCatalog.Merge(catalogRows):null;
        availableHeroes=favorites!=null?CatalogTaxonomy.HeroFilters(favorites.Where(x=>CatalogContent.Matches(x.Item,contentFilter,App.Account?.SignedIn==true)),""):sources.HeroFilters(activeKind,contentFilter);
        if(selectedHero!="" && !availableHeroes.Any(x=>x.Value==selectedHero)) selectedHero="";
        string typeHero=selectedHero==""?CatalogTaxonomy.Search(activeSearch).Hero:selectedHero;
        availableTypes=favorites!=null?CatalogTaxonomy.TypeFilters(favorites.Where(x=>CatalogContent.Matches(x.Item,contentFilter,App.Account?.SignedIn==true) && (typeHero=="" || (typeHero=="__general"?x.Heroes.Count==0:x.Heroes.Contains(typeHero)))),""):sources.TypeFilters(activeKind,typeHero,contentFilter);
        if(selectedCategory!="" && !availableTypes.Any(x=>x.Value==selectedCategory)) selectedCategory="";
        HeroButtonLabel.Text=availableHeroes.FirstOrDefault(x=>x.Value==selectedHero)?.Name??L.T("Все герои");
        CategoryButtonLabel.Text=availableTypes.FirstOrDefault(x=>x.Value==selectedCategory)?.Name??L.T("Все типы модов");
        ResetFiltersButton.Visibility=selectedHero!="" || selectedCategory!="" || contentFilter!="all"?Visibility.Visible:Visibility.Collapsed;
        FilterCategoryOptions();
    }
    void FilterCategoryOptions()
    {
        if(CategoryList==null) return;
        string term=CategorySearch.Text.Trim();
        var choices=(editingHero?availableHeroes:availableTypes).Where(x=>term=="" || (editingHero?CatalogTaxonomy.MatchesHeroChoice(x.Value,term):CatalogTaxonomy.MatchesTypeChoice(x.Value[5..],term)) || CatalogTaxonomy.Normalize(x.Name).Contains(CatalogTaxonomy.Normalize(term))).ToList();
        CategoryList.ItemsSource=new[]{new CatalogFilter("",L.T(editingHero?"Все герои":"Все типы модов"),-1)}.Concat(choices).ToList();
        CategoryEmpty.Visibility=term!="" && choices.Count==0?Visibility.Visible:Visibility.Collapsed;
    }
    void OpenHeroes(object sender,RoutedEventArgs e)=>OpenFilter(true);
    void OpenCategories(object sender,RoutedEventArgs e)=>OpenFilter(false);
    void OpenFilter(bool heroes)
    {
        if(CategoryPopup.IsOpen) {CategoryPopup.IsOpen=false;if(editingHero==heroes) return;}
        editingHero=heroes;CategoryPopup.PlacementTarget=heroes?HeroButton:CategoryButton;
        CategorySearch.Tag=L.T(heroes?"Найти героя":"Найти тип мода");CategoryEmpty.Text=L.T(heroes?"Герои не найдены":"Типы модов не найдены");
        AutomationProperties.SetName(CategorySearch,L.T(heroes?"Найти героя":"Найти тип мода"));
        AutomationProperties.SetName(CategoryList,L.T(heroes?"Герои":"Типы модов"));
        CategoryPopup.IsOpen=true;
    }
    void CategoriesOpened(object sender,EventArgs e) {CategorySearch.Text="";FilterCategoryOptions();CategorySearch.Focus();}
    void CategoriesClosed(object sender,EventArgs e)=>(editingHero?HeroButton:CategoryButton).Focus();
    void SearchCategories(object sender,TextChangedEventArgs e)=>FilterCategoryOptions();
    async Task CommitCategory(CatalogFilter choice)
    {
        if(editingHero)
        {
            selectedHero=choice.Value;
            var plan=CatalogTaxonomy.Search(activeSearch);
            if(plan.Hero!="" && plan.Words.Count==0 && plan.Hero!=selectedHero) {activeSearch="";SearchBox.Text="";searchDelay?.Stop();}
        }
        else selectedCategory=choice.Value;
        UpdateCategoryOptions();CategoryPopup.IsOpen=false;
        await ApplyCatalogFilter();
    }
    async void ResetCatalogFilters(object sender,RoutedEventArgs e)=>await ResetFilters();
    async Task ResetFilters() {selectedHero=selectedCategory="";settingFilters=true;ContentFilterBox.SelectedIndex=0;contentFilter="all";settingFilters=false;UpdateCategoryOptions();await ApplyCatalogFilter();}
    async void PickCategory(object sender,MouseButtonEventArgs e)
    {
        if(ItemsControl.ContainerFromElement(CategoryList,e.OriginalSource as DependencyObject) is ListBoxItem row && row.DataContext is CatalogFilter choice)
        {e.Handled=true;filterChange=CommitCategory(choice);await filterChange;}
    }
    async void CategorySearchKey(object sender,KeyEventArgs e)
    {
        if(e.Key==Key.Escape) {CategoryPopup.IsOpen=false;e.Handled=true;}
        else if(e.Key==Key.Down) {CategoryList.SelectedIndex=CategoryList.Items.Count>1?1:0;CategoryList.Focus();e.Handled=true;}
        else if(e.Key==Key.Enter)
        {
            int index=CategoryList.Items.Count>1?1:0;
            if(CategoryList.Items[index] is CatalogFilter choice) {filterChange=CommitCategory(choice);await filterChange;}e.Handled=true;
        }
    }
    async void CategoryListKey(object sender,KeyEventArgs e)
    {
        if(e.Key==Key.Escape) {CategoryPopup.IsOpen=false;e.Handled=true;}
        else if(e.Key==Key.Enter && CategoryList.SelectedItem is CatalogFilter choice) {filterChange=CommitCategory(choice);await filterChange;e.Handled=true;}
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
#if DIAGNOSTICS
    async Task<string> ReviewCatalogControls(string preview)
    {
        OpenFilter(true);await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ContextIdle);
        CategorySearch.Text="zz-category-does-not-exist";
        if(CategoryEmpty.Visibility!=Visibility.Visible || CategoryList.Items.Count!=1) throw new IOException("Category empty-state check failed");
        CategorySearch.Text="pAgE";await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ContextIdle);
        if(CategoryList.Items.Count<2) throw new IOException("Category search check failed");
        SaveControl(CategoryPopup.Child,preview+".heroes.png");
        var inputSource=PresentationSource.FromVisual(CategorySearch)!;
        CategorySearch.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,inputSource,0,Key.Down){RoutedEvent=Keyboard.PreviewKeyDownEvent});
        CategoryList.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,inputSource,0,Key.Enter){RoutedEvent=Keyboard.PreviewKeyDownEvent});
        if(filterChange!=null) await filterChange;
        await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ContextIdle);
        if(CategoryPopup.IsOpen || selectedHero!="Paige" || CatalogList.Items.Count==0 || !CatalogList.Items.Cast<CatalogItem>().All(x=>x.Hero.Split(',').Contains("Paige"))) throw new IOException("Hero keyboard selection check failed");
        OpenFilter(false);CategorySearch.Text="skin";await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ContextIdle);
        if(CategoryList.Items.Count!=2) throw new IOException("Mod type alias search failed");
        SaveControl(CategoryPopup.Child,preview+".types.png");
        await CommitCategory((CatalogFilter)CategoryList.Items[1]);
        if(selectedCategory!="type:appearance" || CatalogList.Items.Count==0 || !CatalogList.Items.Cast<CatalogItem>().All(x=>x.ModType=="appearance" && x.Hero.Split(',').Contains("Paige"))) throw new IOException("Combined hero/type filter failed");
        await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ContextIdle);SaveControl(FiltersPanel,preview+".filters.png");
        await ResetFilters();
        if(selectedHero!="" || selectedCategory!="" || total<100) throw new IOException("Reset filters failed");
        SearchBox.Text="page";var timer=System.Diagnostics.Stopwatch.StartNew();await SearchNow();timer.Stop();
        if(total==0 || !CatalogList.Items.Cast<CatalogItem>().All(x=>x.Hero.Split(',').Contains("Paige"))) throw new IOException("Page alias search failed");
        OpenSort(this,new RoutedEventArgs());await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ContextIdle);
        SaveControl(SortButton.ContextMenu,preview+".sort.png");SortButton.ContextMenu.IsOpen=false;
        if(lastDetail!=null) await lastDetail;
        return "PASS: hero search and empty state\nPASS: keyboard selects Paige from page\nPASS: combined hero and type filters\nPASS: reset restores full catalog\nPASS: page finds only Paige mods; total "+total+"\nIndexed query and UI refresh: "+timer.ElapsedMilliseconds+" ms\n";
    }
    static void SaveControl(Visual control,string path)
    {
        if(control is FrameworkElement element) element.UpdateLayout();
        var size=VisualTreeHelper.GetDescendantBounds(control);
        var bitmap=new RenderTargetBitmap(Math.Max(1,(int)Math.Ceiling(size.Right)),Math.Max(1,(int)Math.Ceiling(size.Bottom)),96,96,PixelFormats.Pbgra32);bitmap.Render(control);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var stream=File.Create(path);encoder.Save(stream);
    }
#endif
}
