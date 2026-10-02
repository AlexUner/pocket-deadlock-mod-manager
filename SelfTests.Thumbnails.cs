using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PocketDeadlock;

internal static partial class SelfTests
{
    static byte[] TestImage(int width,int height)
    {
        var pixels=new byte[width*height*4];for(int i=0;i<pixels.Length;i+=4) {pixels[i]=80;pixels[i+1]=140;pixels[i+2]=180;pixels[i+3]=255;}
        var bitmap=BitmapSource.Create(width,height,96,96,PixelFormats.Bgra32,null,pixels,width*4);bitmap.Freeze();
        var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using var stream=new MemoryStream();png.Save(stream);return stream.ToArray();
    }
    static async Task ThumbnailTests(string run,Action<bool,string> assert)
    {
        byte[] png=TestImage(1024,512);string url="https://gamebanana.com/images/fixture.png",root=Path.Combine(run,"thumbnail-cache");
        int fetched=0;var started=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cache=new ThumbnailCache(root,async (_,token)=> {Interlocked.Increment(ref fetched);started.TrySetResult();await release.Task.WaitAsync(token);return png;});
        using var cancellation=new CancellationTokenSource();
        var canceled=cache.Get(url,cancellation.Token);var survivor=cache.Get(url,CancellationToken.None);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(3));cancellation.Cancel();bool didCancel=false;
        try {await canceled;}catch(OperationCanceledException) {didCancel=true;}
        release.SetResult();var image=await survivor;
        assert(didCancel && image!=null && fetched==1,"canceling one thumbnail caller preserves a shared visible request");
        assert(image!.IsFrozen && image.PixelWidth==640 && image.PixelHeight==320,"cover decoding limits bitmap size and freezes cross-thread images");
        assert(ReferenceEquals(image,await cache.Get(url,CancellationToken.None)) && fetched==1,"repeated thumbnail and detail requests share memory cache");
        assert(File.Exists(cache.CachePath(url)) && new FileInfo(cache.CachePath(url)).Length>0,"decoded covers persist as PNG in the data cache");
        int diskFetch=0;var restored=new ThumbnailCache(root,(_,_)=> {diskFetch++;throw new IOException("Network offline");});
        assert(await restored.Get(url,CancellationToken.None)!=null && diskFetch==0,"cached covers survive restart without network access");
        await File.WriteAllBytesAsync(cache.CachePath(url),[1,2,3]);int repaired=0;
        var repair=new ThumbnailCache(root,(_,_)=> {repaired++;return Task.FromResult(png);});
        assert(await repair.Get(url,CancellationToken.None)!=null && repaired==1,"corrupt cached image is fetched again without breaking browsing");
        assert(await repair.Get("file:///C:/private.png",CancellationToken.None)==null && await repair.Get("https://example.com/cover.png",CancellationToken.None)==null && repaired==1,"untrusted or local image addresses cannot reach the loader");
        int failures=0;var failed=new ThumbnailCache(Path.Combine(run,"failed-covers"),(_,_)=> {failures++;throw new IOException("Offline");});
        assert(await failed.Get(url,CancellationToken.None)==null && await failed.Get(url,CancellationToken.None)==null && failures==1,"failed covers use a fallback and avoid repeated immediate requests");
        var invalid=new ThumbnailCache(Path.Combine(run,"invalid-covers"),(_,_)=>Task.FromResult(new byte[]{1,2,3}));
        assert(await invalid.Get(url,CancellationToken.None)==null && !File.Exists(invalid.CachePath(url)),"non-image responses are not retained as valid cover files");
        var abandonedStarted=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var abandonedStopped=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var abandoned=new ThumbnailCache(Path.Combine(run,"abandoned-covers"),async (_,token)=> {abandonedStarted.SetResult();try {await Task.Delay(Timeout.Infinite,token);return png;}finally {abandonedStopped.SetResult();}});
        using var leaving=new CancellationTokenSource();var abandonedWait=abandoned.Get(url,leaving.Token);await abandonedStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));leaving.Cancel();
        try {await abandonedWait;}catch(OperationCanceledException) { }
        await abandonedStopped.Task.WaitAsync(TimeSpan.FromSeconds(3));assert(true,"leaving the last cover consumer cancels its network operation");
        int active=0,maximum=0;var parallel=new ThumbnailCache(Path.Combine(run,"parallel-covers"),async (_,token)=>
        {
            int current=Interlocked.Increment(ref active);int previous;
            do {previous=maximum;if(previous>=current) break;}while(Interlocked.CompareExchange(ref maximum,current,previous)!=previous);
            try {await Task.Delay(60,token);return png;}finally {Interlocked.Decrement(ref active);}
        });
        var images=await Task.WhenAll(Enumerable.Range(1,12).Select(i=>parallel.Get("https://gamebanana.com/images/"+i+".png",CancellationToken.None)));
        assert(maximum==4 && images.All(x=>x!=null),"cover downloads have a four-request concurrency limit");
        var narrow=CatalogTilePanel.Layout(610);var wide=CatalogTilePanel.Layout(890);
        assert(narrow.Columns==2 && wide.Columns==3 && narrow.TileWidth*2+12<=610 && wide.TileWidth*3+24<=890,"tile columns reflow without horizontal overflow");
        assert(CatalogTilePanel.Layout(100).Columns==1 && CatalogTilePanel.Layout(double.PositiveInfinity).TileWidth>0,"tile layout handles narrow and unbounded measuring constraints");
    }
}
