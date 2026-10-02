using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;

namespace PocketDeadlock;

public sealed class TransferItem : INotifyPropertyChanged
{
    public string Name {get;init;}="";
    string status=L.T("В очереди");
    public string Status {get=>status;set {status=value;PropertyChanged?.Invoke(this,new(nameof(Status)));}}
    public CancellationTokenSource Cancellation {get;}=new();
    public bool Finished {get;set;}
    public event PropertyChangedEventHandler? PropertyChanged;
}
public partial class MainWindow
{
    readonly ObservableCollection<TransferItem> transfers=[];
    readonly SemaphoreSlim downloadSlots=new(2);
    List<CatalogItem> catalogRows=[];
    DispatcherTimer? presenceTimer;
    bool settingFilters,favoritesView;
    string? draggedId;
    Point dragStart;
    int ActiveTransfers=>transfers.Count(x=>!x.Finished);
    string SelectedCategory=>selectedCategory;
    DispatcherTimer? searchDelay;
    string featureId="";
    void InitializeFeatures(bool preview)
    {
        UpdateCategoryOptions();
        LibraryList.PreviewMouseLeftButtonDown+=(_,e)=> {dragStart=e.GetPosition(LibraryList);draggedId=(ItemsControl.ContainerFromElement(LibraryList,e.OriginalSource as DependencyObject) as ListBoxItem)?.DataContext is LibraryMod mod?mod.Id:null;};
        LibraryList.PreviewMouseMove+=(_,e)=>
        {
            if(draggedId==null || e.LeftButton!=MouseButtonState.Pressed || busy) return;
            Point point=e.GetPosition(LibraryList);
            if(Math.Abs(point.X-dragStart.X)<SystemParameters.MinimumHorizontalDragDistance && Math.Abs(point.Y-dragStart.Y)<SystemParameters.MinimumVerticalDragDistance) return;
            string id=draggedId;draggedId=null;DragDrop.DoDragDrop(LibraryList,new DataObject("PocketDeadlockMod",id),DragDropEffects.Move);
        };
        sources.Status+=status=>Dispatcher.InvokeAsync(()=>IndexStatus.Text=L.T(status));
        sources.Changed+=_=>Dispatcher.InvokeAsync(async ()=>await RefreshIndexedResults());
        if(preview) return;
        searchDelay=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(350)};
        searchDelay.Tick+=async (_,_)=> {searchDelay.Stop();if(IsLoaded && !busy && !library && FeatureScreen.Visibility!=Visibility.Visible) await SearchNow();};
        SearchBox.TextChanged+=(_,_)=> {if(IsLoaded && !library) {searchDelay.Stop();searchDelay.Start();}};
        presenceTimer=new DispatcherTimer {Interval=TimeSpan.FromSeconds(3)};presenceTimer.Tick+=(_,_)=>RefreshGame();presenceTimer.Start();
    }
    void DisplayCatalog(List<CatalogItem> rows,bool preservePosition=false)
    {
        var panel=Descendants<CatalogTilePanel>(CatalogList).FirstOrDefault();
        var selected=CatalogList.SelectedItem as CatalogItem;
        int first=panel==null?0:(int)(panel.VerticalOffset/panel.RowStride)*panel.Columns;
        string? anchor=preservePosition && first<catalogRows.Count?UnifiedCatalog.Identity(catalogRows[first]):null;
        double within=panel==null?0:panel.VerticalOffset%panel.RowStride;
        catalogRows=rows;settingFilters=true;
        try
        {
            UpdateCategoryOptions();CatalogList.ItemsSource=rows;
            CatalogList.SelectedItem=preservePosition && selected!=null?rows.FirstOrDefault(x=>UnifiedCatalog.Identity(x)==UnifiedCatalog.Identity(selected)):null;
            CatalogList.UpdateLayout();
            panel=Descendants<CatalogTilePanel>(CatalogList).FirstOrDefault();
            int index=anchor==null?-1:rows.FindIndex(x=>UnifiedCatalog.Identity(x)==anchor);
            panel?.SetVerticalOffset(index>=0?index/panel.Columns*panel.RowStride+within:0);
            FeedSummary.Text=L.T("Найдено: ")+CatalogList.Items.Count;
        }
        finally {settingFilters=false;}
    }
    async Task ApplyCatalogFilter()
    {
        if(!favoritesView) {if(IsLoaded && !busy) await LoadCatalog();return;}
        var result=UnifiedCatalog.Query(UnifiedCatalog.Merge(catalogRows).Where(x=>CatalogContent.Matches(x.Item,contentFilter,App.Account?.SignedIn==true)),"","",1,SelectedCategory,sortIndex,perPage:int.MaxValue,hero:selectedHero);
        CatalogList.ItemsSource=result.Items;Descendants<ScrollViewer>(CatalogList).FirstOrDefault()?.ScrollToTop();FeedSummary.Text=L.T("Найдено: ")+result.Total;EmptyLabel.Visibility=result.Total==0?Visibility.Visible:Visibility.Collapsed;
    }
    void FilterLibrary(object sender,TextChangedEventArgs e) {if(library) RefreshLibrary();}
    void FeaturePage(string title,string description)
    {
        ClearDetail();FeatureContent.Children.Clear();FeatureScreen.Visibility=Visibility.Visible;
        CatalogList.Visibility=LibraryList.Visibility=SearchPanel.Visibility=LibraryActions.Visibility=FiltersPanel.Visibility=FeedSummary.Visibility=ApplyPanel.Visibility=EmptyLabel.Visibility=Visibility.Collapsed;
        DetailTitle.Text=title;DetailText.Text=description;
        DetailActions.Visibility=Visibility.Collapsed;FormatHelp.Visibility=Visibility.Collapsed;
        library=false;favoritesView=false;
        SetNavigation(featureId=="profiles"?ProfilesTab:DownloadsTab);
        if(!busy && !checkingUpdates) StatusLabel.Text=description;
    }
    static bool SameFavorite(CatalogItem a,CatalogItem b)=>a.Kind==b.Kind && (a.Id>0 && a.Id==b.Id || a.Key!="" && a.Key==b.Key && a.Provider==b.Provider);
    void RefreshFavorite()
    {
        FavoriteButton.IsEnabled=details!=null; FavoriteButton.Content=details!=null && storage.State.Favorites.Any(x=>SameFavorite(x,details.Item))?L.T("Убрать из избранного"):L.T("В избранное");
    }
    void ToggleFavorite(object sender,RoutedEventArgs e)
    {
        if(details==null) return;
        var old=storage.State.Favorites.ToList();var match=old.FirstOrDefault(x=>SameFavorite(x,details.Item));
        if(match==null) storage.State.Favorites.Add(details.Item);else storage.State.Favorites.Remove(match);
        try {storage.Save();RefreshFavorite();if(favoritesView) DisplayCatalog(storage.State.Favorites);}catch(Exception ex) {storage.State.Favorites=old;StatusLabel.Text=L.T(ex.Message);}
    }
    void ShowFavorites(object sender,RoutedEventArgs e)
    {
        SetTab(false);favoritesView=true;SetNavigation(FavoritesTab);ClearDetail();SearchPanel.Visibility=Visibility.Collapsed;
        DisplayCatalog(storage.State.Favorites);EmptyLabel.Text=L.T("Добавляй моды в избранное на странице выбранного мода.");StatusLabel.Text=L.T("Избранное сохранено на этом компьютере.");
    }
    void ShowDownloads(object sender,RoutedEventArgs e)
    {
        featureId="downloads";
        FeaturePage(L.T("Загрузки и история"),L.T("Два файла могут скачиваться одновременно. Готовые моды попадут в библиотеку; подключение к игре остается отдельным действием."));
        var cancel=FeatureButton(L.T("Отменить загрузки"),()=> {foreach(var row in transfers.Where(x=>!x.Finished)) row.Cancellation.Cancel();});cancel.IsEnabled=ActiveTransfers>0;FeatureContent.Children.Add(cancel);
        foreach(var row in transfers)
        {
            var stack=new StackPanel {Margin=new Thickness(0,16,0,0)};
            stack.Children.Add(new TextBlock {Text=row.Name,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap});
            var status=new TextBlock {TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,4,0,8)};status.SetBinding(TextBlock.TextProperty,new System.Windows.Data.Binding(nameof(TransferItem.Status)){Source=row});stack.Children.Add(status);
            if(!row.Finished) stack.Children.Add(FeatureButton(L.T("Отменить"),()=>row.Cancellation.Cancel()));FeatureContent.Children.Add(stack);
        }
        FeatureContent.Children.Add(new TextBlock {Text=L.T("История операций"),FontSize=20,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,24,0,8)});
        foreach(var row in storage.State.Activity.AsEnumerable().Reverse().Take(100)) FeatureContent.Children.Add(new TextBlock {Text=row.Time+" · "+row.Name+"\n"+row.Status,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,14)});
    }
    Button FeatureButton(string text,Action click)
    {
        var button=new Button {Content=L.T(text),HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,0,8,8)};button.Click+=(_,_)=>click();return button;
    }
    void Activity(string name,string status)
    {
        storage.State.Activity.Add(new(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm"),name,status));
        storage.State.Activity=storage.State.Activity.TakeLast(300).ToList();storage.Save();
    }
    async Task QueueDownload(ModDetails info,RemoteFile file,ProfileEntry? profileEntry=null)
    {
        if(file.Blocked) {StatusLabel.Text=L.T("Источник пометил файл как опасный. Выбери другой файл.");return;}
        if(transfers.Any(x=>!x.Finished && x.Name==info.Item.Name+" · "+file.Name)) {StatusLabel.Text=L.T("Этот файл уже находится в очереди.");return;}
        var row=new TransferItem {Name=info.Item.Name+" · "+file.Name};transfers.Add(row);StatusLabel.Text=L.T("Файл добавлен в очередь. Можно выбирать другие моды.");
        string? temp=null;PreparedMod? prepared=null; bool entered=false;
        try
        {
            await downloadSlots.WaitAsync(row.Cancellation.Token);entered=true;temp=storage.NewStaging();
            string ext=Path.GetExtension(file.Name).ToLowerInvariant();if(!new[]{".zip",".rar",".7z",".vpk"}.Contains(ext)) throw new IOException(L.T("Поддерживаются VPK, ZIP, RAR и 7Z."));
            string archive=Path.Combine(temp,"download"+ext);
            await new GameBanana().Download(file,archive,new Progress<string>(s=>row.Status=L.T(s)),row.Cancellation.Token);
            row.Status=L.T("Проверяем VPK…");prepared=await Task.Run(()=>storage.Prepare(archive,row.Cancellation.Token));
            List<string>? chosen=profileEntry?.SelectedPaths is {Count:>0} paths && paths.All(prepared.Files.Contains)?paths:prepared.Files.Count==1?prepared.Files:SelectVpks(prepared.Files);
            if(chosen==null) throw new OperationCanceledException();row.Cancellation.Token.ThrowIfCancellationRequested();
            var installed=storage.Install(prepared,chosen,info.Item.Name,info,file);
            if(profileEntry!=null) {installed.Enabled=profileEntry.Enabled;installed.AllowOverrides=profileEntry.AllowOverrides;storage.State.PendingApply=true;storage.Save();}
            row.Status=L.T("В библиотеке");
            Activity(info.Item.Name,L.T("Скачано: ")+file.Name);
            if(library) {RefreshLibrary();LibraryList.SelectedItem=installed;}StatusLabel.Text=L.T("Мод скачан в библиотеку. Отметь его для применения после выхода из игры.");
        }
        catch(OperationCanceledException) {row.Status=L.T("Отменено");}
        catch(Exception ex) {row.Status=L.T("Ошибка: "+ex.Message);StatusLabel.Text=row.Status;}
        finally
        {
            row.Finished=true;if(prepared!=null) storage.CleanupStaging(prepared.Folder);if(temp!=null) storage.CleanupStaging(temp);if(entered) downloadSlots.Release();
        }
    }
    void ShowProfiles(object sender,RoutedEventArgs e)
    {
        featureId="profiles";
        FeaturePage(L.T("Профили наборов"),L.T("Сохраняй отдельные наборы для разных героев. Переключение меняет только библиотеку. Во время игры новый набор ожидает применения."));
        var name=new TextBox {MaxLength=100,Margin=new Thickness(0,0,0,12),ToolTip=L.T("Название нового профиля"),Tag=L.T("Название нового профиля")};FeatureContent.Children.Add(name);
        FeatureContent.Children.Add(FeatureButton(L.T("Сохранить текущий набор"),()=> {try {new Profiles(storage).Capture(name.Text);ShowProfiles(sender,e);}catch(Exception ex) {StatusLabel.Text=L.T(ex.Message);}}));
        FeatureContent.Children.Add(FeatureButton(L.T("Скопировать ключ текущего набора"),()=> {try {Clipboard.SetText(Profiles.Key(new Profiles(storage).Current(L.T("Текущий набор"))));StatusLabel.Text=L.T("Ключ набора скопирован. В нем сохранены варианты и порядок модов.");}catch(Exception ex) {StatusLabel.Text=L.T(ex.Message);}}));
        FeatureContent.Children.Add(FeatureButton(L.T("Импортировать профиль"),()=>
        {
            var dialog=new OpenFileDialog {Filter="PocketDeadlock profile|*.json"};if(dialog.ShowDialog(this)!=true) return;
            try {new Profiles(storage).Import(dialog.FileName);ShowProfiles(sender,e);}catch(Exception ex) {StatusLabel.Text=L.T(ex.Message);}
        }));
        FeatureContent.Children.Add(new TextBlock {Text=L.T("Установить набор по ключу"),FontSize=20,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,20,0,8)});
        var key=new TextBox {TextWrapping=TextWrapping.Wrap,AcceptsReturn=true,MaxHeight=100,MinHeight=60,MaxLength=200000,Margin=new Thickness(0,0,0,8),ToolTip=L.T("Вставь ключ PD1-…"),Tag=L.T("Вставь ключ PD1-…")};System.Windows.Automation.AutomationProperties.SetName(key,L.T("Вставь ключ PD1-…"));FeatureContent.Children.Add(key);
        FeatureContent.Children.Add(FeatureButton(L.T("Загрузить набор по ключу"),()=>
        {
            try {var profile=new Profiles(storage).FromKey(key.Text);_ = RestoreProfile(profile);}catch(Exception ex) {StatusLabel.Text=L.T(ex.Message);}
        }));
        foreach(var profile in storage.State.Profiles)
        {
            FeatureContent.Children.Add(new TextBlock {Text=profile.Name,FontSize=18,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,20,0,8)});
            FeatureContent.Children.Add(new TextBlock {Text=L.T("Модов в профиле: ")+profile.Mods.Count,Margin=new Thickness(0,0,0,8)});
            var actions=new WrapPanel();FeatureContent.Children.Add(actions);
            actions.Children.Add(FeatureButton(L.T("Выбрать набор"),()=> {try {int missing=new Profiles(storage).Select(profile);SetTab(true);StatusLabel.Text=L.T($"Набор выбран. Не хватает модов: {missing}. Применение доступно после выхода из игры.");}catch(Exception ex) {StatusLabel.Text=L.T(ex.Message);}}));
            actions.Children.Add(FeatureButton(L.T("Экспортировать"),()=> {var dialog=new SaveFileDialog {FileName="PocketDeadlock-profile.json",Filter="PocketDeadlock profile|*.json"};if(dialog.ShowDialog(this)==true) try {Profiles.Export(profile,dialog.FileName);StatusLabel.Text=L.T("Профиль экспортирован. Файлы модов не включаются.");}catch(Exception ex){StatusLabel.Text=L.T(ex.Message);}}));
            actions.Children.Add(FeatureButton(L.T("Скачать отсутствующие"),()=>_ = RestoreProfile(profile)));
            actions.Children.Add(FeatureButton(L.T("Скопировать ключ набора"),()=> {try {Clipboard.SetText(Profiles.Key(profile));StatusLabel.Text=L.T("Ключ набора скопирован. В нем сохранены варианты и порядок модов.");}catch(Exception ex){StatusLabel.Text=L.T(ex.Message);}}));
            actions.Children.Add(FeatureButton(L.T("Удалить профиль"),()=> {storage.State.Profiles.Remove(profile);try {storage.Save();ShowProfiles(sender,e);}catch(Exception ex){storage.State.Profiles.Add(profile);StatusLabel.Text=L.T(ex.Message);}}));
        }
    }
    async Task RestoreProfile(ModProfile profile)
    {
        await Run(L.T("Проверяем файлы профиля…"),async token=>
        {
            var queued=new List<Task>();
            foreach(var entry in profile.Mods.Where(x=>x.RemoteId>0))
            {
                if(storage.State.Mods.Any(x=>x.Kind==entry.Kind && x.RemoteId==entry.RemoteId && (x.FileId==entry.FileId || entry.VariantKey!="" && x.VariantKey==entry.VariantKey))) continue;
                var info=await new GameBanana().Details(entry.Kind,entry.RemoteId,token);
                var file=ModUpdates.MatchVariant(new LibraryMod {FileId=entry.FileId,VariantKey=entry.VariantKey},info.Files);
                if(file==null) {StatusLabel.Text=L.T("Вариант из профиля недоступен. Открой страницу мода и выбери файл вручную.");continue;}
                queued.Add(QueueDownload(info,file,entry));
            }
            await Task.WhenAll(queued);
            int missing=new Profiles(storage).Select(profile);RefreshGame();
            StatusLabel.Text=missing==0?L.T("Набор загружен и выбран в библиотеке. Нажми «Применить выбранные», когда игра закрыта."):L.T("Часть файлов набора недоступна. Проверь загрузки и страницы авторов.");
        });
    }
    async void ScanExisting(object sender,RoutedEventArgs e)
    {
        string folder=Path.Combine(storage.State.GamePath,"game","citadel","addons");
        if(!Directory.Exists(folder)) {StatusLabel.Text=L.T("В выбранной игре нет папки старых addons.");return;}
        var files=Directory.GetFiles(folder,"*.vpk").ToList();var selected=SelectVpks(files);if(selected==null) return;
        await Run(L.T("Читаем существующие VPK…"),async token=>
        {
            foreach(var file in selected) await ImportPrepared(file,Path.GetFileNameWithoutExtension(file),null,null,token);
            StatusLabel.Text=L.T("Выбранные VPK скопированы в библиотеку. Файлы в игре сохранены.");
        });
    }
    async void DropFiles(object sender,DragEventArgs e)
    {
        if(busy) return;
        if(e.Data.GetDataPresent("PocketDeadlockMod"))
        {
            string id=(string)e.Data.GetData("PocketDeadlockMod");
            if((ItemsControl.ContainerFromElement(LibraryList,e.OriginalSource as DependencyObject) as ListBoxItem)?.DataContext is not LibraryMod target) return;
            var mod=storage.State.Mods.FirstOrDefault(x=>x.Id==id);if(mod==null || mod==target) return;
            var old=storage.State.Mods.ToList();int position=storage.State.Mods.IndexOf(target);storage.State.Mods.Remove(mod);storage.State.Mods.Insert(Math.Min(position,storage.State.Mods.Count),mod);
            try {storage.State.PendingApply=true;storage.Save();RefreshLibrary();}catch(Exception ex) {storage.State.Mods=old;StatusLabel.Text=L.T(ex.Message);}e.Handled=true;return;
        }
        if(e.Data.GetData(DataFormats.FileDrop) is not string[] files) return;
        await Run(L.T("Импортируем файлы…"),async token=> {foreach(var file in files.Where(x=>new[]{".vpk",".zip",".rar",".7z"}.Contains(Path.GetExtension(x).ToLowerInvariant()))) await ImportPrepared(file,Path.GetFileNameWithoutExtension(file),null,null,token);});
    }
    void Shortcut(object sender,KeyEventArgs e)
    {
        if(e.Key==Key.F && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) {if(library) LibrarySearch.Focus();else SearchBox.Focus();e.Handled=true;}
        if(e.Key==Key.Escape && busy) operation?.Cancel();
    }
    async Task RefreshIndexedResults()
    {
        if(!IsLoaded || busy || library || favoritesView || FeatureScreen.Visibility==Visibility.Visible) return;
        var selected=CatalogList.SelectedItem as CatalogItem;
        var result=await sources.QueryUnified(activeKind,activeSearch,1,SelectedCategory,sortIndex,CancellationToken.None,selectedHero,perPage:int.MaxValue,content:contentFilter);
        total=result.Total;DisplayCatalog(result.Items,true);
        var retained=selected==null?null:result.Items.FirstOrDefault(x=>UnifiedCatalog.Identity(x)==UnifiedCatalog.Identity(selected));
        if(retained==null) ClearDetail();
        EmptyLabel.Visibility=result.Items.Count==0?Visibility.Visible:Visibility.Collapsed;
        if(result.Total==0) EmptyLabel.Text=sources.IsRefreshing?L.T("Каталог загружается в фоне. Результаты появятся здесь."):L.T("Ничего не найдено. Сбрось фильтры или попробуй другое название.");
    }
    void ReloadLocale()
    {
        DisplayCatalog(catalogRows,true);RefreshGame();RefreshAccount();if(library) RefreshLibrary();
        if(FeatureScreen.Visibility==Visibility.Visible) {if(featureId=="profiles") ShowProfiles(this,new RoutedEventArgs());else ShowDownloads(this,new RoutedEventArgs());}
        else if(library && LibraryList.SelectedItem!=null) SelectedLibraryMod(this,null!);
    }
}
