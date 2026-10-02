using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;

namespace PocketDeadlock;

public partial class MainWindow
{
    event Action? managerUpdateChanged;
    string managerUpdateProgress="";
    string ManagerUpdateStatus()=>storage.State.AppUpdateState switch
    {
        "checking"=>L.T("Проверяем обновления менеджера…"),
        "downloading"=>managerUpdateProgress==""?L.T("Скачиваем обновление менеджера…"):managerUpdateProgress,
        "ready" when stagedApp!=null=>L.T("Версия ")+storage.State.AppUpdateVersion+L.T(" готова к установке."),
        "current"=>L.T("Установлена последняя версия ")+AppUpdates.CurrentVersion.ToString(3),
        "error"=>L.T("Не удалось проверить обновления. Повтори позже."),
        _=>storage.State.UpdateFeed==""?L.T("Канал выпусков менеджера еще не задан."):L.T("Канал выпусков GitHub подключен; проверка еще не выполнена")
    };
    string ManagerUpdateDate()=>DateTimeOffset.TryParse(storage.State.LastAppUpdateCheck,out var date)?date.ToLocalTime().ToString("g"):L.T("еще не было");
    void RefreshManagerUpdateUi()
    {
        if(ManagerUpdatesButton==null) return;
        bool working=storage.State.AppUpdateState is "checking" or "downloading";
        VersionLabel.Text="v"+AppUpdates.CurrentVersion.ToString(3);
        ManagerUpdatesLabel.Text=stagedApp!=null?L.T("Обновление готово"):working?L.T("Проверяем…"):storage.State.AppUpdateState=="error"?L.T("Проверить снова"):L.T("Обновления");
        ManagerUpdatesButton.ToolTip=ManagerUpdateStatus();
        AppUpdateButton.Visibility=stagedApp!=null?Visibility.Visible:Visibility.Collapsed;
        managerUpdateChanged?.Invoke();
    }
    async Task CheckManagerRelease(CancellationToken token)
    {
        if(stagedApp!=null) {RefreshManagerUpdateUi();return;}
        if(storage.State.UpdateFeed=="") {storage.State.AppUpdateState="idle";RefreshManagerUpdateUi();return;}
        storage.State.AppUpdateState="checking";storage.State.LastAppUpdateError="";managerUpdateProgress="";RefreshManagerUpdateUi();
        try
        {
            var service=new AppUpdates(storage.Root);
            var release=await service.Check(storage.State.UpdateFeed,token);
            if(release==null) storage.State.AppUpdateState="current";
            else
            {
                storage.State.AppUpdateState="downloading";storage.State.AppUpdateVersion=release.Version;appRelease=release;RefreshManagerUpdateUi();
                stagedApp=await service.Stage(release,new Progress<string>(text=>{managerUpdateProgress=L.T(text);RefreshManagerUpdateUi();}),token);
                storage.State.AppUpdateState="ready";
            }
            storage.State.LastAppUpdateCheck=DateTimeOffset.UtcNow.ToString("o");
        }
        catch(OperationCanceledException) {storage.State.AppUpdateState="idle";throw;}
        catch(Exception ex) {storage.State.AppUpdateState="error";storage.State.LastAppUpdateError=ex.Message;storage.State.LastAppUpdateCheck=DateTimeOffset.UtcNow.ToString("o");}
        finally {storage.Save();RefreshManagerUpdateUi();}
    }
    async Task CheckManagerNow()
    {
        if(busy || checkingUpdates) return;
        checkingUpdates=true;backgroundUpdate=new CancellationTokenSource();
        try {await CheckManagerRelease(backgroundUpdate.Token);StatusLabel.Text=ManagerUpdateStatus();}
        catch(OperationCanceledException) {StatusLabel.Text=L.T("Операция отменена или истекло время ожидания. Можно повторить.");}
        finally {checkingUpdates=false;backgroundUpdate.Dispose();backgroundUpdate=null;RefreshManagerUpdateUi();}
    }
    void OpenManagerUpdates(object sender,RoutedEventArgs e)
    {
        if(busy) return;
        var window=new Window {Owner=this,Title=L.T("Обновления менеджера"),Width=640,Height=540,MinWidth=500,MinHeight=420,WindowStartupLocation=WindowStartupLocation.CenterOwner};
        var root=new DockPanel {Margin=new Thickness(24)};window.Content=root;
        var actions=new WrapPanel {Margin=new Thickness(0,20,0,0)};DockPanel.SetDock(actions,Dock.Bottom);root.Children.Add(actions);
        var check=new Button {Content=L.T("Проверить сейчас"),Margin=new Thickness(0,0,8,8)};actions.Children.Add(check);
        var install=new Button {Content=L.T("Установить и перезапустить"),Style=(Style)FindResource("Primary"),Margin=new Thickness(0,0,8,8)};actions.Children.Add(install);
        var close=new Button {Content=L.T("Закрыть"),IsCancel=true,Margin=new Thickness(0,0,0,8)};actions.Children.Add(close);
        var stack=new StackPanel();root.Children.Add(new ScrollViewer {Content=stack,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
        stack.Children.Add(new TextBlock {Text="PocketDeadlock "+AppUpdates.CurrentVersion.ToString(3),FontSize=24,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,20)});
        var status=new TextBlock {FontSize=16,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap};AutomationProperties.SetLiveSetting(status,AutomationLiveSetting.Polite);stack.Children.Add(status);
        var date=new TextBlock {TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,16),Foreground=(System.Windows.Media.Brush)FindResource("Muted")};stack.Children.Add(date);
        var progress=new ProgressBar {Height=3,IsIndeterminate=true,Margin=new Thickness(0,0,0,16)};stack.Children.Add(progress);
        var explanation=new TextBlock {TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,16)};stack.Children.Add(explanation);
        var error=new TextBlock {TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,16)};stack.Children.Add(error);
        var notes=new TextBlock {TextWrapping=TextWrapping.Wrap};stack.Children.Add(notes);
        void Refresh()
        {
            bool working=storage.State.AppUpdateState is "checking" or "downloading";
            status.Text=ManagerUpdateStatus();date.Text=L.T("Последняя проверка менеджера:")+" "+ManagerUpdateDate();
            progress.Visibility=working?Visibility.Visible:Visibility.Collapsed;
            check.IsEnabled=!working && !checkingUpdates && stagedApp==null;install.Visibility=stagedApp!=null?Visibility.Visible:Visibility.Collapsed;
            explanation.Text=stagedApp!=null?L.T("Пакет проверен. Установка закроет и снова откроет менеджер. Обычное закрытие также установит подготовленное обновление."):L.T("Автоматическая проверка выполняется при запуске и каждые 30 минут согласно настройкам. Моды и файлы игры не меняются при обновлении программы.");
            error.Text=storage.State.AppUpdateState=="error"?L.T("Подробности:")+" "+L.T(storage.State.LastAppUpdateError):"";
            notes.Text=appRelease?.ReleaseNotes??"";
        }
        check.Click+=async (_,_)=>await CheckManagerNow();install.Click+=(_,_)=>{window.Close();InstallAppUpdate(this,new RoutedEventArgs());};close.Click+=(_,_)=>window.Close();
        window.KeyDown+=(_,key)=>{if(key.Key==Key.Escape) window.Close();};
        managerUpdateChanged+=Refresh;window.Closed+=(_,_)=>managerUpdateChanged-=Refresh;Refresh();window.ShowDialog();
    }
}
