using System.IO;
using System.Net.Http;
using System.Windows.Media.Imaging;

namespace RaisinDocs;

public class ImageCache
{
    private record CacheEntry(BitmapImage Image, double PixelWidth, double PixelHeight);

    private readonly Dictionary<string, CacheEntry> _cache = new();
    private readonly Dictionary<string, Task<CacheEntry?>> _pending = new();
    private static readonly HttpClient _http = new();

    public (BitmapImage Image, double Width, double Height)? Get(string url, string? basePath, double maxWidth)
    {
        string key = ResolveKey(url, basePath);

        if (_cache.TryGetValue(key, out var entry))
            return Scale(entry, maxWidth);

        return null;
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
        _pending[key] = task;
        task.ContinueWith(t =>
        {
            if (dispatcher != null)
            {
                dispatcher.BeginInvoke(() =>
                {
                    _pending.Remove(key);
                    if (t.Result != null)
                    {
                        _cache[key] = t.Result;
                        onLoaded();
                    }
                });
            }
            else
            {
                _pending.Remove(key);
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
