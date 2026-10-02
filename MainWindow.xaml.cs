using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace PocketDeadlock;

public partial class MainWindow : Window
{
    readonly ModStorage storage;
    readonly GameInstall game;
    readonly HybridCatalogs sources;
    CancellationTokenSource? operation;
    bool busy,library;
    int total;
    string activeSearch="",activeKind="Mod",sourceUrl="";
    ModDetails? details;
    LibraryMod? selectedLocal;
    Task? lastDetail;
    CancellationTokenSource? detailRequest;
    int detailGeneration;
    public MainWindow(ModStorage storage,string? preview)
    {
        this.storage=storage; game=new(storage);sources=new(storage.Root,preview==null); InitializeComponent();
        InitializeFeatures(preview!=null);RefreshGame();RefreshAccount(); InitializeUpdateTimer(preview==null);
        Loaded+=async (_,_)=>
        {
            if(preview==null) _ = sources.Warm(CancellationToken.None);
            await LoadCatalog();
#if DIAGNOSTICS
            if(preview!=null)
            {
                if(App.PreviewMode=="--tiles")
                {
                    SearchBox.Text="page";await SearchNow();
                    if(App.PreviewTileErrors)
                    {
                        var sample=catalogRows.Select((x,i)=>i==0?x with {Image=""}:i==1?x with {Image="https://example.com/not-trusted.png"}:i==2?x with {Name=x.Name+" - a very long catalog title that wraps and remains available in the tooltip"}:x).ToList();
                        DisplayCatalog(sample);CatalogList.SelectedIndex=0;lastDetail=ShowDetail(sample[0]);
                    }
                }
                if(lastDetail!=null) await lastDetail;
                if(App.PreviewMode=="--library") {SetTab(true);if(LibraryList.Items.Count>0) LibraryList.SelectedIndex=0;}
                if(App.PreviewMode=="--profiles") ShowProfiles(this,new RoutedEventArgs());
                if(App.PreviewMode=="--downloads") ShowDownloads(this,new RoutedEventArgs());
                string review=App.PreviewMode=="--categories"?await ReviewCatalogControls(preview):App.PreviewMode=="--tiles"?await ReviewTileControls(preview):"";
                if(App.PreviewMode=="--account")
                {
                    var accountWindow=new GameBananaLoginWindow(App.Account!){Owner=this};accountWindow.Show();
                    review=await accountWindow.ReviewGuest(preview+".login.png");
                }
                await Dispatcher.InvokeAsync(()=>{},System.Windows.Threading.DispatcherPriority.ContextIdle);
                UpdateLayout();
                var image=new RenderTargetBitmap((int)ActualWidth,(int)ActualHeight,96,96,PixelFormats.Pbgra32); image.Render(this);
                var png=new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(image));
                using(var file=File.Create(preview)) png.Save(file);
                File.WriteAllText(preview+".txt",$"Catalog rows: {CatalogList.Items.Count}\nDetail: {DetailTitle.Text}\nStatus: {StatusLabel.Text}\n"+review);
                Close();
            }
            else
#endif
                await ScheduledUpdates();
        };
        Closing+=HandleClosing;
    }
    async Task Run(string message,Func<CancellationToken,Task> action)
    {
        if(busy) return;
        busy=true; Workspace.IsEnabled=false; Progress.Visibility=Visibility.Visible; CancelButton.Visibility=Visibility.Visible;
        StatusLabel.Text=message; operation=new CancellationTokenSource();
        try { await action(operation.Token); }
        catch(OperationCanceledException) { StatusLabel.Text=L.T("Операция отменена или истекло время ожидания. Можно повторить."); }
        catch(Exception ex) { StatusLabel.Text=ex is UnauthorizedAccessException?L.T("Windows запретила запись. Проверь доступ к папке игры: ")+ex.Message:ex.Message; }
        finally
        {
            operation.Dispose(); operation=null; busy=false; Workspace.IsEnabled=true; Progress.Visibility=Visibility.Collapsed; CancelButton.Visibility=Visibility.Collapsed;
            RefreshGame();
        }
    }
    void RefreshGame()
    {
        bool running=GameInstall.IsRunning();LaunchButton.IsEnabled=ApplyButton.IsEnabled=DisableButton.IsEnabled=!running;
        GamePathLabel.Text=storage.State.GamePath==""?L.T("Нажми «Папка игры» и выбери Deadlock."):storage.State.GamePath;
        try
        {
            GameStatus.Text=GameInstall.Valid(storage.State.GamePath)
                ? running?L.T("Игра запущена - изменения ожидают применения. Можно скачивать и выбирать моды."):L.T($"Deadlock найден · Наши моды {(game.IsApplied()?L.T("подключены"):L.T("отключены"))} · В библиотеке: {storage.State.Mods.Count} · {(storage.State.PendingApply?L.T("Есть непримененные изменения"):L.T("Набор сохранен"))}")
                :L.T("Нужна папка установленного Deadlock");
        }
        catch { GameStatus.Text=L.T("Не удалось прочитать состояние игры. Проверь папку."); }
    }
    void SetTab(bool useLibrary)
    {
        library=useLibrary;
        featureId="";DetailActions.Visibility=FormatHelp.Visibility=Visibility.Visible;
        favoritesView=false; FeatureScreen.Visibility=Visibility.Collapsed;FiltersPanel.Visibility=library?Visibility.Collapsed:Visibility.Visible;
        CatalogList.Visibility=SearchPanel.Visibility=FeedSummary.Visibility=library?Visibility.Collapsed:Visibility.Visible;
        LibraryList.Visibility=LibraryActions.Visibility=ApplyPanel.Visibility=library?Visibility.Visible:Visibility.Collapsed;
        SetNavigation(library?LibraryTab:CatalogTab);
        if(library && !busy && !checkingUpdates) StatusLabel.Text=L.T("Выбери моды и их порядок. Изменения применятся после выхода из игры.");
        if(library) RefreshLibrary(); else EmptyLabel.Visibility=CatalogList.Items.Count==0?Visibility.Visible:Visibility.Collapsed;
    }
    void SetNavigation(Button selected)
    {
        foreach(var button in new[]{CatalogTab,LibraryTab,FavoritesTab,DownloadsTab,ProfilesTab})
            button.Style=(Style)FindResource(button==selected?"Primary":typeof(Button));
    }
    void RefreshLibrary()
    {
        string? selected=(LibraryList.SelectedItem as LibraryMod)?.Id;
        var visible=storage.State.Mods.Where(x=>x.Name.Contains(LibrarySearch.Text,StringComparison.CurrentCultureIgnoreCase)).ToList();
        LibraryList.ItemsSource=null; LibraryList.ItemsSource=visible;
        if(selected!=null)
        {
            LibraryList.SelectedItem=visible.FirstOrDefault(x=>x.Id==selected);
            if(LibraryList.SelectedItem==null) ClearDetail();
        }
        EmptyLabel.Text=storage.State.Mods.Count==0?L.T("Здесь появятся скачанные моды.\nМожно также импортировать VPK или архив с компьютера."):L.T("В библиотеке ничего не найдено. Попробуй другое название или очисти поиск.");
        EmptyLabel.Visibility=visible.Count==0?Visibility.Visible:Visibility.Collapsed;
        RefreshGame();
    }
    async Task LoadCatalog()=>await Run(L.T("Загружаем каталог…"),async token=>
    {
        SetTab(false);
        var result=await sources.QueryUnified(activeKind,activeSearch,1,SelectedCategory,sortIndex,token,selectedHero,perPage:int.MaxValue,content:contentFilter);
        catalogShowsFavorites=false;DisplayCatalog(result.Items);total=result.Total;FeedSummary.Text=L.T("Найдено: ")+total;
        Descendants<ScrollViewer>(CatalogList).FirstOrDefault()?.ScrollToTop();
        EmptyLabel.Text=L.T("Ничего не найдено. Сбрось фильтры или попробуй другое название.");
        EmptyLabel.Visibility=result.Items.Count==0?Visibility.Visible:Visibility.Collapsed;
        ClearDetail();var searchPlan=CatalogTaxonomy.Search(activeSearch);
        StatusLabel.Text=searchPlan.Hero==""?L.T("Ищи по названию или герою. Тип мода можно выбрать в фильтре."):L.T("Поиск по герою: ")+CatalogTaxonomy.HeroLabel(searchPlan.Hero);
        if(result.Total==0 && sources.IsRefreshing) EmptyLabel.Text=L.T("Каталог загружается в фоне. Результаты появятся здесь.");
    });
    async Task ShowDetail(CatalogItem item)
    {
        detailRequest?.Cancel();detailRequest?.Dispose();detailRequest=new CancellationTokenSource();
        try {await LoadDetail(item.Kind,item.Id,detailRequest.Token,item);}catch(OperationCanceledException) { }catch(GameBananaLoginRequiredException ex)
        {DetailTitle.Text=item.Name;DetailText.Text=ex.Message;sourceUrl=item.Url;SourceButton.IsEnabled=Catalogs.SafePage(sourceUrl);StatusLabel.Text=ex.Message;}
        catch(Exception ex) {StatusLabel.Text=L.T(ex.Message);}
    }
    void ClearDetail(bool cancelRequest=true)
    {
        if(cancelRequest) detailRequest?.Cancel();
        DetailScroll.ScrollToTop();
        detailGeneration++;
        details=null; selectedLocal=null; sourceUrl=""; PreviewImage.Source=null;
        LibraryTools.Visibility=Visibility.Collapsed;
        HigherButton.IsEnabled=LowerButton.IsEnabled=RollbackButton.IsEnabled=false;
        FavoriteButton.Visibility=Visibility.Visible;
        DetailTitle.Text=L.T("Выбери мод"); DetailMeta.Text=""; DetailText.Text=L.T("Нажми на карточку, чтобы посмотреть описание и варианты скачивания."); RequirementsLabel.Text="";
        DownloadButton.IsEnabled=SourceButton.IsEnabled=FavoriteButton.IsEnabled=false; FilesBox.Visibility=FileLabel.Visibility=Visibility.Collapsed;
    }
    async Task LoadDetail(string kind,long id,CancellationToken token,CatalogItem? item=null)
    {
        ClearDetail(false);int generation=detailGeneration;
        var fetched=item==null?await new GameBanana().Details(kind,id,token):await sources.Get(item.Provider).Details(item,token);
        if(generation!=detailGeneration) return;
        details=item==null?fetched:fetched with {Item=fetched.Item with {Hero=item.Hero,ModType=item.ModType}};
        DetailTitle.Text=details.Item.Name; DetailMeta.Text=details.Item.Caption; DetailText.Text=details.Description;
        if(details.Item.ContentRatings!="") DetailMeta.Text+="\n"+L.T(details.Item.ContentRatings);
        RequirementsLabel.Text=details.Requirements.Length>0?L.T("Требования автора: ")+details.Requirements:"";
        sourceUrl=details.Item.Url; SourceButton.IsEnabled=Catalogs.SafePage(sourceUrl);
        DetailMeta.Text+=L.T($"\nЗагрузок: {details.Item.Downloads} · Оценок: {details.Item.Likes}");RefreshFavorite();
        FilesBox.ItemsSource=details.Files; FilesBox.SelectedIndex=details.Files.Count==1?0:-1;
        FilesBox.Visibility=FileLabel.Visibility=Visibility.Visible;
        DownloadButton.Content=L.T("Скачать в библиотеку");RefreshDownloadVariant();
        if(details.Files.Count==0) StatusLabel.Text=L.T("Для этого мода нет прямых загрузок. Открой страницу автора.");
        else if(details.Files.Count>1) StatusLabel.Text=L.T("У мода несколько файлов. Выбери нужный вариант загрузки.");
        if(details.Item.Image!="")
        {
            try
            {
                var image=await App.Thumbnails!.Get(details.Item.Image,token);if(generation==detailGeneration) PreviewImage.Source=image;
            }
            catch(OperationCanceledException) { throw; }
            catch { /* A preview failure must not disable the mod's files. */ }
        }
    }
    void DownloadVariantChanged(object sender,SelectionChangedEventArgs e)=>RefreshDownloadVariant();
    void RefreshDownloadVariant()
    {
        if(details==null) return;
        var file=FilesBox.SelectedItem as RemoteFile;
        DownloadButton.IsEnabled=file is {Blocked:false};
        FileLabel.Text=L.T(file==null && details.Files.Count>1?"Выбери вариант для скачивания":file?.Blocked==true?"Файл заблокирован источником":"Вариант загрузки");
    }
    async void SelectedMod(object sender,SelectionChangedEventArgs e)
    {
        if(CatalogList.SelectedItem is CatalogItem item && !busy && !settingFilters) {lastDetail=ShowDetail(item);await lastDetail;}
    }
    void SelectedLibraryMod(object sender,SelectionChangedEventArgs e)
    {
        if(LibraryList.SelectedItem is not LibraryMod mod) return;
        ClearDetail(); selectedLocal=mod; DetailTitle.Text=mod.Name;
        FavoriteButton.Visibility=Visibility.Collapsed;
        int index=storage.State.Mods.FindIndex(x=>x.Id==mod.Id);
        HigherButton.IsEnabled=index>0;LowerButton.IsEnabled=index>=0 && index<storage.State.Mods.Count-1;
        RollbackButton.IsEnabled=mod.Revisions.Count>0;
        LibraryTools.Visibility=Visibility.Visible; OverridesBox.IsChecked=mod.AllowOverrides; ModAutoBox.IsChecked=mod.AutoUpdate;
        DetailMeta.Text=L.T("Локальная библиотека · ")+mod.Added;
        DetailText.Text=L.T("VPK в наборе: ")+mod.Files.Count+L.T(".\nВариант: ")+(mod.VariantName==""?L.T("Определится при проверке обновлений"):mod.VariantName)+L.T("\nПредыдущих версий: ")+mod.Revisions.Count+"\n\n"+mod.UpdateStatus;
        sourceUrl=mod.SourceUrl; SourceButton.IsEnabled=Catalogs.SafePage(sourceUrl);
        DownloadButton.Content=L.T("Посмотреть варианты"); DownloadButton.IsEnabled=mod.RemoteId>0;
    }
    async void Search(object sender,RoutedEventArgs e)=>await SearchNow();
    async void SearchKey(object sender,KeyEventArgs e) { if(e.Key==Key.Enter) await SearchNow(); }
    async Task SearchNow()
    {
        var input=SearchBox.Text.Trim();
        if(Uri.TryCreate(input,UriKind.Absolute,out var uri) && GameBanana.TrustedUrl(input))
        {
            var match=Regex.Match(uri.AbsolutePath,@"^/(mods|sounds)/(\d+)/?$");
            if(match.Success)
            {
                await Run(L.T("Открываем ссылку…"),token=>LoadDetail(match.Groups[1].Value=="mods"?"Mod":"Sound",long.Parse(match.Groups[2].Value),token)); return;
            }
        }
        activeSearch=input; activeKind=KindBox.SelectedIndex==1?"Sound":"Mod"; await LoadCatalog();
    }
    async void KindChanged(object sender,SelectionChangedEventArgs e) { if(IsLoaded && !busy && !settingFilters) { activeKind=KindBox.SelectedIndex==1?"Sound":"Mod";selectedCategory=selectedHero="";await LoadCatalog(); } }
    async void ShowCatalog(object sender,RoutedEventArgs e)
    {
        if(catalogShowsFavorites) {await LoadCatalog();return;}
        SetTab(false);if(CatalogList.SelectedItem is CatalogItem item) {lastDetail=ShowDetail(item);await lastDetail;}
    }
    void ShowLibrary(object sender,RoutedEventArgs e) { SetTab(true); ClearDetail(); }
    void Cancel(object sender,RoutedEventArgs e)=>operation?.Cancel();
    void ChooseGame(object sender,RoutedEventArgs e)
    {
        if(busy) return;
        try
        {
            if(game.IsApplied()) { StatusLabel.Text=L.T("Сначала отключи наши моды в текущей игре, затем меняй папку."); return; }
            var dialog=new OpenFolderDialog { Title=L.T("Выбери папку Deadlock"),InitialDirectory=storage.State.GamePath };
            if(dialog.ShowDialog(this)==true)
            {
                if(!GameInstall.Valid(dialog.FolderName)) throw new IOException(L.T("Нужна корневая папка Deadlock, внутри которой есть game/citadel и game/bin/win64/deadlock.exe."));
                var old=storage.State.GamePath; storage.State.GamePath=dialog.FolderName;
                try { storage.Save(); } catch { storage.State.GamePath=old; throw; }
                RefreshGame(); StatusLabel.Text=L.T("Папка игры сохранена.");
            }
        }
        catch(Exception ex) { StatusLabel.Text=L.T(ex.Message); }
    }
    async void Launch(object sender,RoutedEventArgs e)
    {
        await Run(L.T("Готовим Deadlock к запуску…"),async token=>
        {
            GameInstall.EnsureClosed(); if(storage.State.ApplyOnLaunch && (storage.State.PendingApply || storage.State.Mods.Any(x=>x.Enabled))) await Task.Run(()=>game.Apply(token),token);
            Process.Start(new ProcessStartInfo("steam://run/1422450") { UseShellExecute=true }); StatusLabel.Text=L.T("Выбранный набор проверен. Команда запуска передана Steam.");
        });
    }
    void OpenSource(object sender,RoutedEventArgs e)
    {
        try { if(Catalogs.SafePage(sourceUrl)) Process.Start(new ProcessStartInfo(sourceUrl) { UseShellExecute=true }); }
        catch(Exception ex) { StatusLabel.Text=L.T(ex.Message); }
    }
    async void Download(object sender,RoutedEventArgs e)
    {
        if(details==null && selectedLocal is { RemoteId:>0 } local)
        {
            await Run(L.T("Проверяем версии…"),token=>LoadDetail(local.Kind,local.RemoteId,token)); return;
        }
        if(details==null || FilesBox.SelectedItem is not RemoteFile file) { StatusLabel.Text=L.T("Выбери файл в списке вариантов загрузки."); return; }
        await QueueDownload(details,file);
    }
    async void Import(object sender,RoutedEventArgs e)
    {
        var dialog=new OpenFileDialog { Title=L.T("Импорт мода"),Filter=L.T("Моды|*.vpk;*.zip;*.rar;*.7z") };
        if(dialog.ShowDialog(this)!=true) return;
        await Run(L.T("Читаем мод…"),token=>ImportPrepared(dialog.FileName,Path.GetFileNameWithoutExtension(dialog.FileName),null,null,token));
    }
    async Task ImportPrepared(string file,string name,ModDetails? remote,RemoteFile? variant,CancellationToken token)
    {
        StatusLabel.Text=L.T("Распаковываем VPK…");
        var prepared=await Task.Run(()=>storage.Prepare(file,token),token);
        try
        {
            List<string>? selected=prepared.Files.Count==1?prepared.Files:SelectVpks(prepared.Files);
            if(selected==null) { StatusLabel.Text=L.T("Импорт отменен."); return; }
            token.ThrowIfCancellationRequested(); StatusLabel.Text=L.T("Проверяем структуру VPK…");
            await Task.Run(()=>storage.Install(prepared,selected,name,remote,variant));
            SetTab(true); ClearDetail(); StatusLabel.Text=L.T("Мод добавлен в библиотеку. Отметь его и нажми «Применить выбранные».");
        }
        finally { storage.CleanupStaging(prepared.Folder); }
    }
    List<string>? SelectVpks(List<string> files)
    {
        var window=new Window { Owner=this,Title=L.T("Выбери VPK из архива"),Width=660,Height=520,MinWidth=450,WindowStartupLocation=WindowStartupLocation.CenterOwner };
        var dock=new DockPanel { Margin=new Thickness(20) }; window.Content=dock;
        var intro=new TextBlock { Text=L.T("В архиве несколько VPK. Это могут быть разные варианты одного скина. Выбери нужные; одинаковые ресурсы одновременно не устанавливаются."),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,16) };
        DockPanel.SetDock(intro,Dock.Top); dock.Children.Add(intro);
        var buttons=new StackPanel { Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,16,0,0) }; DockPanel.SetDock(buttons,Dock.Bottom); dock.Children.Add(buttons);
        var cancel=new Button { Content=L.T("Отмена"),IsCancel=true,Margin=new Thickness(0,0,8,0) }; buttons.Children.Add(cancel);
        var ok=new Button { Content=L.T("Импортировать выбранные"),Style=(Style)FindResource("Primary"),IsDefault=true }; buttons.Children.Add(ok);
        var rows=new StackPanel(); var checks=new List<CheckBox>();
        foreach(var f in files) { var cb=new CheckBox { Content=new TextBlock { Text=f,TextWrapping=TextWrapping.Wrap },Tag=f,Margin=new Thickness(0,4,0,8) }; checks.Add(cb); rows.Children.Add(cb); }
        dock.Children.Add(new ScrollViewer { Content=rows,VerticalScrollBarVisibility=ScrollBarVisibility.Auto });
        ok.Click+=(_,_)=> { if(checks.Any(x=>x.IsChecked==true)) window.DialogResult=true; };
        return window.ShowDialog()==true?checks.Where(x=>x.IsChecked==true).Select(x=>(string)x.Tag).ToList():null;
    }
    void ToggleMod(object sender,RoutedEventArgs e)
    {
        try { storage.State.PendingApply=true; storage.Save(); StatusLabel.Text=L.T("Набор изменен. Он применится перед запуском игры или кнопкой «Применить выбранные»."); }
        catch(Exception ex) { if((sender as CheckBox)?.DataContext is LibraryMod mod) mod.Enabled=!mod.Enabled; RefreshLibrary(); StatusLabel.Text=L.T(ex.Message); }
    }
    async void Apply(object sender,RoutedEventArgs e)=>await Run(L.T("Проверяем конфликты и подключаем выбранные моды…"),async token=>
    {
        await Task.Run(()=>game.Apply(token),token); StatusLabel.Text=L.T("Выбранный набор применен. Теперь можно запускать Deadlock.");
    });
    async void Disable(object sender,RoutedEventArgs e)=>await Run(L.T("Отключаем наши моды…"),async token=>
    {
        await Task.Run(()=>game.Disable(),token); StatusLabel.Text=L.T("Подключение PocketDeadlock удалено. Библиотека и старые моды сохранены.");
    });
    async void CheckUpdates(object sender,RoutedEventArgs e)=>await RunUpdates(false,false);
}
