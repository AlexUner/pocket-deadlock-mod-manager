using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace PocketDeadlock;

public sealed class GameInstall
{
    readonly ModStorage storage;
    readonly bool isolatedTest;
    public GameInstall(ModStorage storage) { this.storage=storage; }
    internal GameInstall(ModStorage storage,bool isolatedTest) { this.storage=storage; this.isolatedTest=isolatedTest; }
    void CheckClosed() { if(!isolatedTest) EnsureClosed(); }
    const string Start="// PocketDeadlock BEGIN";
    const string End="// PocketDeadlock END";
    public static string ConfigPath(string root)=>Path.Combine(root,"game","citadel","gameinfo.gi");
    public static bool Valid(string root)=>File.Exists(ConfigPath(root)) && File.Exists(Path.Combine(root,"game","bin","win64","deadlock.exe"));
    public static string Detect()
    {
        var paths=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var steam=Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam","SteamPath",null) as string;
        if(steam!=null) paths.Add(steam);
        paths.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),"Steam"));
        foreach(var basePath in paths.ToList())
        {
            var libraries=Path.Combine(basePath,"steamapps","libraryfolders.vdf");
            if(!File.Exists(libraries)) continue;
            foreach(Match match in Regex.Matches(File.ReadAllText(libraries),"\"path\"\\s+\"([^\"]+)\"")) paths.Add(match.Groups[1].Value.Replace("\\\\","\\"));
        }
        foreach(var p in paths) { var root=Path.Combine(p,"steamapps","common","Deadlock"); if(Valid(root)) return Path.GetFullPath(root); }
        return "";
    }
    public static void EnsureClosed()
    {
        if(IsRunning()) throw new IOException("Закройте Deadlock перед изменением модов.");
    }
    public static bool IsRunning()
    {
        foreach(var name in new[]{"deadlock","citadel"})
        {
            var processes=Process.GetProcessesByName(name); bool running=processes.Length>0;
            foreach(var process in processes) process.Dispose();
            if(running) return true;
        }
        return false;
    }
    public static string Patch(string original,IEnumerable<string> paths)
    {
        string clean=RemovePatch(original); var all=paths.ToList(); if(all.Count==0) return clean;
        var matches=Regex.Matches(clean,@"(?m)^\s*""?SearchPaths""?\s*\r?\n\s*\{");
        if(matches.Count!=1) throw new IOException("Не удалось однозначно найти SearchPaths. Файл игры не изменен.");
        var match=matches[0]; int index=match.Index+match.Length;
        string nl=clean.Contains("\r\n")?"\r\n":"\n";
        foreach(var path in all) if(!Regex.IsMatch(path,@"^citadel/pocket_mods/[a-f0-9]{32}/[a-f0-9]{32}$")) throw new IOException("Недопустимый путь подключения.");
        // Explicit Mod/Write preserve the game's writable root when a new Game path comes first.
        string block=nl+"\t\t\t"+Start+nl+"\t\t\tMod citadel"+nl+"\t\t\tWrite citadel"+nl
            +string.Join(nl,all.Select(p=>"\t\t\tGame "+p))+nl+"\t\t\t"+End;
        return clean.Insert(index,block);
    }
    public static string RemovePatch(string original)
    {
        if(!original.Contains(Start) && !original.Contains(End)) return original;
        if(Regex.Matches(original,Regex.Escape(Start)).Count!=1 || Regex.Matches(original,Regex.Escape(End)).Count!=1) throw new IOException("Поврежден блок PocketDeadlock. Восстановите gameinfo.gi из резервной копии.");
        var regex=new Regex(@"\r?\n[\t ]*"+Regex.Escape(Start)+@"\r?\n[\s\S]*?"+Regex.Escape(End));
        if(!regex.IsMatch(original)) throw new IOException("Не удалось удалить блок PocketDeadlock.");
        return regex.Replace(original,"",1);
    }
    public int LegacyCount()
    {
        var p=Path.Combine(storage.State.GamePath,"game","citadel","addons");
        return Directory.Exists(p)?Directory.GetFiles(p,"*.vpk").Length:0;
    }
    public bool IsApplied()=>File.Exists(ConfigPath(storage.State.GamePath)) && File.ReadAllText(ConfigPath(storage.State.GamePath)).Contains(Start);
    public void Apply(CancellationToken token)
    {
        CheckClosed(); var root=storage.State.GamePath;
        if(!Valid(root)) throw new IOException("Выберите папку Deadlock с установленной игрой.");
        var config=ConfigPath(root); var original=File.ReadAllBytes(config);
        var text=Decode(original); Patch(text,[]); // Fail before creating anything if the old block is malformed.
        var chosen=storage.State.Mods.Where(x=>x.Enabled).ToList();
        var resources=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach(var mod in chosen.AsEnumerable().Reverse())
        {
            token.ThrowIfCancellationRequested();
            if(mod.Files.Count!=mod.Hashes.Count || mod.Files.Count>99) throw new IOException("Повреждена запись мода: "+mod.Name);
            for(int i=0;i<mod.Files.Count;i++)
            {
                if(!Regex.IsMatch(mod.Files[i],@"^pak[0-9]{2}_dir\.vpk$")) throw new IOException("Недопустимое имя VPK.");
                var file=Path.Combine(storage.ModFolder(mod),mod.Files[i]);
                if(ModStorage.Hash(file)!=mod.Hashes[i]) throw new IOException("VPK был изменен после установки: "+mod.Name);
                foreach(var resource in Vpk.ReadGameResources(file))
                {
                    if(resources.TryGetValue(resource,out var other) && !mod.AllowOverrides) throw new IOException($"Конфликт: «{other}» и «{mod.Name}» заменяют {resource}. Поставьте нужный мод выше и разрешите ему перекрывать ресурсы, либо отключите один мод.");
                    resources[resource]=mod.Name;
                }
            }
        }
        var baseDir=Path.Combine(root,"game","citadel","pocket_mods");
        ModStorage.RejectLinks(baseDir);
        var mounts=new List<string>();
        // Immutable per-mod copies can be reused across sets. Repeated Apply does not duplicate gigabytes.
        // The one atomic config replacement activates the complete set.
        foreach(var mod in chosen)
        {
            string generation=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(mod.Id+string.Join("",mod.Hashes)))).ToLowerInvariant()[..32];
            var dir=Path.Combine(baseDir,generation);
            var destination=Path.Combine(dir,mod.Id);
            if(Directory.Exists(destination))
            {
                if(Directory.GetFiles(destination).Length!=mod.Files.Count || mod.Files.Where((file,i)=>!File.Exists(Path.Combine(destination,file)) || ModStorage.Hash(Path.Combine(destination,file))!=mod.Hashes[i]).Any())
                    throw new IOException("Копия мода в игре изменена: "+mod.Name+". Удалите только его папку из pocket_mods после отключения наших модов.");
            }
            else
            {
                var staging=Path.Combine(dir,"stage_"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(staging);
                try
                {
                    foreach(var file in mod.Files)
                    {
                        token.ThrowIfCancellationRequested(); File.Copy(Path.Combine(storage.ModFolder(mod),file),Path.Combine(staging,file));
                    }
                    Directory.Move(staging,destination);
                }
                finally { if(Directory.Exists(staging)) { ModStorage.RejectLinks(staging); Directory.Delete(staging,true); } }
            }
            mounts.Add($"citadel/pocket_mods/{generation}/{mod.Id}");
        }
        token.ThrowIfCancellationRequested(); CheckClosed();
        ReplaceConfig(config,original,Encode(Patch(text,mounts),original));
        storage.State.PendingApply=false; storage.Save();
    }
    public void Disable()
    {
        CheckClosed(); var config=ConfigPath(storage.State.GamePath);
        if(!Valid(storage.State.GamePath)) throw new IOException("Папка игры не найдена.");
        var bytes=File.ReadAllBytes(config); ReplaceConfig(config,bytes,Encode(RemovePatch(Decode(bytes)),bytes));
    }
    void ReplaceConfig(string config,byte[] original,byte[] modified)
    {
        if(original.SequenceEqual(modified)) return;
        if((File.GetAttributes(config)&FileAttributes.ReparsePoint)!=0) throw new IOException("gameinfo.gi является ссылкой.");
        // Do not recursively inspect the entire game; only check parents of this file.
        for(var p=new DirectoryInfo(Path.GetDirectoryName(config)!);p!=null;p=p.Parent)
            if((p.Attributes&FileAttributes.ReparsePoint)!=0) throw new IOException("Папка игры является ссылкой.");
        string backup=Path.Combine(storage.Root,"backups"); Directory.CreateDirectory(backup);
        string name=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(original))+".gi";
        if(!File.Exists(Path.Combine(backup,name))) ModStorage.AtomicWrite(Path.Combine(backup,name),original);
        if(!File.ReadAllBytes(config).SequenceEqual(original)) throw new IOException("Файл игры изменился во время операции. Повторите применение.");
        ModStorage.AtomicWrite(config,modified);
    }
    internal static string Decode(byte[] value) => new UTF8Encoding(false,true).GetString(value.AsSpan(value.AsSpan().StartsWith(new byte[]{239,187,191})?3:0));
    internal static byte[] Encode(string value,byte[] original)
    {
        var bytes=Encoding.UTF8.GetBytes(value);
        return original.AsSpan().StartsWith(new byte[]{239,187,191}) ? new byte[]{239,187,191}.Concat(bytes).ToArray() : bytes;
    }
}
