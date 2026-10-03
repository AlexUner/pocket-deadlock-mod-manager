using System.IO;
using System.Text.Json;

namespace PocketDeadlock;

internal static partial class SelfTests
{
    static async Task PopularityTests(string run,Action<bool,string> assert)
    {
        var sparse=new CatalogItem(12,"Mod","Fixture","Author","UI","","",1);
        assert(sparse.DownloadsDisplay=="↓ —" && sparse.LikesDisplay=="★ —","missing popularity is not reported as zero");
        var zero=sparse with {DownloadsKnown=true,LikesKnown=true};
        assert(zero.DownloadsDisplay=="↓ 0" && zero.LikesDisplay=="★ 0","known zero counts remain visible");
        using var json=JsonDocument.Parse("{\"_idRow\":12,\"_sName\":\"Fixture\",\"_nLikeCount\":0}");
        var parsed=GameBanana.Item(json.RootElement,"Mod");
        assert(!parsed.HasDownloads && parsed.HasLikes && parsed.Likes==0,"GameBanana list parser distinguishes omitted downloads from true zero likes");
        var fresh=sparse with {Category="RatKing",Downloads=1234,Likes=27,DownloadsKnown=true,LikesKnown=true};
        string folder=Path.Combine(run,"popularity-index");
        var origin=new FixtureCatalog(new ModDetails(fresh,"","",[]),"");
        var hybrid=new HybridCatalog("Test",origin,folder);
        hybrid.Merge([sparse]);int changes=0;hybrid.Changed+=()=>changes++;
        await hybrid.Details(sparse,CancellationToken.None);
        assert(hybrid.Snapshot().Single().Downloads==1234 && changes==1,"opening details updates popularity in the cached feed and emits a refresh");
        assert(hybrid.Snapshot().Single().Category=="UI","profile leaf category cannot replace indexed mod type when caching popularity");
        hybrid.Merge([parsed]);
        assert(hybrid.Snapshot().Single().Downloads==1234 && hybrid.Snapshot().Single().HasDownloads,"sparse list refresh preserves previously fetched download count");
        hybrid.Merge([sparse]);
        var restored=new HybridCatalog("Test",origin,folder).Snapshot().Single();
        assert(restored.Downloads==1234 && restored.Likes==0 && restored.HasLikes,"disk cache preserves detail counts and accepts a source's authoritative zero");
        hybrid.Merge([zero]);
        assert(hybrid.Snapshot().Single().Downloads==0 && hybrid.Snapshot().Single().HasDownloads,"known zero can replace an older positive download count");
        var merged=UnifiedCatalog.Merge([sparse,zero with {Provider="DeadlockMods"}]).Single().Item;
        assert(merged.HasDownloads && merged.HasLikes && merged.Downloads==0,"unified aliases preserve availability of true zero counts");
        string previous=L.Language;
        try
        {
            L.Set("en");
            assert(sparse.DownloadsHelp.StartsWith("Download count unavailable.") && sparse.LikesHelp.StartsWith("Like count unavailable."),"unknown popularity tooltips are translated to English");
        }
        finally {L.Set(previous);}
    }
}
