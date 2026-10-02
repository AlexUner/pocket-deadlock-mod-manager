using System.IO;
using System.Text.RegularExpressions;

namespace PocketDeadlock;

public record UpdateSummary(int Updated,int Available,int Manual,int Errors);
public sealed class ModUpdates(ModStorage storage,IModCatalog? provider=null)
{
    readonly IModCatalog origin=provider??new GameBanana();
    // GameBanana appends a content suffix; retain words such as with/without-flash and version names.
    public static string VariantKey(string name)=>Regex.Replace(Path.GetFileNameWithoutExtension(name).ToLowerInvariant(),@"_[a-f0-9]{4,32}$","");
    public static RemoteFile? MatchVariant(LibraryMod mod,List<RemoteFile> files)
    {
        var same=files.FirstOrDefault(x=>x.Id==mod.FileId);
        if(same!=null && (mod.VariantKey=="" || VariantKey(same.Name)==mod.VariantKey)) return same;
        if(mod.VariantKey=="") return null;
        var matches=files.Where(x=>VariantKey(x.Name)==mod.VariantKey).ToList();
        return matches.Count==1?matches[0]:null;
    }
    public async Task<UpdateSummary> Check(bool download,IProgress<string> progress,CancellationToken token)
    {
        int updated=0,available=0,manual=0,errors=0;
        var metadata=new Dictionary<string,ModDetails>();
        foreach(var original in storage.State.Mods.Where(x=>x.RemoteId>0).ToList())
        {
            token.ThrowIfCancellationRequested(); progress.Report("Проверяем: "+original.Name);
            try
            {
                string key=original.Kind+":"+original.RemoteId;
                if(!metadata.TryGetValue(key,out var info)) { info=await origin.Details(original.Kind,original.RemoteId,token); metadata[key]=info; }
                var file=MatchVariant(original,info.Files);
                if(file==null)
                {
                    original.AvailableUpdate=null; original.UpdateStatus="Нужно выбрать вариант вручную"; manual++; continue;
                }
                bool changed=file.Id!=original.FileId || original.RemoteMd5!="" && file.Md5!="" && !file.Md5.Equals(original.RemoteMd5,StringComparison.OrdinalIgnoreCase);
                if(original.RemoteMd5=="" && original.RemoteModified<info.Item.Modified) changed=true;
                if(original.VariantKey=="") { original.VariantName=file.Name; original.VariantKey=VariantKey(file.Name); }
                if(!changed)
                {
                    original.RemoteMd5=file.Md5; original.RemoteModified=info.Item.Modified; original.AvailableUpdate=null; original.UpdateStatus="Последняя версия"; continue;
                }
                original.AvailableUpdate=file; original.UpdateStatus="Доступно обновление"; available++;
                if(download && original.AutoUpdate)
                {
                    if(await Install(original,info,file,progress,token)) { updated++; available--; }
                    else { original.UpdateStatus="Обновление требует выбора VPK вручную"; manual++; }
                }
            }
            catch(OperationCanceledException) { throw; }
            catch(Exception ex) { original.UpdateStatus="Ошибка: "+ex.Message; errors++; }
        }
        storage.State.LastUpdateCheck=DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm"); storage.Save();
        return new(updated,available,manual,errors);
    }
    public async Task<bool> Install(LibraryMod mod,ModDetails details,RemoteFile file,IProgress<string> progress,CancellationToken token)
    {
        if(file.Blocked) throw new IOException("Файл обновления помечен источником как опасный.");
        string temp=storage.NewStaging(); PreparedMod? prepared=null;
        try
        {
            string ext=Path.GetExtension(file.Name).ToLowerInvariant();
            if(!new[]{".zip",".rar",".7z",".vpk"}.Contains(ext)) throw new IOException("Обновление не является VPK или архивом.");
            string archive=Path.Combine(temp,"update"+ext); await origin.Download(file,archive,progress,token);
            prepared=await Task.Run(()=>storage.Prepare(archive,token),token);
            List<string> chosen;
            if(mod.SelectedPaths.Count>0 && mod.SelectedPaths.All(prepared.Files.Contains)) chosen=mod.SelectedPaths.ToList();
            else if(mod.Files.Count==1 && prepared.Files.Count==1) chosen=prepared.Files.ToList();
            else return false;
            token.ThrowIfCancellationRequested();
            var fresh=await Task.Run(()=>storage.Install(prepared,chosen,details.Item.Name,details,file,false),token);
            storage.ReplaceVersion(mod,fresh); return true;
        }
        finally { if(prepared!=null) storage.CleanupStaging(prepared.Folder); storage.CleanupStaging(temp); }
    }
}
