using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using System.Windows.Threading;
using PrivacyAudit.Core;
using PrivacyAudit.PeopleDetection;
using SixLabors.ImageSharp.PixelFormats;

namespace PrivacyAudit;

public sealed class AsyncThumbnail : System.Windows.Controls.Image
{
    static readonly SemaphoreSlim ImageDecodeSlots = new(4);
    // Video decode is intentionally serialized: opening many Source Readers at once makes the
    // gallery contend for disk, codecs and CPU and can starve the UI thread.
    static readonly SemaphoreSlim VideoDecodeSlots = new(1);
    static readonly ConcurrentDictionary<string, WeakReference<BitmapSource>> Cache = new(StringComparer.OrdinalIgnoreCase);
    static readonly ConcurrentDictionary<string, byte> FailedPreviews = new(StringComparer.OrdinalIgnoreCase);
    int _requestVersion;
    CancellationTokenSource? _loadCts;
    DispatcherOperation? _queuedLoad;

    public AsyncThumbnail()
    {
        Loaded += (_, _) => ScheduleLoad();
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

    static void Changed(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var thumbnail = (AsyncThumbnail)d;
        if (thumbnail.IsLoaded) thumbnail.ScheduleLoad();
    }

    void ScheduleLoad()
    {
        _queuedLoad?.Abort();
        _queuedLoad = Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(QueueLoad));
    }

    public static async Task PreloadAsync(IEnumerable<Finding> findings, int width, CancellationToken token)
    {
        var paths = findings.Where(x => string.Equals(x.Category, "Images", StringComparison.OrdinalIgnoreCase) || string.Equals(x.Category, "Video", StringComparison.OrdinalIgnoreCase))
            .Select(x => (x.Path, x.Category)).Where(x => File.Exists(x.Path)).DistinctBy(x => x.Path, StringComparer.OrdinalIgnoreCase).ToArray();
        var jobs = paths.Select(item => PreloadPathAsync(item.Path, width, item.Category, token)).ToArray();
        await Task.WhenAll(jobs);
    }

    static async Task PreloadPathAsync(string path, int width, string category, CancellationToken token)
    {
        var normalizedWidth = Math.Clamp(width, 32, 512);
        var key = CacheKey(path, normalizedWidth, category);
        if (FailedPreviews.ContainsKey(key) || Cache.TryGetValue(key, out var weak) && weak.TryGetTarget(out _)) return;
        var slots = SlotsFor(category);
        await slots.WaitAsync(token);
        try
        {
            var bitmap = await DecodeAsync(path, normalizedWidth, category, token);
            if (bitmap is not null) Cache[key] = new(bitmap); else FailedPreviews.TryAdd(key, 0);
        }
        catch (OperationCanceledException) { throw; }
        catch { }
        finally { slots.Release(); }
    }

    async void QueueLoad()
    {
        CancelLoad();
        _loadCts = new CancellationTokenSource();
        var token = _loadCts.Token;
        var version = ++_requestVersion;
        Source = null;
        var path = FilePath;
        var category = Category;
        var width = Math.Clamp(DecodeWidth, 32, 512);
        if ((!string.Equals(category, "Images", StringComparison.OrdinalIgnoreCase) && !string.Equals(category, "Video", StringComparison.OrdinalIgnoreCase)) || string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
        var key = CacheKey(path, width, category);
        if (FailedPreviews.ContainsKey(key)) return;
        if (Cache.TryGetValue(key, out var weak) && weak.TryGetTarget(out var cached)) { Source = cached; return; }

        var entered = false;
        var slots = SlotsFor(category);
        try
        {
            await slots.WaitAsync(token);
            entered = true;
            var bitmap = await DecodeAsync(path, width, category, token);
            token.ThrowIfCancellationRequested();
            if (bitmap is null) { FailedPreviews.TryAdd(key, 0); return; }
            Cache[key] = new(bitmap);
            if (version == _requestVersion && string.Equals(path, FilePath, StringComparison.OrdinalIgnoreCase)) Source = bitmap;
            if (Cache.Count > 2048) foreach (var item in Cache.Where(x => !x.Value.TryGetTarget(out _)).Take(512)) Cache.TryRemove(item.Key, out _);
        }
        catch (OperationCanceledException) { }
        catch { /* A failed preview must never stop the UI or the audit. */ }
        finally { if (entered) slots.Release(); }
    }

    void CancelLoad()
    {
        _queuedLoad?.Abort();
        _queuedLoad = null;
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = null;
    }

    static async Task<BitmapSource?> DecodeAsync(string path, int width, string? category, CancellationToken token)
    {
        try
        {
            if (string.Equals(category, "Video", StringComparison.OrdinalIgnoreCase))
            {
                using var samples = await VideoFrameSampler.SamplePreviewAsync(path, token);
                token.ThrowIfCancellationRequested();
                var frame = samples.Frames.FirstOrDefault();
                if (frame is null) return null;
                var pixels = new byte[frame.Width * frame.Height * 3];
                frame.CopyPixelDataTo(pixels);
                var bitmap = BitmapSource.Create(frame.Width, frame.Height, 96, 96, PixelFormats.Rgb24, null, pixels, frame.Width * 3);
                bitmap.Freeze();
                return bitmap;
            }
            return await Task.Run(() => DecodeImage(path, width), token);
        }
        catch (OperationCanceledException) { throw; }
        catch { return null; }
    }

    static BitmapSource DecodeImage(string path, int width)
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

    static SemaphoreSlim SlotsFor(string? category) =>
        string.Equals(category, "Video", StringComparison.OrdinalIgnoreCase) ? VideoDecodeSlots : ImageDecodeSlots;

    static string CacheKey(string path, int width, string? category) => $"{category}|{width}|{path}";
}
