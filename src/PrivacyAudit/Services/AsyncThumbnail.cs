using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using PrivacyAudit.Core;

namespace PrivacyAudit;

public sealed class AsyncThumbnail : System.Windows.Controls.Image
{
    static readonly SemaphoreSlim DecodeSlots = new(4);
    static readonly ConcurrentDictionary<string, WeakReference<BitmapSource>> Cache = new(StringComparer.OrdinalIgnoreCase);
    int _requestVersion;
    CancellationTokenSource? _loadCts;

    public AsyncThumbnail()
    {
        Loaded += (_, _) => QueueLoad();
        Unloaded += (_, _) => CancelLoad();
    }

    public static readonly DependencyProperty FilePathProperty = DependencyProperty.Register(
        nameof(FilePath), typeof(string), typeof(AsyncThumbnail), new PropertyMetadata(null, Changed));
    public static readonly DependencyProperty CategoryProperty = DependencyProperty.Register(
        nameof(Category), typeof(string), typeof(AsyncThumbnail), new PropertyMetadata(null, Changed));
    public static readonly DependencyProperty DecodeWidthProperty = DependencyProperty.Register(
        nameof(DecodeWidth), typeof(int), typeof(AsyncThumbnail), new PropertyMetadata(96, Changed));

    public string? FilePath { get => (string?)GetValue(FilePathProperty); set => SetValue(FilePathProperty, value); }
    public string? Category { get => (string?)GetValue(CategoryProperty); set => SetValue(CategoryProperty, value); }
    public int DecodeWidth { get => (int)GetValue(DecodeWidthProperty); set => SetValue(DecodeWidthProperty, value); }

    static void Changed(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((AsyncThumbnail)d).QueueLoad();

    public static async Task PreloadAsync(IEnumerable<Finding> findings, int width, CancellationToken token)
    {
        var paths = findings.Where(x => string.Equals(x.Category, "Images", StringComparison.OrdinalIgnoreCase))
            .Select(x => x.Path).Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var jobs = paths.Select(path => PreloadPathAsync(path, width, token)).ToArray();
        await Task.WhenAll(jobs);
    }

    static async Task PreloadPathAsync(string path, int width, CancellationToken token)
    {
        var key = $"{Math.Clamp(width, 32, 512)}|{path}";
        if (Cache.TryGetValue(key, out var weak) && weak.TryGetTarget(out _)) return;
        await DecodeSlots.WaitAsync(token);
        try
        {
            var bitmap = await Task.Run(() => Decode(path, width), token);
            if (bitmap is not null) Cache[key] = new(bitmap);
        }
        catch (OperationCanceledException) { throw; }
        catch { }
        finally { DecodeSlots.Release(); }
    }

    async void QueueLoad()
    {
        CancelLoad();
        _loadCts = new CancellationTokenSource();
        var token = _loadCts.Token;
        var version = ++_requestVersion;
        Source = null;
        var path = FilePath;
        var width = Math.Clamp(DecodeWidth, 32, 512);
        if (!string.Equals(Category, "Images", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
        var key = $"{width}|{path}";
        if (Cache.TryGetValue(key, out var weak) && weak.TryGetTarget(out var cached)) { Source = cached; return; }

        var entered = false;
        try
        {
            await DecodeSlots.WaitAsync(token);
            entered = true;
            var bitmap = await Task.Run(() => Decode(path, width), token);
            token.ThrowIfCancellationRequested();
            if (bitmap is null) return;
            Cache[key] = new(bitmap);
            if (version == _requestVersion && string.Equals(path, FilePath, StringComparison.OrdinalIgnoreCase)) Source = bitmap;
            if (Cache.Count > 2048) foreach (var item in Cache.Where(x => !x.Value.TryGetTarget(out _)).Take(512)) Cache.TryRemove(item.Key, out _);
        }
        catch (OperationCanceledException) { }
        finally { if (entered) DecodeSlots.Release(); }
    }

    void CancelLoad()
    {
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = null;
    }

    static BitmapSource? Decode(string path, int width)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(path);
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            image.DecodePixelWidth = width;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch { return null; }
    }
}
