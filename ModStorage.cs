using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using SharpCompress.Archives;

namespace PocketDeadlock;

public sealed class ModStorage
{
    public string Root { get; }
    public AppState State { get; private set; }
    static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    public ModStorage(string root)
    {
        Root=Path.GetFullPath(root); Directory.CreateDirectory(Root);
        string file=Path.Combine(Root,"state.json");
        State=File.Exists(file) ? JsonSerializer.Deserialize<AppState>(File.ReadAllText(file)) ?? throw new IOException("Повреждена библиотека state.json.") : new();
        foreach(var mod in State.Mods) { ValidateId(mod.Id); if(mod.ContentId!="") ValidateId(mod.ContentId); }
    }
    public void Save() => AtomicWrite(Path.Combine(Root,"state.json"),JsonSerializer.SerializeToUtf8Bytes(State,Options));
    public static void AtomicWrite(string path, byte[] bytes)
    {
        var tmp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try
        {
            using(var output=new FileStream(tmp,FileMode.CreateNew,FileAccess.Write,FileShare.None)) { output.Write(bytes); output.Flush(true); }
            File.Move(tmp,path,true);
        }
        finally { if(File.Exists(tmp)) File.Delete(tmp); }
    }
    public static string Hash(string file) { using var f=File.OpenRead(file); return Convert.ToHexString(SHA256.HashData(f)); }
    public string ModFolder(LibraryMod mod) { string id=mod.ContentId==""?mod.Id:mod.ContentId; ValidateId(id); return Path.Combine(Root,"mods",id); }
    public string NewStaging()
    {
        var p=Path.Combine(Root,"staging",Guid.NewGuid().ToString("N")); Directory.CreateDirectory(p); return p;
    }
    public void CleanupStaging(string folder)
    {
        var full=Path.GetFullPath(folder);
        var parent=Path.Combine(Root,"staging")+Path.DirectorySeparatorChar;
        if(!full.StartsWith(parent,StringComparison.OrdinalIgnoreCase)) throw new IOException("Неверная временная папка.");
        RejectLinks(full);
        if(Directory.Exists(full)) Directory.Delete(full,true);
    }
    public PreparedMod Prepare(string source, CancellationToken token)
    {
        var folder=NewStaging(); var files=new List<string>(); long total=0;
        const long maxTotal=4L*1024*1024*1024;
        try
        {
            if(new FileInfo(source).Length>2L*1024*1024*1024) throw new IOException("Лимит архива: 2 ГБ.");
            if(Path.GetExtension(source).Equals(".vpk",StringComparison.OrdinalIgnoreCase))
            {
                string name=Path.GetFileName(source); File.Copy(source,Path.Combine(folder,name)); files.Add(name);
            }
            else
            {
                if(!new[]{".zip",".rar",".7z"}.Contains(Path.GetExtension(source).ToLowerInvariant())) throw new IOException("Поддерживаются VPK, ZIP, RAR и 7Z.");
                using var archive=ArchiveFactory.OpenArchive(source);
                int entries=0;
                foreach(var entry in archive.Entries)
                {
                    token.ThrowIfCancellationRequested();
                    if(++entries>10000) throw new IOException("Слишком много файлов в архиве.");
                    if(entry.IsDirectory) continue;
                    string name=(entry.Key??"").Replace('\\','/');
                    if(name.StartsWith('/') || name.Split('/').Any(x=>x is ".." or "." || x.Contains(':'))) throw new IOException("В архиве найден небезопасный путь.");
                    if(!name.EndsWith(".vpk",StringComparison.OrdinalIgnoreCase)) continue;
                    if(entry.IsEncrypted || !string.IsNullOrEmpty(entry.LinkTarget)) throw new IOException("Шифрованные архивы и ссылки не поддерживаются.");
                    if(files.Count>=99) throw new IOException("Лимит одного набора: 99 VPK.");
                    string dest=Path.GetFullPath(Path.Combine(folder,name.Replace('/',Path.DirectorySeparatorChar)));
                    if(!dest.StartsWith(folder+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)) throw new IOException("Недопустимый путь архива.");
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    using var input=entry.OpenEntryStream();
                    using var output=new FileStream(dest,FileMode.CreateNew,FileAccess.Write,FileShare.None);
                    byte[] buffer=new byte[128*1024]; int count;
                    while((count=input.Read(buffer))>0)
                    {
                        token.ThrowIfCancellationRequested(); total+=count;
                        if(total>maxTotal) throw new IOException("Распакованные VPK превышают 4 ГБ.");
                        output.Write(buffer,0,count);
                    }
                    files.Add(name);
                }
            }
            if(files.Count==0) throw new IOException("В архиве нет VPK. Скрипты и программы не устанавливаются.");
            return new(folder,files);
        }
        catch { CleanupStaging(folder); throw; }
    }
    public LibraryMod Install(PreparedMod prepared,List<string> selected,string name,ModDetails? details=null,RemoteFile? remoteFile=null,bool register=true)
    {
        if(selected.Count==0) throw new IOException("Выберите хотя бы один VPK.");
        var mod=new LibraryMod { Name=name,Kind=details?.Item.Kind??"Local",RemoteId=details?.Item.Id??0,
            RemoteModified=details?.Item.Modified??0,FileId=remoteFile?.Id??0,SourceUrl=details?.Item.Url??"",Enabled=false,
            VariantName=remoteFile?.Name??"",VariantKey=remoteFile==null?"":ModUpdates.VariantKey(remoteFile.Name),RemoteMd5=remoteFile?.Md5??"",SelectedPaths=selected.ToList() };
        string destination=ModFolder(mod); Directory.CreateDirectory(destination);
        try
        {
            var resources=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for(int i=0;i<selected.Count;i++)
            {
                if(!prepared.Files.Contains(selected[i])) throw new IOException("Неизвестный файл.");
                var source=Path.Combine(prepared.Folder,selected[i]);
                var entries=Vpk.ReadResources(source);
                if(entries.Any(x=>!resources.Add(x))) throw new IOException("Выбраны VPK, заменяющие одинаковые ресурсы. Выберите один вариант.");
                string file=$"pak{i+1:00}_dir.vpk";
                File.Copy(source,Path.Combine(destination,file));
                mod.Files.Add(file); mod.Hashes.Add(Hash(Path.Combine(destination,file)));
            }
            if(register)
            {
                State.Mods.Add(mod);
                try { Save(); } catch { State.Mods.Remove(mod); throw; }
            }
            return mod;
        }
        catch { RejectLinks(destination); Directory.Delete(destination,true); throw; }
    }
    static ModRevision Snapshot(LibraryMod mod)=>new(mod.ContentId==""?mod.Id:mod.ContentId,mod.Name,mod.RemoteModified,mod.FileId,mod.VariantName,mod.VariantKey,mod.RemoteMd5,mod.Files.ToList(),mod.Hashes.ToList(),mod.SelectedPaths.ToList());
    public LibraryMod ReplaceVersion(LibraryMod old,LibraryMod fresh)
    {
        int index=State.Mods.FindIndex(x=>x.Id==old.Id); if(index<0) throw new IOException("Мод больше не находится в библиотеке.");
        fresh.ContentId=fresh.Id; fresh.Id=old.Id; fresh.Enabled=old.Enabled; fresh.AutoUpdate=old.AutoUpdate; fresh.AllowOverrides=old.AllowOverrides;
        fresh.Revisions=old.Revisions.ToList(); fresh.Revisions.Add(Snapshot(old)); fresh.UpdateStatus="Обновлен; предыдущая версия сохранена";
        State.Mods[index]=fresh; bool pending=State.PendingApply; State.PendingApply=true;
        try { Save(); } catch { State.Mods[index]=old; State.PendingApply=pending; throw; }
        return fresh;
    }
    public LibraryMod Rollback(LibraryMod old)
    {
        if(old.Revisions.Count==0) throw new IOException("Предыдущих версий нет.");
        var revision=old.Revisions[^1]; ValidateId(revision.Folder);
        var fresh=JsonSerializer.Deserialize<LibraryMod>(JsonSerializer.Serialize(old))!;
        fresh.ContentId=revision.Folder; fresh.Name=revision.Name; fresh.RemoteModified=revision.Modified; fresh.FileId=revision.FileId;
        fresh.VariantName=revision.VariantName; fresh.VariantKey=revision.VariantKey; fresh.RemoteMd5=revision.Md5; fresh.Files=revision.Files; fresh.Hashes=revision.Hashes; fresh.SelectedPaths=revision.SelectedPaths;
        if(fresh.Files.Count==0 || fresh.Files.Count!=fresh.Hashes.Count || fresh.Files.Any(x=>!Regex.IsMatch(x,@"^pak[0-9]{2}_dir\.vpk$"))) throw new IOException("Повреждена запись предыдущей версии.");
        for(int i=0;i<fresh.Files.Count;i++)
            if(Hash(Path.Combine(ModFolder(fresh),fresh.Files[i]))!=fresh.Hashes[i]) throw new IOException("Предыдущая версия повреждена.");
        fresh.Revisions=old.Revisions.Take(old.Revisions.Count-1).ToList(); fresh.Revisions.Add(Snapshot(old));
        fresh.AutoUpdate=false; fresh.AvailableUpdate=null; fresh.UpdateStatus="Выполнен откат; автообновление этого мода выключено";
        int index=State.Mods.FindIndex(x=>x.Id==old.Id); if(index<0) throw new IOException("Мод больше не находится в библиотеке.");
        State.Mods[index]=fresh; bool pending=State.PendingApply; State.PendingApply=true;
        try { Save(); } catch { State.Mods[index]=old; State.PendingApply=pending; throw; }
        return fresh;
    }
    internal static void ValidateId(string id) { if(!Regex.IsMatch(id,"^[a-f0-9]{32}$")) throw new IOException("Поврежден идентификатор мода."); }
    internal static void RejectLinks(string path)
    {
        for(var p=new DirectoryInfo(Path.GetFullPath(path));p!=null;p=p.Parent)
            if(p.Exists && (p.Attributes & FileAttributes.ReparsePoint)!=0) throw new IOException("Папки-ссылки не поддерживаются: "+p.FullName);
        if(Directory.Exists(path))
            foreach(var entry in Directory.EnumerateFileSystemEntries(path,"*",SearchOption.TopDirectoryOnly))
            {
                if((File.GetAttributes(entry)&FileAttributes.ReparsePoint)!=0) throw new IOException("В папке обнаружена ссылка.");
                if(Directory.Exists(entry)) RejectLinks(entry);
            }
    }
}

public static class Vpk
{
    public static HashSet<string> ReadResources(string path)
    {
        using var f=File.OpenRead(path); using var header=new BinaryReader(f);
        if(f.Length<12 || header.ReadUInt32()!=0x55AA1234) throw new IOException("Файл не является VPK: "+Path.GetFileName(path));
        uint version=header.ReadUInt32(); uint length=header.ReadUInt32();
        if(version is not (1 or 2) || length>32*1024*1024) throw new IOException("Неподдерживаемая структура VPK.");
        int offset=version==1?12:28;
        if(f.Length<offset+length) throw new IOException("Обрезанный VPK.");
        f.Position=offset; var tree=header.ReadBytes((int)length);
        using var data=new MemoryStream(tree); using var reader=new BinaryReader(data);
        var result=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string ReadString()
        {
            var bytes=new List<byte>(); byte b;
            while((b=reader.ReadByte())!=0) { bytes.Add(b); if(bytes.Count>4096) throw new IOException("Повреждено имя ресурса VPK."); }
            return Encoding.UTF8.GetString(bytes.ToArray());
        }
        try
        {
            string ext,dir,name;
            while((ext=ReadString())!="")
                while((dir=ReadString())!="")
                    while((name=ReadString())!="")
                    {
                        reader.ReadUInt32(); ushort preload=reader.ReadUInt16(),archive=reader.ReadUInt16();
                        uint start=reader.ReadUInt32(),size=reader.ReadUInt32();
                        if(reader.ReadUInt16()!=0xffff) throw new IOException("Поврежден индекс VPK.");
                        if(size>0 && archive!=0x7fff) throw new IOException("Составной VPK с внешними частями пока не поддерживается. Нужен единый VPK.");
                        if(size>0 && (long)offset+length+start+size>f.Length) throw new IOException("Отсутствуют данные ресурса VPK.");
                        if(data.Position+preload>data.Length) throw new IOException("Обрезанный индекс VPK.");
                        data.Position+=preload;
                        result.Add((dir==" "?"":dir+"/")+name+(ext==" "?"":"."+ext));
                        if(result.Count>500000) throw new IOException("Слишком большой индекс VPK.");
                    }
        }
        catch(EndOfStreamException) { throw new IOException("Поврежден индекс VPK."); }
        if(result.Count==0) throw new IOException("VPK не содержит ресурсов.");
        return result;
    }
}
