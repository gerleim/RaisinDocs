using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Windows.Media.Imaging;

namespace RaisinDocs;

public class ImageCache
{
    private record CacheEntry(BitmapImage Image, double PixelWidth, double PixelHeight);

    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new();
    private readonly ConcurrentDictionary<string, Task<CacheEntry?>> _pending = new();
    private readonly ConcurrentDictionary<string, (double W, double H)> _sizes = new();
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };

    public (BitmapImage Image, double Width, double Height)? Get(string url, string? basePath, double maxWidth)
    {
        string key = ResolveKey(url, basePath);

        if (_cache.TryGetValue(key, out var entry))
            return Scale(entry, maxWidth);

        return null;
    }

    internal void TestInject(string url, string? basePath, double pixelWidth, double pixelHeight)
    {
        string key = ResolveKey(url, basePath);
        var wb = new System.Windows.Media.Imaging.WriteableBitmap(1, 1, 96, 96,
            System.Windows.Media.PixelFormats.Bgra32, null);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(wb));
        var ms = new MemoryStream();
        png.Save(ms);
        ms.Position = 0;
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.StreamSource = ms;
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.EndInit();
        bmp.Freeze();
        _cache[key] = new CacheEntry(bmp, pixelWidth, pixelHeight);
    }

    /// <summary>
    /// The scaled size an image will occupy, read from the file header without decoding it.
    /// Null when that cannot be known without fetching - an http url, or a missing file.
    /// </summary>
    /// <remarks>
    /// Layout needs a size the moment an image comes into view, and it used to reserve 20x20
    /// until the decode finished. That was wrong twice over: everything below the image jumped
    /// when the real size arrived, and the only way to correct it was InvalidateLayout, which
    /// reparses the whole document and drops the render caches - measured at up to 41ms on a
    /// long document, mid-scroll, which is felt as the scroll pausing near images.
    ///
    /// Reading the header costs a file open and a few hundred bytes, so the size is right from
    /// the first frame, nothing moves when the pixels arrive, and the decode that follows only
    /// needs a repaint.
    /// </remarks>
    public (double Width, double Height)? GetPixelSize(string url, string? basePath, double maxWidth)
    {
        string key = ResolveKey(url, basePath);

        if (_cache.TryGetValue(key, out var entry))
        {
            var scaled = Scale(entry, maxWidth);
            return (scaled.Width, scaled.Height);
        }

        if (_sizes.TryGetValue(key, out var known))
            return ScaleSize(known.W, known.H, maxWidth);

        // An http image cannot be measured without fetching it, so its size stays unknown and
        // the caller keeps the old placeholder-then-relayout behaviour.
        if (IsHttpUrl(url)) return null;

        try
        {
            string path = Path.IsPathRooted(url) ? url : Path.Combine(basePath ?? ".", url);
            path = Path.GetFullPath(path);
            if (!File.Exists(path)) return null;

            using var fs = File.OpenRead(path);
            var frame = BitmapFrame.Create(fs, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            double w = frame.PixelWidth, h = frame.PixelHeight;
            if (w <= 0 || h <= 0) return null;

            _sizes[key] = (w, h);
            return ScaleSize(w, h, maxWidth);
        }
        catch
        {
            return null;
        }
    }

    private static (double Width, double Height) ScaleSize(double w, double h, double maxWidth)
    {
        if (w > maxWidth && maxWidth > 0)
        {
            double ratio = maxWidth / w;
            w = maxWidth;
            h *= ratio;
        }
        return (w, h);
    }

    public void RequestLoad(string url, string? basePath, Action onLoaded)
    {
        string key = ResolveKey(url, basePath);

        if (_cache.ContainsKey(key))
            return;

        if (_pending.ContainsKey(key))
            return;

        var dispatcher = System.Windows.Threading.Dispatcher.FromThread(System.Threading.Thread.CurrentThread);
        var task = Task.Run(() => LoadEntry(key, url, basePath));
        _pending.TryAdd(key, task);
        task.ContinueWith(t =>
        {
            if (dispatcher != null)
            {
                dispatcher.BeginInvoke(() =>
                {
                    _pending.TryRemove(key, out _);
                    if (t.Result != null)
                    {
                        _cache[key] = t.Result;
                        onLoaded();
                    }
                });
            }
            else
            {
                _pending.TryRemove(key, out _);
                if (t.Result != null)
                    _cache[key] = t.Result;
            }
        });
    }

    private CacheEntry? LoadEntry(string key, string url, string? basePath)
    {
        try
        {
            if (IsHttpUrl(url))
                return LoadFromHttp(url);

            return LoadFromFile(url, basePath);
        }
        catch
        {
            return null;
        }
    }

    private CacheEntry? LoadFromFile(string url, string? basePath)
    {
        string path = Path.IsPathRooted(url) ? url : Path.Combine(basePath ?? ".", url);
        path = Path.GetFullPath(path);

        if (!File.Exists(path))
            return null;

        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.UriSource = new Uri(path, UriKind.Absolute);
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.EndInit();
        bitmap.Freeze();

        return new CacheEntry(bitmap, bitmap.PixelWidth, bitmap.PixelHeight);
    }

    private CacheEntry? LoadFromHttp(string url)
    {
        var data = _http.GetByteArrayAsync(url).GetAwaiter().GetResult();
        var stream = new MemoryStream(data);

        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.StreamSource = stream;
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.EndInit();
        bitmap.Freeze();

        return new CacheEntry(bitmap, bitmap.PixelWidth, bitmap.PixelHeight);
    }

    private static (BitmapImage Image, double Width, double Height) Scale(CacheEntry entry, double maxWidth)
    {
        double w = entry.PixelWidth;
        double h = entry.PixelHeight;

        if (w > maxWidth && maxWidth > 0)
        {
            double ratio = maxWidth / w;
            w = maxWidth;
            h *= ratio;
        }

        return (entry.Image, w, h);
    }

    private static string ResolveKey(string url, string? basePath)
    {
        if (IsHttpUrl(url))
            return url;

        string path = Path.IsPathRooted(url) ? url : Path.Combine(basePath ?? ".", url);
        return Path.GetFullPath(path);
    }

    private static bool IsHttpUrl(string url)
    {
        return url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
               url.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
    }
}
