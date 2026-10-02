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
        RefreshManagerUpdateUi();
        if(!enabled) return;
        updateTimer=new DispatcherTimer { Interval=TimeSpan.FromMinutes(30) };
        updateTimer.Tick+=async (_,_)=> {_ = sources.Warm(CancellationToken.None);await ScheduledUpdates();}; updateTimer.Start();
    }
    async Task ScheduledUpdates()
    {
        if(busy || checkingUpdates || ActiveTransfers>0 || !storage.State.CheckUpdatesAutomatically && !storage.State.CheckAppUpdatesAutomatically) return;
        await RunUpdates(true);
    }
    async Task RunUpdates(bool scheduled,bool includeManager=true)
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
        if(includeManager && (!scheduled || storage.State.CheckAppUpdatesAutomatically) && storage.State.UpdateFeed!="" && stagedApp==null)
        {
            await CheckManagerRelease(token);message+=ManagerUpdateStatus();
        }
        else if(includeManager && storage.State.UpdateFeed=="") message+=L.T("Канал выпусков менеджера еще не задан.");
        StatusLabel.Text=message;
    }
    if(!scheduled) {await Run(L.T(includeManager?"Проверяем обновления модов и менеджера…":"Проверяем обновления модов…"),Check);return;}
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
}
