using System.IO;
using System.IO.Compression;
using System.Text;
using System.Security.Cryptography;
using System.Text.Json;

namespace PocketDeadlock;

// This runner accepts an explicit scratch directory and never applies to the detected real game.
internal static class SelfTests
{
    public static async Task Run(string target,bool live=true)
    {
        Directory.CreateDirectory(target);
        string run=Path.Combine(target,Guid.NewGuid().ToString("N")); Directory.CreateDirectory(run);
        var results=new List<string>();
        void Assert(bool ok,string message) { if(!ok) throw new Exception("FAIL: "+message); results.Add("PASS: "+message); }
        void Fails(Action action,string message)
        {
            bool failed=false;
            try { action(); } catch(IOException) { failed=true; }
            Assert(failed,message);
        }
        const string config="\"GameInfo\"\r\n{\r\n  PGIVersion \"preserved\"\r\n  FileSystem\r\n  {\r\n    SearchPaths\r\n    {\r\n      Game_UILanguage citadel_*LANGUAGE*\r\n      Game citadel\r\n      Game core\r\n    }\r\n  }\r\n}\r\n";
        string mount="citadel/pocket_mods/"+new string('a',32)+"/"+new string('b',32);
        var patched=GameInstall.Patch(config,[mount]);
        Assert(patched.Contains("Game "+mount),"SearchPaths contains the new mount");
        Assert(GameInstall.RemovePatch(patched)==config,"disable returns byte-equivalent original text");
        Assert(GameInstall.Patch(patched,[mount])==patched,"patching twice does not duplicate the block");
        Assert(GameInstall.RemovePatch(patched.Replace("preserved","updated"))==config.Replace("preserved","updated"),"disable preserves an external Steam update");
        Fails(()=>GameInstall.Patch("broken",[mount]),"reject missing SearchPaths");
        Fails(()=>GameInstall.Patch(config,["../oops"]),"reject invalid mount path");
        Fails(()=>GameInstall.RemovePatch(patched.Replace("// PocketDeadlock END","")),"reject incomplete manager block");
        var bom=new byte[]{239,187,191}.Concat(Encoding.UTF8.GetBytes(config)).ToArray();
        Assert(GameInstall.Encode(GameInstall.Decode(bom),bom).SequenceEqual(bom),"preserve UTF-8 BOM");
        string vpk=Path.Combine(run,"sample.vpk"); MakeVpk(vpk,"test_resource");
        Assert(Vpk.ReadResources(vpk).Contains("materials/test_resource.vmat_c"),"read VPK resource index");
        string broken=Path.Combine(run,"bad.vpk"); File.WriteAllText(broken,"not a vpk");
        Fails(()=>Vpk.ReadResources(broken),"reject fake VPK");
        string multipart=Path.Combine(run,"multi.vpk"); MakeVpk(multipart,"multipart",0);
        Fails(()=>Vpk.ReadResources(multipart),"reject unsupported split VPK");
        var storage=new ModStorage(Path.Combine(run,"data"));
        var prepared=storage.Prepare(vpk,CancellationToken.None);
        var mod=storage.Install(prepared,prepared.Files,"Synthetic mod"); storage.CleanupStaging(prepared.Folder);
        Assert(storage.State.Mods.Count==1 && !mod.Enabled,"import defaults to inactive");
        Assert(new ModStorage(storage.Root).State.Mods.Count==1,"state survives reopening");
        string zip=Path.Combine(run,"valid.zip");
        using(var a=ZipFile.Open(zip,ZipArchiveMode.Create)) { a.CreateEntryFromFile(vpk,"nested/model.vpk"); using var w=new StreamWriter(a.CreateEntry("do-not-run.exe").Open()); w.Write("ignored"); }
        prepared=storage.Prepare(zip,CancellationToken.None);
        Assert(prepared.Files.SequenceEqual(new[]{"nested/model.vpk"}),"extract only VPK from a nested ZIP"); storage.CleanupStaging(prepared.Folder);
        string evil=Path.Combine(run,"traversal.zip");
        using(var a=ZipFile.Open(evil,ZipArchiveMode.Create)) a.CreateEntryFromFile(vpk,"../outside.vpk");
        Fails(()=>storage.Prepare(evil,CancellationToken.None),"reject archive path traversal");
        Assert(!File.Exists(Path.Combine(storage.Root,"staging","outside.vpk")),"path traversal writes nothing outside extraction directory");
        string root=Path.Combine(run,"fake-game");
        Directory.CreateDirectory(Path.Combine(root,"game","bin","win64"));
        File.WriteAllText(Path.Combine(root,"game","bin","win64","deadlock.exe"),"test placeholder, never executed");
        Directory.CreateDirectory(Path.GetDirectoryName(GameInstall.ConfigPath(root))!);
        File.WriteAllBytes(GameInstall.ConfigPath(root),bom);
        var legacy=Path.Combine(root,"game","citadel","addons"); Directory.CreateDirectory(legacy);
        File.Copy(vpk,Path.Combine(legacy,"pak01_dir.vpk")); string legacyHash=ModStorage.Hash(Path.Combine(legacy,"pak01_dir.vpk"));
        storage.State.GamePath=root; mod.Enabled=true; storage.Save();
        var game=new GameInstall(storage,true); game.Apply(CancellationToken.None);
        Assert(game.IsApplied(),"apply mounts a complete copied mod generation");
        Assert(Directory.GetFiles(Path.Combine(root,"game","citadel","pocket_mods"),"*.vpk",SearchOption.AllDirectories).Length==1,"apply copies selected VPK");
        var once=File.ReadAllBytes(GameInstall.ConfigPath(root)); game.Apply(CancellationToken.None);
        Assert(File.ReadAllBytes(GameInstall.ConfigPath(root)).SequenceEqual(once) && Directory.GetFiles(Path.Combine(root,"game","citadel","pocket_mods"),"*.vpk",SearchOption.AllDirectories).Length==1,"repeated Apply reuses verified files and does not duplicate disk space");
        Assert(File.ReadAllBytes(Directory.GetFiles(Path.Combine(storage.Root,"backups"),"*.gi")[0]).SequenceEqual(bom),"backup preserves original config bytes");
        Assert(ModStorage.Hash(Path.Combine(legacy,"pak01_dir.vpk"))==legacyHash,"legacy addons remain unchanged");
        prepared=storage.Prepare(vpk,CancellationToken.None);
        var duplicate=storage.Install(prepared,prepared.Files,"Conflicting mod"); storage.CleanupStaging(prepared.Folder); duplicate.Enabled=true;
        var before=File.ReadAllBytes(GameInstall.ConfigPath(root));
        Fails(()=>game.Apply(CancellationToken.None),"block two mods replacing the same resource");
        Assert(File.ReadAllBytes(GameInstall.ConfigPath(root)).SequenceEqual(before),"a conflict leaves config unchanged");
        mod.AllowOverrides=true; game.Apply(CancellationToken.None);
        Assert(game.IsApplied(),"explicit higher-priority override permits overlapping resources");
        string order=File.ReadAllText(GameInstall.ConfigPath(root));
        Assert(order.IndexOf("/"+mod.Id,StringComparison.Ordinal)<order.IndexOf("/"+duplicate.Id,StringComparison.Ordinal),"higher-priority mod mounts first");
        mod.AllowOverrides=false;
        duplicate.Enabled=false;
        before=File.ReadAllBytes(GameInstall.ConfigPath(root));
        using(var cancelled=new CancellationTokenSource()) { cancelled.Cancel(); try { game.Apply(cancelled.Token); } catch(OperationCanceledException) {} }
        Assert(File.ReadAllBytes(GameInstall.ConfigPath(root)).SequenceEqual(before),"cancellation before commit leaves config unchanged");
        File.AppendAllText(Path.Combine(storage.ModFolder(mod),mod.Files[0]),"tampered");
        Fails(()=>game.Apply(CancellationToken.None),"detect modified library VPK by SHA256");
        game.Disable(); Assert(File.ReadAllBytes(GameInstall.ConfigPath(root)).SequenceEqual(bom),"full apply-disable roundtrip preserves original BOM and CRLF");
        game.Disable(); Assert(!game.IsApplied(),"disable is idempotent");
        Assert(!GameBanana.TrustedUrl("https://gamebanana.com.evil.example/download") && !GameBanana.TrustedUrl("http://gamebanana.com/dl/1") && !GameBanana.TrustedUrl("https://gamebanana.com:8443/dl/1"),"reject untrusted download hosts and insecure URLs");
        await UpdateTests(run,Assert,Fails);
        L.Set("en");Assert(L.T("Игра запущена - изменения ожидают применения. Можно скачивать и выбирать моды.")=="Game is running. Changes are pending; downloading and choosing mods is available.","English running-game protection label is fully localized");
        L.Set("ru");Assert(L.T("Настройки")=="Настройки","Russian interface preserves native text");
        var profiles=new Profiles(storage); var profile=profiles.Capture("Test profile");mod.Enabled=false;duplicate.Enabled=true;
        int missing=profiles.Select(profile);Assert(missing==0 && mod.Enabled && !duplicate.Enabled && storage.State.PendingApply,"profile restores selection and leaves game application pending");
        string exported=Path.Combine(run,"profile.json");Profiles.Export(profile,exported);var imported=profiles.Import(exported);
        Assert(imported.Id!=profile.Id && imported.Mods.Count==profile.Mods.Count,"profile export and import preserve loadout entries with a fresh identity");
        var shared=new ModProfile("private-id","Shared bars",[new ProfileEntry("local-private-id",722688,"Mod",1834173,"classichealthbars-with-damage-flash",true,true,["main/pak01_dir.vpk"])]);
        string key=Profiles.Key(shared);var sharedCopy=profiles.FromKey(key);
        Assert(sharedCopy.Mods[0].Id=="" && sharedCopy.Mods[0].SelectedPaths!.SequenceEqual(shared.Mods[0].SelectedPaths!),"loadout key transfers variant and VPK selection without private local identifiers");
        Assert(sharedCopy.Mods[0].AllowOverrides && sharedCopy.Mods[0].FileId==1834173,"loadout key preserves priority permission and exact download variant");
        Fails(()=>profiles.FromKey(key.Replace(key[5],key[5]=='A'?'B':'A')),"corrupted loadout key is rejected");
        Fails(()=>Profiles.Key(profile),"local-only VPK cannot produce a misleading automatic-install key");
        var indexed=new HybridCatalog("Test",new FixtureCatalog(new ModDetails(new CatalogItem(1,"Mod","Indexed Haze","Test","Skins","","",1),"","",[]),""),Path.Combine(run,"index"));
        indexed.Merge([new(1,"Mod","Indexed Haze","Author","Skins","","",1,Downloads:10),new(2,"Mod","Health UI","Author","UI","","",2,Downloads:50)]);
        var instant=await indexed.Query("Mod","Haze",1,"",0,CancellationToken.None);
        Assert(instant.Items.Single().Id==1,"local catalog index answers search without a remote request");
        var reopened=new HybridCatalog("Test",new FixtureCatalog(new ModDetails(new CatalogItem(999,"Mod","Unused","","","","",0),"","",[]),""),Path.Combine(run,"index"));
        instant=await reopened.Query("Mod","",1,"",2,CancellationToken.None);
        Assert(instant.Total==2 && instant.Items[0].Id==2,"disk catalog index survives restart and sorts across the collection");
        instant=await reopened.Query("Mod","",1,"UI",0,CancellationToken.None);
        Assert(instant.Total==1 && instant.Items[0].Id==2,"catalog category filter uses the whole index");
        if(live)
        {
        var provider=new GameBanana();
        var list=await provider.Browse("Mod","",1,CancellationToken.None);
        Assert(list.Items.Count>0 && list.Total>0,"live GameBanana mods catalog");
        var search=await provider.Browse("Mod","Haze",1,CancellationToken.None);
        Assert(search.Items.Count>0 && search.Items.All(x=>x.Kind=="Mod"),"live global search scoped to Deadlock mods");
        var sounds=await provider.Browse("Sound","",1,CancellationToken.None);
        Assert(sounds.Items.Count>0,"live GameBanana sounds catalog");
        var details=await provider.Details("Mod",722797,CancellationToken.None);
        Assert(details.Files.Count>0,"live mod details and download variants");
        var small=details.Files.First(x=>x.Size<2*1024*1024 && !x.Blocked);
        string downloaded=Path.Combine(run,"live"+Path.GetExtension(small.Name));
        await provider.Download(small,downloaded,new Progress<string>(),CancellationToken.None);
        Assert(File.Exists(downloaded) && new FileInfo(downloaded).Length==small.Size,"real mod download with catalog size and MD5 check");
        prepared=storage.Prepare(downloaded,CancellationToken.None);
        Assert(prepared.Files.Count>0 && prepared.Files.All(x=>Vpk.ReadResources(Path.Combine(prepared.Folder,x)).Count>0),"real downloaded RAR extracts into valid VPK");
        var real=storage.Install(prepared,prepared.Files,details.Item.Name,details,small); storage.CleanupStaging(prepared.Folder);
        mod.Enabled=false; real.Enabled=true; game.Apply(CancellationToken.None);
        Assert(game.IsApplied(),"real mod can be installed and mounted in the test game");
        game.Disable(); Assert(File.ReadAllBytes(GameInstall.ConfigPath(root)).SequenceEqual(bom),"real mod rollback restores original test config");
        foreach(var source in new[]{"Deadlocker","DeadlockMods"})
        {
            IModCatalog community=new CommunityCatalog(source);
            var communityList=await community.Browse("Mod","",1,CancellationToken.None);
            Assert(communityList.Items.Count>0 && communityList.Total>0,"live "+source+" catalog");
            var linked=communityList.Items.First(x=>x.Id>0);
            var linkedInfo=await community.Details(linked,CancellationToken.None);
            Assert(linkedInfo.Item.Provider==source && linkedInfo.Files.Count>0,"live "+source+" details resolve original file variants");
            var communitySounds=await community.Browse("Sound","",1,CancellationToken.None);
            Assert(source=="Deadlocker"?communitySounds.Items.Count==0:communitySounds.Items.Count>0,source=="Deadlocker"?"Deadlocker mod-only catalog reports sound availability accurately":"live "+source+" sound catalog");
        }
        }
        var report=$"{results.Count} checks passed.\nTest directory: {run}\nReal game was not modified.\n\n"+string.Join("\n",results);
        File.WriteAllText(Path.Combine(target,"test-results.txt"),report);
    }
    static async Task UpdateTests(string run,Action<bool,string> assert,Action<Action,string> fails)
    {
        var token=CancellationToken.None; var progress=new Progress<string>();
        string a=Path.Combine(run,"original.vpk"),b=Path.Combine(run,"updated.vpk"); MakeVpk(a,"original"); MakeVpk(b,"updated");
        var store=new ModStorage(Path.Combine(run,"update-data"));
        var item=new CatalogItem(1,"Mod","Test variants","Test","UI","","https://gamebanana.com/mods/1",1);
        var originalFile=new RemoteFile(10,"bars-with-flash_aaaa.zip",1,"","OLD",false);
        var originalDetails=new ModDetails(item,"","",[originalFile]);
        var prep=store.Prepare(a,token); var old=store.Install(prep,prep.Files,item.Name,originalDetails,originalFile); store.CleanupStaging(prep.Folder);
        old.Enabled=true; old.AllowOverrides=true; string oldId=old.Id,oldFolder=store.ModFolder(old);
        string archive=Path.Combine(run,"update.zip");
        using(var zip=ZipFile.Open(archive,ZipArchiveMode.Create)) zip.CreateEntryFromFile(b,"original.vpk");
        var updateFile=originalFile with {Id=11,Name="bars-with-flash_bbbb.zip",Md5="NEW"};
        var wrong=updateFile with {Id=12,Name="bars-without-flash_cccc.zip"};
        var updateInfo=new ModDetails(item with {Modified=2},"","",[wrong,updateFile]);
        assert(ModUpdates.MatchVariant(old,updateInfo.Files)?.Id==11,"variant matching preserves with-flash versus without-flash");
        assert(ModUpdates.MatchVariant(old,[updateFile,updateFile with {Id=13}])==null,"ambiguous variant is never chosen automatically");
        assert(ModUpdates.MatchVariant(new LibraryMod {FileId=10},[originalFile])?.Id==10,"legacy library recovers exact selected file ID");
        var fake=new FixtureCatalog(updateInfo,archive); var updater=new ModUpdates(store,fake);
        var result=await updater.Check(true,progress,token); var current=store.State.Mods.Single();
        assert(result.Updated==1 && current.Id==oldId && current.Enabled && current.AllowOverrides,"auto update preserves mod identity, activation and priority permission");
        assert(current.Revisions.Count==1 && Directory.Exists(oldFolder) && current.Hashes[0]==ModStorage.Hash(b),"auto update retains previous version and installs verified new VPK");
        assert(store.State.PendingApply && new ModStorage(store.Root).State.Mods[0].ContentId==current.ContentId,"new version and pending application survive restart");
        result=await updater.Check(true,progress,token); assert(result.Updated==0 && current.Revisions.Count==1,"unchanged mod is not downloaded repeatedly");
        var rolled=store.Rollback(current);
        assert(rolled.Id==oldId && !rolled.AutoUpdate && rolled.Hashes[0]==ModStorage.Hash(a),"rollback restores original VPK and disables automatic re-update");
        result=await updater.Check(true,progress,token); assert(result.Updated==0 && result.Available==1,"per-mod update switch prevents automatic replacement");
        rolled.AutoUpdate=true; fake.Info=updateInfo with {Files=[wrong]};
        result=await updater.Check(true,progress,token); assert(result.Manual==1 && store.State.Mods[0].FileId==10,"removed variant requires manual choice and preserves installed version");
        fake.Info=updateInfo; fake.FailDownload=true;
        result=await updater.Check(true,progress,token); assert(result.Errors==1 && store.State.Mods[0].FileId==10,"download failure preserves original mod");
        fake.FailDownload=false;
        string choices=Path.Combine(run,"changed-selection.zip");
        using(var zip=ZipFile.Open(choices,ZipArchiveMode.Create)) {zip.CreateEntryFromFile(b,"choice-a.vpk"); zip.CreateEntryFromFile(a,"choice-b.vpk");}
        fake.Archive=choices;
        assert(!await updater.Install(rolled,updateInfo,updateFile,progress,token) && store.State.Mods[0].FileId==10,"changed VPK alternatives require manual choice");
        File.AppendAllText(Path.Combine(store.Root,"mods",current.ContentId,current.Files[0]),"tampered");
        fails(()=>store.Rollback(rolled),"rollback rejects a corrupted previous version");
        using var key=ECDsa.Create(); key.KeySize=256; string publicKey=key.ExportSubjectPublicKeyInfoPem();
        AppRelease Sign(AppRelease r)=>r with {Signature=Convert.ToBase64String(key.SignData(Encoding.UTF8.GetBytes(r.SignedText),HashAlgorithmName.SHA256))};
        string package=Path.Combine(run,"app-fixture.zip");
        using(var zip=ZipFile.Open(package,ZipArchiveMode.Create))
        {
            zip.CreateEntryFromFile(typeof(AppUpdates).Assembly.Location,"app/PocketDeadlock.dll");
            foreach(var name in new[]{"PocketDeadlock.exe","PocketDeadlock.deps.json","PocketDeadlock.runtimeconfig.json","System.Private.CoreLib.dll"})
                using(var writer=new StreamWriter(zip.CreateEntry("app/"+name).Open())) writer.Write("Fixture - never executed");
        }
        var release=Sign(new AppRelease(AppUpdates.CurrentVersion.ToString(3),package,ModStorage.Hash(package),new FileInfo(package).Length,""));
        AppUpdates.Verify(release,publicKey); assert(true,"release signature verifies against its public key");
        fails(()=>AppUpdates.Verify(release with {Sha256=new string('0',64)},publicKey),"modified release signature is rejected");
        string feed=Path.Combine(run,"update-feed.json"); File.WriteAllText(feed,JsonSerializer.Serialize(release));
        var app=new AppUpdates(Path.Combine(run,"app-data"),publicKey,new Version(0,1,0,0));
        assert((await app.Check(feed,token))?.Version==release.Version,"signed local feed detects a newer version");
        string stage=await app.Stage(release,progress,token);
        assert(File.Exists(Path.Combine(stage,"PocketDeadlock.dll")),"signed package stages successfully with three-part version");
        string job=AppUpdates.CreateJob(stage,Path.Combine(run,"unused-target"),0,release,false);
        assert(File.Exists(Path.Combine(Path.GetDirectoryName(job)!,"package.zip")),"update job locates signed archive through nested package folders");
        bool rejected=false;
        try { await app.Stage(Sign(release with {Sha256=new string('0',64)}),progress,token); } catch(IOException) {rejected=true;}
        assert(rejected,"signed feed with wrong package hash is rejected");
        rejected=false;
        try {await new AppUpdates(Path.Combine(run,"same-app"),publicKey,AppUpdates.CurrentVersion).Stage(release,progress,token);} catch(IOException) {rejected=true;}
        assert(rejected,"same-version or older app package cannot be installed");
        string evil=Path.Combine(run,"app-traversal.zip");
        using(var zip=ZipFile.Open(evil,ZipArchiveMode.Create)) using(var w=new StreamWriter(zip.CreateEntry("../outside.txt").Open())) w.Write("bad");
        fails(()=>AppUpdates.Extract(evil,Path.Combine(run,"safe-app"),token),"app update rejects ZIP path traversal");
        File.WriteAllText(Path.Combine(run,"legacy-state.json"),"{}");
        var state=JsonSerializer.Deserialize<AppState>("{\"Mods\":[]}")!;
        assert(state.CheckUpdatesAutomatically && state.CheckAppUpdatesAutomatically && state.UpdateFeed.Contains("github.com/AlexUner"),"legacy settings receive enabled automatic checks and the GitHub release feed");
    }
    sealed class FixtureCatalog(ModDetails details,string archive):IModCatalog
    {
        public ModDetails Info=details; public string Archive=archive; public bool FailDownload;
        public Task<CatalogPage> Browse(string kind,string search,int page,CancellationToken token)=>Task.FromResult(new CatalogPage([Info.Item],1,24));
        public Task<ModDetails> Details(string kind,long id,CancellationToken token)=>Task.FromResult(Info);
        public Task Download(RemoteFile file,string destination,IProgress<string> progress,CancellationToken token)
        {
            if(FailDownload) throw new IOException("Test download failure");
            File.Copy(Archive,destination); return Task.CompletedTask;
        }
    }
    static void MakeVpk(string file,string name,ushort archive=0x7fff)
    {
        using var tree=new MemoryStream(); using(var w=new BinaryWriter(tree,Encoding.UTF8,true))
        {
            void S(string s) { w.Write(Encoding.UTF8.GetBytes(s)); w.Write((byte)0); }
            S("vmat_c"); S("materials"); S(name); w.Write((uint)0); w.Write((ushort)0); w.Write(archive); w.Write((uint)0); w.Write((uint)4); w.Write((ushort)0xffff); S(""); S(""); S("");
        }
        using var output=new BinaryWriter(File.Create(file)); output.Write((uint)0x55aa1234); output.Write((uint)2); output.Write((uint)tree.Length);
        output.Write((uint)4); output.Write((uint)0); output.Write((uint)0); output.Write((uint)0); output.Write(tree.ToArray()); output.Write(new byte[]{1,2,3,4});
    }
}
