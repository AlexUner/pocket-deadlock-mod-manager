using System.IO;
using System.Text.Json;
using System.IO.Compression;
using System.Security.Cryptography;

namespace PocketDeadlock;

public sealed class Profiles(ModStorage storage)
{
    public ModProfile Capture(string name)
    {
        if(string.IsNullOrWhiteSpace(name) || name.Length>100) throw new IOException("Введите название профиля от 1 до 100 символов.");
        var profile=Current(name);
        storage.State.Profiles.Add(profile); try {storage.Save();} catch {storage.State.Profiles.Remove(profile);throw;} return profile;
    }
    public ModProfile Current(string name)=>new(Guid.NewGuid().ToString("N"),name.Trim(),storage.State.Mods.Select(x=>new ProfileEntry(x.Id,x.RemoteId,x.Kind,x.FileId,x.VariantKey,x.Enabled,x.AllowOverrides,x.SelectedPaths.ToList())).ToList());
    public int Select(ModProfile profile)
    {
        var old=storage.State.Mods.ToList(); var settings=old.Select(x=>(x,x.Enabled,x.AllowOverrides)).ToList();
        var order=new List<LibraryMod>(); int missing=0;
        foreach(var entry in profile.Mods)
        {
            var mod=old.FirstOrDefault(x=>x.Id==entry.Id || entry.RemoteId>0 && x.RemoteId==entry.RemoteId && x.Kind==entry.Kind && (x.FileId==entry.FileId || x.VariantKey==entry.VariantKey && entry.VariantKey!=""));
            if(mod==null) {if(entry.Enabled) missing++;continue;}
            if(order.Contains(mod)) continue;
            mod.Enabled=entry.Enabled; mod.AllowOverrides=entry.AllowOverrides; order.Add(mod);
        }
        foreach(var mod in old.Where(x=>!order.Contains(x))) {mod.Enabled=false;order.Add(mod);}
        storage.State.Mods=order; bool pending=storage.State.PendingApply; storage.State.PendingApply=true;
        try {storage.Save();} catch {storage.State.Mods=old;storage.State.PendingApply=pending;foreach(var (mod,enabled,overrides) in settings) {mod.Enabled=enabled;mod.AllowOverrides=overrides;}throw;}
        return missing;
    }
    public static void Export(ModProfile profile,string file)=>ModStorage.AtomicWrite(file,JsonSerializer.SerializeToUtf8Bytes(profile,new JsonSerializerOptions {WriteIndented=true}));
    static void Validate(ModProfile profile)
    {
        if(string.IsNullOrWhiteSpace(profile.Name) || profile.Name.Length>100 || profile.Mods==null || profile.Mods.Count>1000 || profile.Mods.Any(x=>x.RemoteId<0 || x.FileId<0 || x.Kind is not("Mod" or "Sound" or "Local") || x.VariantKey==null || x.VariantKey.Length>300 || x.SelectedPaths?.Any(p=>p==null || p.Length>1024 || Path.IsPathRooted(p) || p.Split('/','\\').Any(part=>part is ".." or "." || part.Contains(':')))==true)) throw new IOException("Неверные данные профиля.");
    }
    public static string Key(ModProfile profile)
    {
        Validate(profile);
        if(profile.Mods.Any(x=>x.Enabled && x.RemoteId==0)) throw new IOException("В наборе есть локальные VPK без ссылки на каталог. Их нужно передать отдельно или отключить перед созданием ключа.");
        var portable=profile with {Id="",Mods=profile.Mods.Where(x=>x.Enabled && x.RemoteId>0).Select(x=>x with {Id=""}).ToList()};
        using var data=new MemoryStream();using(var zip=new GZipStream(data,CompressionLevel.SmallestSize,true)) zip.Write(JsonSerializer.SerializeToUtf8Bytes(portable));
        byte[] compressed=data.ToArray();string hash=Convert.ToHexString(SHA256.HashData(compressed))[..12];
        return "PD1-"+hash+"-"+Convert.ToBase64String(compressed).TrimEnd('=').Replace('+','-').Replace('/','_');
    }
    public ModProfile FromKey(string key)
    {
        key=key.Trim();if(key.Length>200000 || !key.StartsWith("PD1-")) throw new IOException("Неверный ключ набора PocketDeadlock.");
        var parts=key.Split('-',3);if(parts.Length!=3) throw new IOException("Неверный ключ набора PocketDeadlock.");
        byte[] bytes;
        try {string encoded=parts[2].Replace('-','+').Replace('_','/');bytes=Convert.FromBase64String(encoded.PadRight((encoded.Length+3)/4*4,'='));}catch(FormatException) {throw new IOException("Ключ набора поврежден.");}
        if(Convert.ToHexString(SHA256.HashData(bytes))[..12]!=parts[1]) throw new IOException("Ключ набора поврежден.");
        using var compressed=new MemoryStream(bytes);using var unzip=new GZipStream(compressed,CompressionMode.Decompress);using var data=new MemoryStream();
        GameBanana.CopyLimited(unzip,data,1024*1024,CancellationToken.None).GetAwaiter().GetResult();
        var profile=JsonSerializer.Deserialize<ModProfile>(data.ToArray())??throw new IOException("Неверный профиль.");Validate(profile);
        profile=profile with {Id=Guid.NewGuid().ToString("N")};storage.State.Profiles.Add(profile);
        try {storage.Save();}catch {storage.State.Profiles.Remove(profile);throw;}return profile;
    }
    public ModProfile Import(string file)
    {
        if(new FileInfo(file).Length>1024*1024) throw new IOException("Файл профиля превышает 1 МБ.");
        var profile=JsonSerializer.Deserialize<ModProfile>(File.ReadAllText(file))??throw new IOException("Неверный профиль.");
        Validate(profile);
        profile=profile with {Id=Guid.NewGuid().ToString("N")};storage.State.Profiles.Add(profile);
        try {storage.Save();} catch {storage.State.Profiles.Remove(profile);throw;} return profile;
    }
}
