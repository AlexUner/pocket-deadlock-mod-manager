using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace PocketDeadlock;

public partial class MainWindow
{
    DispatcherTimer? updateTimer;
    string? stagedApp;
    AppRelease? appRelease;
    bool restartForUpdate;
    bool checkingUpdates;
    CancellationTokenSource? backgroundUpdate;
    void InitializeUpdateTimer(bool enabled)
    {
        if(!enabled) return;
        updateTimer=new DispatcherTimer { Interval=TimeSpan.FromMinutes(30) };
        updateTimer.Tick+=async (_,_)=> {_ = sources.Warm(CancellationToken.None);await ScheduledUpdates();}; updateTimer.Start();
    }
    async Task ScheduledUpdates()
    {
        if(busy || checkingUpdates || ActiveTransfers>0 || !storage.State.CheckUpdatesAutomatically && !storage.State.CheckAppUpdatesAutomatically) return;
        await RunUpdates(true);
    }
    async Task RunUpdates(bool scheduled)
    {
    if(checkingUpdates) return;
    if(ActiveTransfers>0) {StatusLabel.Text=L.T("Проверка обновлений будет доступна после завершения загрузок.");return;}
    async Task Check(CancellationToken token)
    {
        string message="";
        if(!scheduled || storage.State.CheckUpdatesAutomatically)
        {
            var summary=await new ModUpdates(storage).Check(storage.State.DownloadModUpdatesAutomatically,new Progress<string>(s=>StatusLabel.Text=L.T(s)),token);
            message=L.T($"Моды: обновлено {summary.Updated}, доступно {summary.Available}, нужен выбор {summary.Manual}, ошибок {summary.Errors}. ");
            if(library) RefreshLibrary();
        }
        if((!scheduled || storage.State.CheckAppUpdatesAutomatically) && storage.State.UpdateFeed!="" && stagedApp==null)
        {
            try
            {
                var service=new AppUpdates(storage.Root);
                var release=await service.Check(storage.State.UpdateFeed,token);
                if(release==null) storage.State.LastAppUpdateStatus=L.T("Установлена последняя версия ")+AppUpdates.CurrentVersion.ToString(3);
                else
                {
                    stagedApp=await service.Stage(release,new Progress<string>(s=>StatusLabel.Text=L.T(s)),token); appRelease=release;
                    AppUpdateButton.Visibility=Visibility.Visible;
                    storage.State.LastAppUpdateStatus=L.T("Версия ")+release.Version+L.T(" готова. Установится при закрытии менеджера.");
                }
            }
            catch(OperationCanceledException) { throw; }
            catch(Exception ex) { storage.State.LastAppUpdateStatus=L.T("Обновление менеджера: ")+ex.Message; }
            storage.Save(); message+=storage.State.LastAppUpdateStatus;
        }
        else if(storage.State.UpdateFeed=="") message+=L.T("Канал выпусков менеджера еще не задан.");
        StatusLabel.Text=message;
    }
    if(!scheduled) {await Run(L.T("Проверяем обновления модов и менеджера…"),Check);return;}
    checkingUpdates=true;backgroundUpdate=new CancellationTokenSource();
    LibraryTools.IsEnabled=LibraryActions.IsEnabled=false;
    try {await Check(backgroundUpdate.Token);}
    catch(OperationCanceledException) { }
    catch(Exception ex) {StatusLabel.Text=L.T(ex.Message);}
    finally
    {
        checkingUpdates=false;backgroundUpdate.Dispose();backgroundUpdate=null;
        LibraryTools.IsEnabled=LibraryActions.IsEnabled=true;RefreshGame();
    }
    }
    void HandleClosing(object? sender,CancelEventArgs e)
    {
        if(ActiveTransfers>0) {foreach(var item in transfers.Where(x=>!x.Finished)) item.Cancellation.Cancel();e.Cancel=true;StatusLabel.Text=L.T("Завершаем загрузки. Затем окно можно закрыть.");return;}
        if(busy) { operation?.Cancel(); e.Cancel=true; StatusLabel.Text=L.T("Завершаем текущую операцию. Затем окно можно закрыть."); return; }
        if(checkingUpdates) {backgroundUpdate?.Cancel();e.Cancel=true;StatusLabel.Text=L.T("Завершаем текущую операцию. Затем окно можно закрыть.");return;}
        if(stagedApp!=null && appRelease!=null)
        {
            try
            {
                string job=AppUpdates.CreateJob(stagedApp,AppContext.BaseDirectory,Environment.ProcessId,appRelease,restartForUpdate);
                AppUpdates.StartInstall(job);
            }
            catch(Exception ex) { StatusLabel.Text=L.T(ex.Message); e.Cancel=true; return; }
        }
        updateTimer?.Stop();presenceTimer?.Stop();searchDelay?.Stop();
    }
    void InstallAppUpdate(object sender,RoutedEventArgs e) { if(busy) return; restartForUpdate=true; Close(); }
    void LibraryOptionChanged(object sender,RoutedEventArgs e)
    {
        if(selectedLocal==null) return;
        try
        {
            selectedLocal.AllowOverrides=OverridesBox.IsChecked==true; selectedLocal.AutoUpdate=ModAutoBox.IsChecked==true;
            storage.State.PendingApply=true; storage.Save(); StatusLabel.Text=L.T("Настройки мода сохранены. Изменения приоритета применятся перед запуском.");
        }
        catch(Exception ex) { StatusLabel.Text=L.T(ex.Message); }
    }
    void MoveHigher(object sender,RoutedEventArgs e)=>Move(-1);
    void MoveLower(object sender,RoutedEventArgs e)=>Move(1);
    void Move(int offset)
    {
        if(selectedLocal==null) return;
        int index=storage.State.Mods.FindIndex(x=>x.Id==selectedLocal.Id),next=index+offset;
        if(index<0 || next<0 || next>=storage.State.Mods.Count) return;
        (storage.State.Mods[index],storage.State.Mods[next])=(storage.State.Mods[next],storage.State.Mods[index]);
        try { storage.State.PendingApply=true; storage.Save(); RefreshLibrary(); StatusLabel.Text=L.T("Порядок изменен. Верхний мод имеет больший приоритет."); }
        catch(Exception ex) { (storage.State.Mods[index],storage.State.Mods[next])=(storage.State.Mods[next],storage.State.Mods[index]); StatusLabel.Text=L.T(ex.Message); }
    }
    async void UpdateSelected(object sender,RoutedEventArgs e)
    {
        if(selectedLocal is not {RemoteId:>0} mod) { StatusLabel.Text=L.T("Локальный мод обновляется импортом новой версии."); return; }
        await Run(L.T("Обновляем выбранный вариант…"),async token=>
        {
            var origin=new GameBanana(); var info=await origin.Details(mod.Kind,mod.RemoteId,token);
            var file=ModUpdates.MatchVariant(mod,info.Files);
            if(file==null) { await LoadDetail(mod.Kind,mod.RemoteId,token); StatusLabel.Text=L.T("Не удалось однозначно определить вариант. Выбери нужный файл."); return; }
            bool updated=await new ModUpdates(storage).Install(mod,info,file,new Progress<string>(s=>StatusLabel.Text=L.T(s)),token);
            if(updated) { RefreshLibrary(); StatusLabel.Text=L.T("Мод обновлен. Предыдущая версия сохранена; новый набор применится перед запуском."); }
            else { await LoadDetail(mod.Kind,mod.RemoteId,token); StatusLabel.Text=L.T("Внутри обновления изменились варианты VPK. Нужен ручной выбор."); }
        });
    }
    void RollbackSelected(object sender,RoutedEventArgs e)
    {
        if(selectedLocal==null) return;
        try { storage.Rollback(selectedLocal); RefreshLibrary(); StatusLabel.Text=L.T("Предыдущая версия восстановлена. Автообновление этого мода выключено, чтобы сохранить откат."); }
        catch(Exception ex) { StatusLabel.Text=L.T(ex.Message); }
    }
    void RemoveSelected(object sender,RoutedEventArgs e)
    {
        if(selectedLocal==null) return;
        var mod=selectedLocal; int index=storage.State.Mods.FindIndex(x=>x.Id==mod.Id);
        storage.State.Mods.RemoveAt(index);
        try { storage.State.PendingApply=true; storage.Save(); RefreshLibrary(); ClearDetail(); StatusLabel.Text=L.T("Мод убран из библиотеки. Он отключится при применении набора; сохраненные файлы остаются для восстановления."); }
        catch(Exception ex) { storage.State.Mods.Insert(index,mod); StatusLabel.Text=L.T(ex.Message); }
    }
    void UpdateSettings(object sender,RoutedEventArgs e)
    {
        if(busy) return;
        var window=new Window {Owner=this,Title=L.T("Обновления PocketDeadlock ")+AppUpdates.CurrentVersion.ToString(3),Width=740,Height=760,WindowStartupLocation=WindowStartupLocation.CenterOwner};
        var stack=new StackPanel {Margin=new Thickness(24)}; window.Content=new ScrollViewer {Content=stack,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        stack.Children.Add(new TextBlock {Text=L.T("Обновления и запуск"),FontSize=24,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,16)});
        stack.Children.Add(new TextBlock {Text=L.T("Язык интерфейса"),Margin=new Thickness(0,0,0,8)});
        var language=new ComboBox {ItemsSource=new[]{L.T("Как в Windows"),L.T("Русский"),"English"},SelectedIndex=storage.State.Language=="ru"?1:storage.State.Language=="en"?2:0,Margin=new Thickness(0,0,0,16)};stack.Children.Add(language);
        stack.Children.Add(new TextBlock {Text=L.T("Тема оформления"),Margin=new Thickness(0,0,0,8)});
        var theme=new ComboBox {ItemsSource=new[]{L.T("Как в Windows"),L.T("Темная"),L.T("Светлая")},SelectedIndex=storage.State.Theme=="dark"?1:storage.State.Theme=="light"?2:0,Margin=new Thickness(0,0,0,16)};stack.Children.Add(theme);
        CheckBox Check(string text,bool value) { var box=new CheckBox {Content=text,IsChecked=value,Margin=new Thickness(0,0,0,12)}; stack.Children.Add(box); return box; }
        var checks=Check(L.T("Проверять моды при запуске и каждые 30 минут"),storage.State.CheckUpdatesAutomatically);
        var downloads=Check(L.T("Автоматически скачивать обновления выбранного варианта"),storage.State.DownloadModUpdatesAutomatically);
        var apply=Check(L.T("Применять выбранный набор перед запуском Deadlock"),storage.State.ApplyOnLaunch);
        var app=Check(L.T("Проверять и готовить обновления самого менеджера"),storage.State.CheckAppUpdatesAutomatically);
        stack.Children.Add(new TextBlock {Text=L.T("Адрес подписанного канала выпусков"),Margin=new Thickness(0,8,0,8),FontWeight=FontWeights.SemiBold});
        var feed=new TextBox {Text=storage.State.UpdateFeed,MinHeight=42}; stack.Children.Add(feed);
        stack.Children.Add(new TextBlock {Text=L.T("HTTPS-ссылка на update-feed.json или полный путь к локальному файлу. Пакеты проверяются по подписи и SHA256, устанавливаются после закрытия приложения."),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,16),Foreground=(System.Windows.Media.Brush)FindResource("Muted")});
        stack.Children.Add(new TextBlock {Text=L.T("Последняя проверка модов: ")+(storage.State.LastUpdateCheck==""?L.T("еще не было"):storage.State.LastUpdateCheck),TextWrapping=TextWrapping.Wrap});
        stack.Children.Add(new TextBlock {Text=storage.State.LastAppUpdateStatus,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,16)});
        var error=new TextBlock {TextWrapping=TextWrapping.Wrap}; stack.Children.Add(error);
        var buttons=new WrapPanel {Margin=new Thickness(0,12,0,0)}; stack.Children.Add(buttons);
        var save=new Button {Content=L.T("Сохранить"),Style=(Style)FindResource("Primary"),Margin=new Thickness(0,0,8,0)}; buttons.Children.Add(save);
        var check=new Button {Content=L.T("Сохранить и проверить сейчас")}; buttons.Children.Add(check);
        bool Save()
        {
            try
            {
                string value=feed.Text.Trim();
                if(value!="" && !Catalogs.SafePage(value) && !Path.IsPathFullyQualified(value)) throw new IOException(L.T("Нужен HTTPS-адрес или полный путь к JSON-файлу."));
                storage.State.CheckUpdatesAutomatically=checks.IsChecked==true; storage.State.DownloadModUpdatesAutomatically=downloads.IsChecked==true;
                storage.State.ApplyOnLaunch=apply.IsChecked==true; storage.State.CheckAppUpdatesAutomatically=app.IsChecked==true; storage.State.UpdateFeed=value;
                storage.State.Language=language.SelectedIndex==1?"ru":language.SelectedIndex==2?"en":"system";storage.State.Theme=theme.SelectedIndex==1?"dark":theme.SelectedIndex==2?"light":"system";
                storage.Save();L.Set(storage.State.Language);L.Theme(storage.State.Theme);ReloadLocale(); return true;
            }
            catch(Exception ex) { error.Text=L.T(ex.Message); return false; }
        }
        save.Click+=(_,_)=>{if(Save()) window.Close();};
        check.Click+=async (_,_)=>{if(Save()) {window.Close(); await RunUpdates(false);}};
        stack.Children.Add(FeatureButton(L.T("Открыть папку данных"),()=>Process.Start(new ProcessStartInfo(storage.Root){UseShellExecute=true})));
        stack.Children.Add(FeatureButton(L.T("Открыть резервные копии"),()=> {string folder=Path.Combine(storage.Root,"backups");Directory.CreateDirectory(folder);Process.Start(new ProcessStartInfo(folder){UseShellExecute=true});}));
        window.ShowDialog();
    }
}
