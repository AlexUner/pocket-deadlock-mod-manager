using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace PocketDeadlock;

public partial class MainWindow
{
    void UpdateSettings(object sender,RoutedEventArgs e)
    {
        if(busy || checkingUpdates) return;
        var window=new Window {Owner=this,Title=L.T("Настройки PocketDeadlock"),Width=740,Height=760,MinWidth=600,MinHeight=560,WindowStartupLocation=WindowStartupLocation.CenterOwner};
        var root=new DockPanel {Margin=new Thickness(24)};window.Content=root;
        var footer=new StackPanel {Margin=new Thickness(0,16,0,0)};DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);
        var error=new TextBlock {TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,8)};footer.Children.Add(error);
        var buttons=new WrapPanel();footer.Children.Add(buttons);
        var save=new Button {Content=L.T("Сохранить"),Style=(Style)FindResource("Primary"),Margin=new Thickness(0,0,8,8)};buttons.Children.Add(save);
        var check=new Button {Content=L.T("Сохранить и проверить сейчас"),Margin=new Thickness(0,0,8,8)};buttons.Children.Add(check);
        var cancel=new Button {Content=L.T("Отмена"),IsCancel=true,Margin=new Thickness(0,0,0,8)};buttons.Children.Add(cancel);
        var stack=new StackPanel();root.Children.Add(new ScrollViewer {Content=stack,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
        void Heading(string text)=>stack.Children.Add(new TextBlock {Text=L.T(text),FontSize=20,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,16,0,12)});
        CheckBox Check(string text,bool value) {var box=new CheckBox {Content=new TextBlock {Text=L.T(text),TextWrapping=TextWrapping.Wrap},IsChecked=value,Margin=new Thickness(0,0,0,12)};stack.Children.Add(box);return box;}
        Heading("Интерфейс");
        stack.Children.Add(new TextBlock {Text=L.T("Язык интерфейса"),Margin=new Thickness(0,0,0,8)});
        var language=new ComboBox {ItemsSource=new[]{L.T("Как в Windows"),L.T("Русский"),"English"},SelectedIndex=storage.State.Language=="ru"?1:storage.State.Language=="en"?2:0,Margin=new Thickness(0,0,0,16)};stack.Children.Add(language);
        stack.Children.Add(new TextBlock {Text=L.T("Тема оформления"),Margin=new Thickness(0,0,0,8)});
        var theme=new ComboBox {ItemsSource=new[]{L.T("Как в Windows"),L.T("Темная"),L.T("Светлая")},SelectedIndex=storage.State.Theme=="dark"?1:storage.State.Theme=="light"?2:0,Margin=new Thickness(0,0,0,16)};stack.Children.Add(theme);
        System.Windows.Automation.AutomationProperties.SetName(language,L.T("Язык интерфейса"));System.Windows.Automation.AutomationProperties.SetName(theme,L.T("Тема оформления"));
        Heading("Моды и запуск");
        var checks=Check("Проверять моды при запуске и каждые 30 минут",storage.State.CheckUpdatesAutomatically);
        var downloads=Check("Автоматически скачивать обновления выбранного варианта",storage.State.DownloadModUpdatesAutomatically);
        var apply=Check("Применять выбранный набор перед запуском Deadlock",storage.State.ApplyOnLaunch);
        Heading("Обновления менеджера");
        var app=Check("Проверять и готовить обновления самого менеджера",storage.State.CheckAppUpdatesAutomatically);
        stack.Children.Add(new TextBlock {Text="PocketDeadlock "+AppUpdates.CurrentVersion.ToString(3)+" · "+ManagerUpdateStatus(),TextWrapping=TextWrapping.Wrap,Foreground=(System.Windows.Media.Brush)FindResource("Muted"),Margin=new Thickness(0,0,0,12)});
        var advanced=new StackPanel {Margin=new Thickness(0,12,0,0)};
        stack.Children.Add(new Expander {Header=new TextBlock {Text=L.T("Дополнительно"),Foreground=(System.Windows.Media.Brush)FindResource("Ink")},Content=advanced,Foreground=(System.Windows.Media.Brush)FindResource("Ink"),Margin=new Thickness(0,8,0,16)});
        advanced.Children.Add(new TextBlock {Text=L.T("Адрес подписанного канала выпусков"),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,8)});
        var feed=new TextBox {Text=storage.State.UpdateFeed,MinHeight=42};advanced.Children.Add(feed);System.Windows.Automation.AutomationProperties.SetName(feed,L.T("Адрес подписанного канала выпусков"));
        advanced.Children.Add(new TextBlock {Text=L.T("HTTPS-ссылка на update-feed.json или полный путь к локальному файлу. Пакеты проверяются по подписи и SHA256, устанавливаются после закрытия приложения."),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,16),Foreground=(System.Windows.Media.Brush)FindResource("Muted")});
        advanced.Children.Add(FeatureButton(L.T("Открыть папку данных"),()=>Process.Start(new ProcessStartInfo(storage.Root){UseShellExecute=true})));
        advanced.Children.Add(FeatureButton(L.T("Открыть резервные копии"),()=>{string folder=Path.Combine(storage.Root,"backups");Directory.CreateDirectory(folder);Process.Start(new ProcessStartInfo(folder){UseShellExecute=true});}));
        bool Save()
        {
            try
            {
                string value=feed.Text.Trim();
                if(value!="" && !Catalogs.SafePage(value) && !Path.IsPathFullyQualified(value)) throw new IOException(L.T("Нужен HTTPS-адрес или полный путь к JSON-файлу."));
                storage.State.CheckUpdatesAutomatically=checks.IsChecked==true;storage.State.DownloadModUpdatesAutomatically=downloads.IsChecked==true;
                storage.State.ApplyOnLaunch=apply.IsChecked==true;storage.State.CheckAppUpdatesAutomatically=app.IsChecked==true;storage.State.UpdateFeed=value;
                storage.State.Language=language.SelectedIndex==1?"ru":language.SelectedIndex==2?"en":"system";storage.State.Theme=theme.SelectedIndex==1?"dark":theme.SelectedIndex==2?"light":"system";
                storage.Save();L.Set(storage.State.Language);L.Theme(storage.State.Theme);ReloadLocale();return true;
            }
            catch(Exception ex) {error.Text=L.T(ex.Message);return false;}
        }
        save.Click+=(_,_)=>{if(Save()) window.Close();};cancel.Click+=(_,_)=>window.Close();
        check.Click+=async (_,_)=>{if(Save()){window.Close();await RunUpdates(false);}};
        window.ShowDialog();
    }
}
