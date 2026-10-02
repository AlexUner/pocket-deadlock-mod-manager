using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media.Imaging;

namespace PocketDeadlock;

// Shared by covers and the detail pane. Each caller owns its wait, not other callers' requests.
public sealed class ThumbnailCache
{
    sealed class Entry
    {
        public Task<BitmapSource?> Work=null!;
        public readonly CancellationTokenSource Lifetime=new(TimeSpan.FromSeconds(25));
        public int Waiters;
        public long Used,RetryAfter;
    }
    readonly string root;
    readonly Func<string,CancellationToken,Task<byte[]>> fetch;
    readonly SemaphoreSlim slots=new(4);
    readonly object gate=new();
    readonly Dictionary<string,Entry> entries=new(StringComparer.Ordinal);
    public ThumbnailCache(string root):this(root,Fetch) { }
    internal ThumbnailCache(string root,Func<string,CancellationToken,Task<byte[]>> fetch)
    {
        this.root=Path.GetFullPath(root);this.fetch=fetch;
        try {Directory.CreateDirectory(this.root);}catch(IOException) { }catch(UnauthorizedAccessException) { }
        _=Task.Run(TrimDisk);
    }
    internal string CachePath(string url)=>Path.Combine(root,Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url)))+".png");
    public async Task<BitmapSource?> Get(string url,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();if(!Catalogs.SafeImage(url)) return null;
        Entry entry;
        lock(gate)
        {
            if(entries.TryGetValue(url,out var old) && old.Work.IsCompletedSuccessfully && old.Work.Result==null && Environment.TickCount64>=old.RetryAfter) entries.Remove(url);
            if(!entries.TryGetValue(url,out entry!))
            {
                foreach(string key in entries.Where(x=>x.Value.Work.IsCompleted && x.Value.Waiters==0).OrderBy(x=>x.Value.Used).Take(Math.Max(0,entries.Count-127)).Select(x=>x.Key).ToList()) entries.Remove(key);
                entry=new Entry();entries[url]=entry;
                var created=entry;entry.Work=Task.Run(async()=>
                {
                    try {var image=await Load(url,created.Lifetime.Token);if(image==null) Interlocked.Exchange(ref created.RetryAfter,Environment.TickCount64+60_000);return image;}
                    finally {created.Lifetime.Dispose();}
                });
            }
            entry.Waiters++;entry.Used=Environment.TickCount64;
        }
        try {return await entry.Work.WaitAsync(token);}
        finally
        {
            bool cancel=false;
            lock(gate)
            {
                entry.Waiters--;
                if(entry.Waiters==0 && !entry.Work.IsCompleted)
                {
                    if(entries.GetValueOrDefault(url)==entry) entries.Remove(url);
                    cancel=true;
                }
            }
            if(cancel) try {entry.Lifetime.Cancel();}catch(ObjectDisposedException) { }
        }
    }
    async Task<BitmapSource?> Load(string url,CancellationToken token)
    {
        string path=CachePath(url);bool acquired=false;
        try
        {
            if(File.Exists(path))
            {
                try
                {
                    if(new FileInfo(path).Length>2*1024*1024) throw new IOException("Cached image too large");
                    return Decode(await File.ReadAllBytesAsync(path,token));
                }
                catch(OperationCanceledException) {throw;}
                catch {try {File.Delete(path);}catch { } }
            }
            await slots.WaitAsync(token);acquired=true;
            var bytes=await fetch(url,token);
            if(bytes.Length>8*1024*1024) throw new IOException("Image too large");
            var image=Decode(bytes);
            string temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            try
            {
                var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(image));
                using(var output=File.Create(temporary)) png.Save(output);
                File.Move(temporary,path,true);
            }
            catch(IOException) { }catch(UnauthorizedAccessException) { }
            finally {try {if(File.Exists(temporary)) File.Delete(temporary);}catch { } }
            _=Task.Run(TrimDisk);return image;
        }
        catch {return null;}
        finally {if(acquired) slots.Release();}
    }
    internal static BitmapSource Decode(byte[] bytes)
    {
        using var stream=new MemoryStream(bytes);
        var decoder=BitmapDecoder.Create(stream,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnDemand);
        var frame=decoder.Frames[0];int width=frame.PixelWidth,height=frame.PixelHeight;
        if(width<=0 || height<=0 || width>16384 || height>16384 || (long)width*height>64_000_000) throw new IOException("Unsupported image dimensions");
        stream.Position=0;var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;
        if(Math.Max(width,height)>640) {if(width>=height) image.DecodePixelWidth=640;else image.DecodePixelHeight=640;}
        image.StreamSource=stream;image.EndInit();image.Freeze();return image;
    }
    static async Task<byte[]> Fetch(string url,CancellationToken token)
    {
        using var response=await CommunityCatalog.GetImage(url,token);
        using var input=await response.Content.ReadAsStreamAsync(token);using var output=new MemoryStream();
        await GameBanana.CopyLimited(input,output,8*1024*1024,token);return output.ToArray();
    }
    void TrimDisk()
    {
        try
        {
            long kept=0;
            foreach(var file in new DirectoryInfo(root).EnumerateFiles("*.png").OrderByDescending(x=>x.LastWriteTimeUtc))
            {
                kept+=file.Length;if(kept>128L*1024*1024 || file.LastWriteTimeUtc<DateTime.UtcNow.AddDays(-30)) try {file.Delete();}catch { }
            }
        }
        catch { /* A cache failure must not disable browsing. */ }
    }
}
