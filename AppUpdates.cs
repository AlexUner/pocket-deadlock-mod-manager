using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PocketDeadlock;

public sealed record AppRelease(string Version,string PackageUrl,string Sha256,long Size,string Signature,string ReleaseNotes="")
{
    public string SignedText=>$"{Version}\n{PackageUrl}\n{Sha256.ToUpperInvariant()}\n{Size}";
}
public sealed record UpdateJob(string Stage,string Target,int OldPid,AppRelease Release,bool Restart=true);
public sealed class AppUpdates
{
    public static Version CurrentVersion=>Assembly.GetExecutingAssembly().GetName().Version??new Version(0,2,0);
    readonly string root,key;
    readonly Version current;
    static readonly HttpClient Http=new(new HttpClientHandler{AllowAutoRedirect=false}){Timeout=TimeSpan.FromMinutes(10)};
    public AppUpdates(string dataRoot):this(dataRoot,UpdateKey.PublicPem,CurrentVersion){}
    internal AppUpdates(string dataRoot,string publicKey,Version version) { root=Path.Combine(dataRoot,"updates"); key=publicKey; current=version; }
    internal static Version ParseVersion(string value)
    {
        var v=Version.Parse(value); return new Version(v.Major,v.Minor,Math.Max(0,v.Build),Math.Max(0,v.Revision));
    }
    internal static void Verify(AppRelease release,string publicKey)
    {
        if(!Version.TryParse(release.Version,out _) || !Regex.IsMatch(release.Sha256,"^[a-fA-F0-9]{64}$") || release.Size<=0 || release.Size>250L*1024*1024) throw new IOException("Неверные данные выпуска менеджера.");
        using var ecdsa=ECDsa.Create(); ecdsa.ImportFromPem(publicKey);
        byte[] signature;
        try { signature=Convert.FromBase64String(release.Signature); } catch(FormatException) { throw new IOException("Неверная подпись выпуска."); }
        if(!ecdsa.VerifyData(Encoding.UTF8.GetBytes(release.SignedText),signature,HashAlgorithmName.SHA256)) throw new IOException("Подпись выпуска не совпала. Обновление остановлено.");
    }
    public async Task<AppRelease?> Check(string feed,CancellationToken token)
    {
        if(string.IsNullOrWhiteSpace(feed)) return null;
        using var content=await Open(feed,1024*1024,token);
        var release=await JsonSerializer.DeserializeAsync<AppRelease>(content,cancellationToken:token)??throw new IOException("Пустой канал обновления.");
        Verify(release,key);
        return ParseVersion(release.Version)>ParseVersion(current.ToString())?release:null;
    }
    static async Task<MemoryStream> Open(string address,long limit,CancellationToken token)
    {
        var memory=new MemoryStream();
        try
        {
            if(Path.IsPathFullyQualified(address) && !address.StartsWith("http",StringComparison.OrdinalIgnoreCase))
            {
                using var input=File.OpenRead(address); await GameBanana.CopyLimited(input,memory,limit,token);
            }
            else
            {
                string url=address;
                for(int i=0;i<6;i++)
                {
                    if(!Uri.TryCreate(url,UriKind.Absolute,out var uri) || uri.Scheme!="https" || !uri.IsDefaultPort || uri.UserInfo!="") throw new IOException("Для обновлений нужен адрес HTTPS или полный путь к локальному файлу.");
                    using var response=await Http.GetAsync(uri,HttpCompletionOption.ResponseHeadersRead,token);
                    if((int)response.StatusCode is >=300 and <400 && response.Headers.Location!=null) { url=new Uri(uri,response.Headers.Location).AbsoluteUri; continue; }
                    response.EnsureSuccessStatusCode();
                    using var input=await response.Content.ReadAsStreamAsync(token); await GameBanana.CopyLimited(input,memory,limit,token); break;
                }
                if(memory.Length==0) throw new IOException("Не удалось получить пакет обновления.");
            }
            memory.Position=0; return memory;
        }
        catch { memory.Dispose(); throw; }
    }
    public async Task<string> Stage(AppRelease release,IProgress<string> progress,CancellationToken token)
    {
        Verify(release,key); if(ParseVersion(release.Version)<=ParseVersion(current.ToString())) throw new IOException("Версия обновления должна быть новее установленной.");
        string dir=Path.Combine(root,Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir); ModStorage.RejectLinks(dir);
        progress.Report("Скачиваем обновление менеджера "+release.Version+"…");
        using var data=await Open(release.PackageUrl,250L*1024*1024,token);
        if(data.Length!=release.Size || Convert.ToHexString(SHA256.HashData(data)).ToUpperInvariant()!=release.Sha256.ToUpperInvariant()) throw new IOException("Контрольная сумма пакета менеджера не совпала.");
        data.Position=0; string zip=Path.Combine(dir,"package.zip");
        using(var output=File.Create(zip)) await data.CopyToAsync(output,token);
        string stage=Path.Combine(dir,"stage"); Directory.CreateDirectory(stage);
        Extract(zip,stage,token);
        var executables=Directory.GetFiles(stage,"PocketDeadlock.exe",SearchOption.AllDirectories);
        if(executables.Length!=1) throw new IOException("В пакете отсутствует единственный PocketDeadlock.exe.");
        string app=Path.GetDirectoryName(executables[0])!;
        if(ReadVersion(Path.Combine(app,"PocketDeadlock.dll"))!=ParseVersion(release.Version)) throw new IOException("Версия файла менеджера не совпала с выпуском.");
        foreach(var required in new[]{"PocketDeadlock.deps.json","PocketDeadlock.runtimeconfig.json","System.Private.CoreLib.dll"})
            if(!File.Exists(Path.Combine(app,required))) throw new IOException("Неполный пакет менеджера: "+required);
        progress.Report("Обновление менеджера проверено и готово. Закройте приложение для установки."); return app;
    }
    internal static void Extract(string zip,string destination,CancellationToken token)
    {
        string prefix=Path.GetFullPath(destination)+Path.DirectorySeparatorChar; long total=0; int count=0;
        using var archive=ZipFile.OpenRead(zip);
        foreach(var entry in archive.Entries)
        {
            token.ThrowIfCancellationRequested(); if(++count>3000) throw new IOException("Слишком много файлов в пакете.");
            string name=entry.FullName.Replace('\\','/');
            if(name.StartsWith('/') || name.Split('/').Any(x=>x is ".." or "." || x.Contains(':')) || ((entry.ExternalAttributes>>16)&0xf000)==0xa000) throw new IOException("Небезопасный путь в пакете обновления.");
            string target=Path.GetFullPath(Path.Combine(destination,name));
            if(!target.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)) throw new IOException("Путь выходит за пределы пакета.");
            if(name.EndsWith('/')) { Directory.CreateDirectory(target); continue; }
            total+=entry.Length; if(total>600L*1024*1024) throw new IOException("Слишком большой распакованный пакет.");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using var input=entry.Open(); using var output=new FileStream(target,FileMode.CreateNew,FileAccess.Write,FileShare.None);
            GameBanana.CopyLimited(input,output,600L*1024*1024-total+entry.Length,token).GetAwaiter().GetResult();
        }
    }
    internal static Version ReadVersion(string dll)
    {
        using var data=File.OpenRead(dll); using var pe=new PEReader(data); return pe.GetMetadataReader().GetAssemblyDefinition().Version;
    }
    public static string CreateJob(string stage,string target,int oldPid,AppRelease release,bool restart=true)
    {
        var directory=new DirectoryInfo(stage);
        while(directory!=null && !File.Exists(Path.Combine(directory.FullName,"package.zip"))) directory=directory.Parent;
        if(directory==null) throw new IOException("Не найден проверенный архив обновления.");
        string job=Path.Combine(directory.FullName,"job.json");
        ModStorage.AtomicWrite(job,JsonSerializer.SerializeToUtf8Bytes(new UpdateJob(Path.GetFullPath(stage),Path.GetFullPath(target),oldPid,release,restart))); return job;
    }
    public static void StartInstall(string job)
    {
        var data=JsonSerializer.Deserialize<UpdateJob>(File.ReadAllText(job))??throw new IOException("Неверное задание обновления.");
        var start=new ProcessStartInfo(Path.Combine(data.Stage,"PocketDeadlock.exe")){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden};
        start.ArgumentList.Add("--apply-update"); start.ArgumentList.Add(job); _=Process.Start(start)??throw new IOException("Не удалось запустить установку обновления.");
    }
    public static Task ApplyJob(string jobPath)=>ApplyJob(jobPath,UpdateKey.PublicPem);
    internal static async Task ApplyJob(string jobPath,string publicKey)
    {
        string result=Path.Combine(Path.GetDirectoryName(jobPath)!,"result.json");
        UpdateJob? job=null; var replaced=new List<(string Target,string? Backup)>();
        try
        {
            job=JsonSerializer.Deserialize<UpdateJob>(File.ReadAllText(jobPath))??throw new IOException("Неверное задание.");
            Verify(job.Release,publicKey);
            string stage=Path.TrimEndingDirectorySeparator(Path.GetFullPath(job.Stage)),target=Path.TrimEndingDirectorySeparator(Path.GetFullPath(job.Target));
            string stagePrefix=Path.EndsInDirectorySeparator(stage)?stage:stage+Path.DirectorySeparatorChar;
            string targetPrefix=Path.EndsInDirectorySeparator(target)?target:target+Path.DirectorySeparatorChar;
            if(target.StartsWith(stagePrefix,StringComparison.OrdinalIgnoreCase) || stage.StartsWith(targetPrefix,StringComparison.OrdinalIgnoreCase) || target.Equals(stage,StringComparison.OrdinalIgnoreCase)) throw new IOException("Папки пакета и установленной программы совпадают.");
            if(!File.Exists(Path.Combine(target,"PocketDeadlock.exe")) || !File.Exists(Path.Combine(target,"PocketDeadlock.dll"))) throw new IOException("Папка назначения не содержит PocketDeadlock.");
            ModStorage.RejectLinks(stage); ModStorage.RejectLinks(target);
            if(ReadVersion(Path.Combine(stage,"PocketDeadlock.dll"))!=ParseVersion(job.Release.Version)) throw new IOException("Неверная версия подготовленного пакета.");
            if(ReadVersion(Path.Combine(target,"PocketDeadlock.dll"))>=ParseVersion(job.Release.Version)) throw new IOException("В папке назначения уже установлена эта или более новая версия.");
            // Verify the original signed archive again, then re-extract to detect modified staging files.
            string package=Path.Combine(Path.GetDirectoryName(jobPath)!,"package.zip");
            if(new FileInfo(package).Length!=job.Release.Size || ModStorage.Hash(package)!=job.Release.Sha256.ToUpperInvariant()) throw new IOException("Подготовленный архив изменен.");
            string verified=Path.Combine(Path.GetDirectoryName(jobPath)!,"verified_"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(verified);
            Extract(package,verified,CancellationToken.None);
            string verifiedApp=Path.GetDirectoryName(Directory.GetFiles(verified,"PocketDeadlock.exe",SearchOption.AllDirectories).Single())!;
            if(ReadVersion(Path.Combine(verifiedApp,"PocketDeadlock.dll"))!=ParseVersion(job.Release.Version)) throw new IOException("Неверная версия выпуска.");
            if(job.OldPid>0)
            {
                try { using var process=Process.GetProcessById(job.OldPid); await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(45)); }
                catch(ArgumentException) { /* Process already exited. */ }
            }
            string backup=Path.Combine(Path.GetDirectoryName(jobPath)!,"backup"); Directory.CreateDirectory(backup);
            foreach(var file in Directory.GetFiles(verifiedApp,"*",SearchOption.AllDirectories))
            {
                string relative=Path.GetRelativePath(verifiedApp,file),destination=Path.GetFullPath(Path.Combine(target,relative));
                if(!destination.StartsWith(targetPrefix,StringComparison.OrdinalIgnoreCase)) throw new IOException("Неверный путь установки.");
                string? saved=null;
                if(File.Exists(destination)) { saved=Path.Combine(backup,relative); Directory.CreateDirectory(Path.GetDirectoryName(saved)!); File.Copy(destination,saved); }
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!); replaced.Add((destination,saved)); ModStorage.AtomicWrite(destination,File.ReadAllBytes(file));
            }
            ModStorage.AtomicWrite(result,JsonSerializer.SerializeToUtf8Bytes(new{Success=true,Version=job.Release.Version}));
            if(job.Restart) Process.Start(new ProcessStartInfo(Path.Combine(target,"PocketDeadlock.exe")){UseShellExecute=true});
        }
        catch(Exception ex)
        {
            foreach(var item in replaced.AsEnumerable().Reverse())
                try { if(item.Backup==null) File.Delete(item.Target); else ModStorage.AtomicWrite(item.Target,File.ReadAllBytes(item.Backup)); } catch { }
            ModStorage.AtomicWrite(result,JsonSerializer.SerializeToUtf8Bytes(new{Success=false,Error=ex.Message}));
            if(job?.Restart==true) try { Process.Start(new ProcessStartInfo(Path.Combine(job.Target,"PocketDeadlock.exe")){UseShellExecute=true}); } catch { }
            throw;
        }
    }
}
