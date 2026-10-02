using System.IO;
using System.Text;

namespace PocketDeadlock;

internal static partial class SelfTests
{
    static void AccountTests(string run,Action<bool,string> assert,Action<Action,string> fails)
    {
        string root=Path.Combine(run,"account-data");Directory.CreateDirectory(root);
        var account=new GameBananaAccount(root);
        var value=new AccountCookie("session_fixture","fixture-only-secret-123", ".gamebanana.com","/",true,true,DateTime.UtcNow.AddHours(1));
        fails(()=>account.Save([value],false),"anonymous login cannot be connected just because a guest cookie exists");
        fails(()=>account.Save([value with {Domain="example.com"}],true),"unrelated cookie domains cannot be saved as a GameBanana session");
        account.Save([value,value with {Name="unrelated",Domain="example.com"},value with {Name="expired",Expires=DateTime.UtcNow.AddMinutes(-1)}],true);
        var restored=new GameBananaAccount(root);
        assert(restored.SignedIn && restored.Header("https://gamebanana.com/apiv11/Mod/1/ProfilePage").Contains(value.Value),"GameBanana session survives restart under the same Windows account");
        assert(!Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(root,"gamebanana-session.dat"))).Contains(value.Value),"session file uses Windows encryption instead of plaintext cookies");
        assert(restored.Header("https://images.gamebanana.com/img.png")=="" && restored.Header("https://files.gamebanana.com/file.zip")=="" && restored.Header("https://example.com/")=="" && restored.Header("http://gamebanana.com/")=="" && restored.Header("https://gamebanana.com.evil.test/")=="","session cookies are scoped to the HTTPS GameBanana website and never sent to CDNs or other catalogs");
        assert(!restored.Header("https://gamebanana.com/").Contains("expired") && !restored.Header("https://gamebanana.com/").Contains("unrelated"),"expired and foreign cookies are discarded");
        restored.Forget();assert(!restored.SignedIn && !File.Exists(Path.Combine(root,"gamebanana-session.dat")) && restored.Header("https://gamebanana.com/")=="","logout removes the protected session and stops authenticated requests");
        File.WriteAllBytes(Path.Combine(root,"gamebanana-session.dat"),[1,2,3]);
        assert(!new GameBananaAccount(root).SignedIn,"invalid protected session does not crash startup or imply a login");
        var normal=new CatalogItem(1,"Mod","Regular mod","Author","Skins","","https://gamebanana.com/mods/1",1);
        var sensitive=normal with {Id=2,ContentRatings="Sensitive content",InitialVisibility="hide"};
        var warning=normal with {Id=3,InitialVisibility="warn"};
        assert(CatalogContent.Matches(normal,"safe",false) && !CatalogContent.Matches(sensitive,"all",false) && CatalogContent.Matches(sensitive,"sensitive",true),"sensitive catalog records become available with a connected account and the content filter");
        assert(!CatalogContent.Matches(sensitive,"safe",true) && !CatalogContent.Matches(normal,"sensitive",true) && CatalogContent.Matches(warning,"sensitive",false),"regular, sensitive and all-available filters remain independent of hero and mod type");
        var merged=UnifiedCatalog.Merge([normal,sensitive with {Id=1,Provider="DeadlockMods"}]).Single().Item;
        assert(CatalogContent.Sensitive(merged) && merged.InitialVisibility=="hide","provider deduplication retains sensitive-content metadata");
    }
}
