using System.Windows;
using System.Windows.Controls;

namespace PocketDeadlock;

public partial class MainWindow
{
    string contentFilter="all";
    void RefreshAccount()=>AccountButton.Content=L.T(App.Account?.SignedIn==true?"Аккаунт GameBanana":"Войти GameBanana");
    async void OpenAccount(object sender,RoutedEventArgs e)
    {
        if(busy || App.Account==null) return;
        var item=CatalogList.SelectedItem as CatalogItem;
        var login=new GameBananaLoginWindow(App.Account){Owner=this};login.ShowDialog();RefreshAccount();
        // Cookie-aware catalog requests replace the guest search cache after login or logout.
        sources.ResetSearch();
        if(!library && FeatureScreen.Visibility!=Visibility.Visible) await LoadCatalog();
        _=sources.Warm(CancellationToken.None);
        if(login.Connected)
        {
            StatusLabel.Text=L.T("Аккаунт подключен. Каталог учитывает доступ GameBanana; выбери нужный фильтр контента.");
            if(item!=null) {CatalogList.SelectedItem=catalogRows.FirstOrDefault(x=>UnifiedCatalog.Identity(x)==UnifiedCatalog.Identity(item));lastDetail=ShowDetail(item);await lastDetail;}
            else if(Uri.TryCreate(SearchBox.Text,UriKind.Absolute,out var url) && GameBanana.TrustedUrl(url.AbsoluteUri)) await SearchNow();
        }
    }
    async void ContentFilterChanged(object sender,SelectionChangedEventArgs e)
    {
        contentFilter=ContentFilterBox?.SelectedIndex switch {1=>"safe",2=>"sensitive",_=>"all"};
        if(IsLoaded && !busy && !settingFilters)
        {
            await ApplyCatalogFilter();
            if(contentFilter=="sensitive" && App.Account?.SignedIn!=true) StatusLabel.Text=L.T("Для скрытых модов войди в GameBanana и проверь настройки контента аккаунта.");
        }
    }
}
