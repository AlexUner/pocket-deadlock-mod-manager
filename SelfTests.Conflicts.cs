using System.IO;
using System.Text;

namespace PocketDeadlock;

internal static partial class SelfTests
{
    static void ConflictDocumentationTests(string run,Action<bool,string> assert,Action<Action,string> fails)
    {
        string folder=Path.Combine(run,"documentation-conflicts");Directory.CreateDirectory(folder);
        string a=Path.Combine(folder,"a.vpk"),b=Path.Combine(folder,"b.vpk"),overlap=Path.Combine(folder,"overlap.vpk");
        MakeResourceVpk(a,"README.txt","LICENSE","materials/hero_a.vmat_c");
        MakeResourceVpk(b,"readme.TXT","LICENSE","models/hero_b.vmdl_c");
        MakeResourceVpk(overlap,"README.txt","materials/hero_a.vmat_c");
        assert(Vpk.ReadResources(a).Contains("README.txt") && Vpk.ReadGameResources(a).SetEquals(["materials/hero_a.vmat_c"]),"root documentation remains in the VPK index but does not count as a game conflict");
        string text=Path.Combine(folder,"game-text.vpk");
        MakeResourceVpk(text,"readme.md","CHANGELOG.rst","NOTICE.txt","scripts/README.txt","resource/LICENSE.txt","README.cfg","settings.txt");
        assert(Vpk.ReadGameResources(text).SetEquals(["scripts/README.txt","resource/LICENSE.txt","README.cfg","settings.txt"]),"nested documentation names, executable configuration and other text remain game resources");
        string docsOnly=Path.Combine(folder,"docs-only.vpk");MakeResourceVpk(docsOnly,"README.txt","LICENSE");
        fails(()=>Vpk.ReadGameResources(docsOnly),"documentation-only VPK cannot be installed as a game mod");

        var store=new ModStorage(Path.Combine(folder,"data"));
        var prepared=new PreparedMod(folder,["a.vpk","b.vpk"]);
        var pair=store.Install(prepared,prepared.Files,"Two distinct heroes with shared documentation");
        assert(pair.Files.Count==2 && !pair.AllowOverrides,"multi-VPK import accepts shared documentation without granting asset overrides");
        int installed=store.State.Mods.Count;
        fails(()=>store.Install(new PreparedMod(folder,["a.vpk","overlap.vpk"]),["a.vpk","overlap.vpk"],"Conflicting variants"),"multi-VPK import still rejects overlapping game assets");
        assert(store.State.Mods.Count==installed,"failed conflicting import leaves the installed library intact");
        var first=store.Install(new PreparedMod(folder,["a.vpk"]),["a.vpk"],"Hero A");
        var second=store.Install(new PreparedMod(folder,["b.vpk"]),["b.vpk"],"Hero B");
        first.Enabled=true;second.Enabled=true;
        string gameRoot=Path.Combine(folder,"fake-game");
        Directory.CreateDirectory(Path.Combine(gameRoot,"game","bin","win64"));
        File.WriteAllText(Path.Combine(gameRoot,"game","bin","win64","deadlock.exe"),"fixture, never executed");
        Directory.CreateDirectory(Path.GetDirectoryName(GameInstall.ConfigPath(gameRoot))!);
        const string original="GameInfo\n{\nFileSystem\n{\nSearchPaths\n{\nGame citadel\n}\n}\n}\n";
        File.WriteAllText(GameInstall.ConfigPath(gameRoot),original);
        store.State.GamePath=gameRoot;
        var game=new GameInstall(store,true);game.Apply(CancellationToken.None);
        assert(game.IsApplied() && !first.AllowOverrides && !second.AllowOverrides,"distinct mods with shared root documentation apply without disabling conflict protection");
        var duplicate=store.Install(new PreparedMod(folder,["overlap.vpk"]),["overlap.vpk"],"Overlapping hero A");duplicate.Enabled=true;
        var before=File.ReadAllBytes(GameInstall.ConfigPath(gameRoot));
        fails(()=>game.Apply(CancellationToken.None),"shared documentation cannot hide a real model or material conflict");
        assert(before.SequenceEqual(File.ReadAllBytes(GameInstall.ConfigPath(gameRoot))),"a real conflict still preserves the game configuration");
        game.Disable();assert(File.ReadAllText(GameInstall.ConfigPath(gameRoot))==original,"documentation conflict handling preserves the apply-disable roundtrip");
    }

    static void MakeResourceVpk(string file,params string[] paths)
    {
        using var tree=new MemoryStream();
        using(var writer=new BinaryWriter(tree,Encoding.UTF8,true))
        {
            void S(string value) {writer.Write(Encoding.UTF8.GetBytes(value));writer.Write((byte)0);}
            foreach(var extension in paths.GroupBy(p=>Path.GetExtension(p)))
            {
                S(extension.Key==""?" ":extension.Key[1..]);
                foreach(var directory in extension.GroupBy(p=>Path.GetDirectoryName(p)?.Replace('\\','/')??""))
                {
                    S(directory.Key==""?" ":directory.Key);
                    foreach(string path in directory)
                    {
                        S(Path.GetFileNameWithoutExtension(path));writer.Write((uint)0);writer.Write((ushort)0);writer.Write((ushort)0x7fff);
                        writer.Write((uint)0);writer.Write((uint)4);writer.Write((ushort)0xffff);
                    }
                    S("");
                }
                S("");
            }
            S("");
        }
        using var output=new BinaryWriter(File.Create(file));
        output.Write((uint)0x55aa1234);output.Write((uint)2);output.Write((uint)tree.Length);
        output.Write((uint)4);output.Write((uint)0);output.Write((uint)0);output.Write((uint)0);
        output.Write(tree.ToArray());output.Write(new byte[]{1,2,3,4});
    }
}
