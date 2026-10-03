using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace PocketDeadlock;

public sealed class GameBananaLoginWindow : Window
{
    const string Login="https://gamebanana.com/members/account/login";
    const string Signup="https://gamebanana.com/members/account/signup";
    readonly GameBananaAccount account;
    readonly WebView2 browser=new();
    readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,12,0),VerticalAlignment=VerticalAlignment.Center};
    readonly TextBlock address=new(){TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,12),FontSize=12};
    readonly Button save=new(){Content=L.T("Использовать этот аккаунт"),IsEnabled=false};
    public bool Connected {get;private set;}
    public GameBananaLoginWindow(GameBananaAccount account)
    {
        this.account=account;Title=L.T("Аккаунт GameBanana");Width=1060;Height=820;MinWidth=840;MinHeight=660;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var root=new DockPanel{Margin=new Thickness(16)};Content=root;
        var header=new StackPanel();DockPanel.SetDock(header,Dock.Top);root.Children.Add(header);
        var actions=new WrapPanel{Margin=new Thickness(0,0,0,12)};header.Children.Add(actions);
        AddAction(actions,L.T("Войти"),()=>Navigate(Login));AddAction(actions,L.T("Зарегистрироваться"),()=>Navigate(Signup));
        AddAction(actions,L.T("Сайт и настройки контента"),()=>{Navigate("https://gamebanana.com/");status.Text=L.T("Настройки видимости контента находятся в меню твоего аккаунта GameBanana.");});
        AddAction(actions,L.T("Выйти из аккаунта"),()=>{account.Forget();browser.CoreWebView2?.CookieManager.DeleteAllCookies();Connected=false;Navigate(Login);status.Text=L.T("Вход удален из менеджера.");});
        header.Children.Add(address);
        var footer=new DockPanel{Margin=new Thickness(0,12,0,0)};DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);
        DockPanel.SetDock(save,Dock.Right);footer.Children.Add(save);footer.Children.Add(status);save.Click+=async (_,_)=>await Connect();
        root.Children.Add(browser);status.Text=L.T("Войди или зарегистрируйся на GameBanana. Пароль вводится на сайте.");
        Loaded+=async (_,_)=>await Initialize();Closed+=(_,_)=>browser.Dispose();
    }
    static void AddAction(Panel panel,string text,Action action)
    {
        var button=new Button{Content=text,Margin=new Thickness(0,0,8,0)};button.Click+=(_,_)=>action();panel.Children.Add(button);
    }
    void Navigate(string url) {if(browser.CoreWebView2!=null) browser.CoreWebView2.Navigate(url);}
    async Task Initialize()
    {
        try
        {
            var environment=await CoreWebView2Environment.CreateAsync(null,account.BrowserFolder);
            await browser.EnsureCoreWebView2Async(environment);
            var core=browser.CoreWebView2;core.Settings.AreHostObjectsAllowed=false;core.Settings.IsWebMessageEnabled=false;
            core.Settings.AreDevToolsEnabled=false;core.Settings.IsStatusBarEnabled=false;
            // Keep this app's browser login until explicit logout, even if HTTP verification failed.
            core.NavigationStarting+=(_,e)=>
            {
                if(!GameBananaAccount.SessionHost(e.Uri)) {e.Cancel=true;status.Text=L.T("Открой внешнюю ссылку в обычном браузере; здесь используется только GameBanana.");return;}
                address.Text=e.Uri;
            };
            core.NewWindowRequested+=(_,e)=> {e.Handled=true;if(GameBananaAccount.SessionHost(e.Uri)) core.Navigate(e.Uri);};
            core.NavigationCompleted+=async (_,e)=>
            {
                save.IsEnabled=e.IsSuccess;
                if(e.IsSuccess && await Recognized()) status.Text=L.T("Вход выполнен. Нажми «Использовать этот аккаунт», чтобы подключить его к менеджеру.");
            };
            core.DownloadStarting+=(_,e)=> {e.Cancel=true;status.Text=L.T("Подключи аккаунт и скачай мод из каталога менеджера.");};
            Navigate(Login);
        }
        catch(WebView2RuntimeNotFoundException)
        {
            status.Text=L.T("Для входа нужен Microsoft Edge WebView2 Runtime.");
            var runtime=new Button{Content=L.T("Установить WebView2")};runtime.Click+=(_,_)=>Process.Start(new ProcessStartInfo("https://developer.microsoft.com/microsoft-edge/webview2/"){UseShellExecute=true});
            ((DockPanel)Content).Children.Add(runtime);
        }
        catch(Exception) {status.Text=L.T("Не удалось открыть окно входа. Закрой его и повтори.");}
    }
    async Task<bool> Recognized()
    {
        if(browser.CoreWebView2==null || !GameBananaAccount.SessionHost(browser.Source?.AbsoluteUri??"")) return false;
        try {return await browser.CoreWebView2.ExecuteScriptAsync("window.g_bIsGuest === false") == "true";}
        catch {return false;}
    }
    async Task Connect()
    {
        save.IsEnabled=false;
        try
        {
            if(!await Recognized()) {status.Text=L.T("Сначала заверши вход на GameBanana.");return;}
            var cookies=await browser.CoreWebView2.CookieManager.GetCookiesAsync("https://gamebanana.com/");
            account.Save(cookies.Select(x=>new AccountCookie(x.Name,x.Value,x.Domain,x.Path,x.IsSecure,x.IsHttpOnly,x.IsSession?default:x.Expires.ToUniversalTime())),true,browser.CoreWebView2.Settings.UserAgent);
            if(!await account.Verify(CancellationToken.None)) {status.Text=L.T("Сайт выполнил вход, но менеджер не подтвердил сеанс. Нажми «Использовать этот аккаунт» еще раз; повторный ввод пароля не нужен.");return;}
            Connected=true;DialogResult=true;
        }
        catch(Exception) {account.Forget();status.Text=L.T("Не удалось сохранить вход. Повтори после загрузки страницы.");}
        finally {save.IsEnabled=true;}
    }
#if DIAGNOSTICS
    internal async Task<string> ReviewGuest(string imagePath)
    {
        var limit=DateTime.UtcNow.AddSeconds(30);
        while((browser.CoreWebView2==null || !save.IsEnabled) && DateTime.UtcNow<limit) await Task.Delay(100);
        if(browser.CoreWebView2==null || !save.IsEnabled) throw new IOException("GameBanana embedded login did not load");
        await Connect();
        if(account.SignedIn || Connected) throw new IOException("Guest browser was accepted as a logged-in account");
        if(!GameBananaAccount.SessionHost(browser.Source?.AbsoluteUri??"")) throw new IOException("Login browser navigated outside GameBanana");
        using(var output=File.Create(imagePath)) await browser.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,output);
        Navigate(Signup);await Task.Delay(1200);
        if(browser.Source?.AbsolutePath!="/members/account/signup") throw new IOException("Registration action failed");
        Close();return "PASS: live embedded GameBanana login and registration\nPASS: anonymous browser cannot save a signed-in session\n";
    }
#endif
}
