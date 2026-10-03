using System.IO;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text.Json;

namespace PocketDeadlock;

internal static partial class SelfTests
{
    static void OldAssembly(string path)
    {
        var metadata=new MetadataBuilder();
        metadata.AddModule(0,metadata.GetOrAddString("PocketDeadlock.dll"),metadata.GetOrAddGuid(Guid.NewGuid()),default,default);
        metadata.AddAssembly(metadata.GetOrAddString("PocketDeadlock"),new Version(0,1,0,0),default,default,0,0);
        var pe=new ManagedPEBuilder(PEHeaderBuilder.CreateLibraryHeader(),new MetadataRootBuilder(metadata),new BlobBuilder());
        var bytes=new BlobBuilder();pe.Serialize(bytes);File.WriteAllBytes(path,bytes.ToArray());
    }
    static string OldInstall(string run,string name)
    {
        string target=Path.Combine(run,name);Directory.CreateDirectory(target);
        OldAssembly(Path.Combine(target,"PocketDeadlock.dll"));
        File.WriteAllText(Path.Combine(target,"PocketDeadlock.exe"),"Old fixture - never executed");
        File.WriteAllText(Path.Combine(target,"System.Private.CoreLib.dll"),"Old fixture runtime");
        File.WriteAllText(Path.Combine(target,"owner-data.txt"),"Preserve local data");
        return target;
    }
    static async Task UpdateInstallTests(string run,AppUpdates app,AppRelease release,string publicKey,Action<bool,string> assert)
    {
        var token=CancellationToken.None;var progress=new Progress<string>();
        string target=OldInstall(run,"installed-fixture"),stage=await app.Stage(release,progress,token);
        string job=AppUpdates.CreateJob(stage+Path.DirectorySeparatorChar,target+Path.DirectorySeparatorChar,0,release,false);
        await AppUpdates.ApplyJob(job,publicKey);
        using(var result=JsonDocument.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(job)!,"result.json"))))
            assert(result.RootElement.GetProperty("Success").GetBoolean() && ModStorage.Hash(Path.Combine(target,"PocketDeadlock.dll"))==ModStorage.Hash(Path.Combine(stage,"PocketDeadlock.dll")),"real update replacement accepts trailing separators from AppContext.BaseDirectory");
        assert(File.ReadAllText(Path.Combine(target,"owner-data.txt"))=="Preserve local data" && File.ReadAllText(Path.Combine(Path.GetDirectoryName(job)!,"backup","PocketDeadlock.exe"))=="Old fixture - never executed","update backs up old application files and preserves unrelated local data");
        string lockedTarget=OldInstall(run,"locked-installed-fixture");
        var original=Directory.GetFiles(lockedTarget).ToDictionary(x=>Path.GetFileName(x)!,x=>File.ReadAllBytes(x));
        stage=await app.Stage(release,progress,token);job=AppUpdates.CreateJob(stage,lockedTarget+Path.DirectorySeparatorChar,0,release,false);
        bool failed=false;
        using(var locked=new FileStream(Path.Combine(lockedTarget,"System.Private.CoreLib.dll"),FileMode.Open,FileAccess.Read,FileShare.None))
            try {await AppUpdates.ApplyJob(job,publicKey);}catch(IOException){failed=true;}
        assert(failed && Directory.GetFiles(lockedTarget).Length==original.Count && original.All(x=>File.ReadAllBytes(Path.Combine(lockedTarget,x.Key)).SequenceEqual(x.Value)),"failed update rolls replaced files back when a destination runtime file is locked");
    }
}
