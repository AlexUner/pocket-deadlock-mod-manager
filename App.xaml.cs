using System.IO;
using System.Windows;

namespace PocketDeadlock;

public partial class App : Application
{
    internal static string PreviewMode="";
    internal static bool PreviewTileErrors;
    internal static ThumbnailCache? Thumbnails;
    Mutex? instance;
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            if(e.Args.Length>=2 && e.Args[0]=="--apply-update")
            {
                ShutdownMode=ShutdownMode.OnExplicitShutdown;
                try { await AppUpdates.ApplyJob(Path.GetFullPath(e.Args[1])); Shutdown(0); } catch { Shutdown(1); } return;
            }
            if(e.Args.Length>=2 && e.Args[0]=="--self-test")
            {
                ShutdownMode=ShutdownMode.OnExplicitShutdown;
                await SelfTests.Run(Path.GetFullPath(e.Args[1]),!e.Args.Contains("--offline")); Shutdown(0); return;
            }
            if(e.Args.Length>=4 && e.Args[0]=="--prepare-update")
            {
                ShutdownMode=ShutdownMode.OnExplicitShutdown;
                var updater=new AppUpdates(Path.GetFullPath(e.Args[2]),UpdateKey.PublicPem,AppUpdates.ReadVersion(Path.Combine(e.Args[3],"PocketDeadlock.dll")));
                var release=await updater.Check(e.Args[1],CancellationToken.None)??throw new IOException("No newer release");
                string stage=await updater.Stage(release,new Progress<string>(),CancellationToken.None);
                string job=AppUpdates.CreateJob(stage,e.Args[3],0,release,false);
                File.WriteAllText(Path.Combine(e.Args[2],"prepared.json"),System.Text.Json.JsonSerializer.Serialize(new {Job=job,Stage=stage}));Shutdown(0);return;
            }
            bool preview=e.Args.Length>=2 && e.Args[0]=="--preview";
            if(!preview)
            {
                instance=new Mutex(true,"Local\\PocketDeadlock-Manager",out bool first);
                if(!first) { MessageBox.Show("PocketDeadlock уже открыт."); Shutdown(); return; }
            }
            string root=preview?Path.Combine(Path.GetDirectoryName(Path.GetFullPath(e.Args[1]))!,"preview-data")
                :Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"PocketDeadlock");
            var storage=new ModStorage(root);
            Thumbnails=new ThumbnailCache(Path.Combine(root,"thumbnails"));
            if(preview) {if(e.Args.Contains("--en")) storage.State.Language="en";if(e.Args.Contains("--ru")) storage.State.Language="ru";if(e.Args.Contains("--light")) storage.State.Theme="light";if(e.Args.Contains("--dark")) storage.State.Theme="dark";PreviewMode=e.Args.FirstOrDefault(x=>x is "--library" or "--profiles" or "--downloads" or "--categories" or "--tiles")??"";}
            PreviewTileErrors=preview && e.Args.Contains("--tile-errors");
            L.Set(storage.State.Language);L.Theme(storage.State.Theme);
            if(!GameInstall.Valid(storage.State.GamePath)) { storage.State.GamePath=GameInstall.Detect(); storage.Save(); }
            var window=new MainWindow(storage,preview?Path.GetFullPath(e.Args[1]):null);
            if(preview && e.Args.Contains("--small")) {window.Width=window.MinWidth;window.Height=window.MinHeight;}
            MainWindow=window; window.Show();
        }
        catch(Exception ex)
        {
            if(e.Args.Length>=2 && e.Args[0]=="--self-test")
            {
                Directory.CreateDirectory(e.Args[1]); File.WriteAllText(Path.Combine(e.Args[1],"FAILURE.txt"),ex.ToString());
            }
            else MessageBox.Show(ex.Message,"PocketDeadlock",MessageBoxButton.OK,MessageBoxImage.Error);
            Shutdown(1);
        }
    }
    protected override void OnExit(ExitEventArgs e) { instance?.Dispose(); base.OnExit(e); }
}
