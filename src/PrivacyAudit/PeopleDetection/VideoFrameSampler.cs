using System.Diagnostics;
using System.Runtime.InteropServices;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace PrivacyAudit.PeopleDetection;

/// <summary>Decodes at most two in-memory RGB frames with Windows Media Foundation.</summary>
public static class VideoFrameSampler
{
    const int FirstVideoStream = unchecked((int)0xfffffffc), MediaSource = unchecked((int)0xffffffff), EndOfStream = 2, MaxSamplesAfterSeek = 32;
    static readonly TimeSpan DecodeGuard = TimeSpan.FromMilliseconds(900);
    static Guid AdvancedProcessing = new("0f81da2c-b537-4672-a8b2-a681b17307a3"), DurationKey = new("6c990d33-bb8e-477a-8598-0d5d96fcd88a"),
        MajorTypeKey = new("48eba18e-f8c9-4687-bf11-0a74c9f96a8f"), SubtypeKey = new("f7e34c9a-42e8-4714-b74b-cb29d72c35e5"),
        FrameSizeKey = new("1652c33d-d6b2-4012-b834-72030849a37d"), StrideKey = new("644b4e48-1e02-4516-b0eb-c01ca9d49ac6"),
        VideoType = new("73646976-0000-0010-8000-00aa00389b71"), Rgb32 = new("00000016-0000-0010-8000-00aa00389b71");

    public static Task<VideoFrameSamples> SampleForClassificationAsync(string path, CancellationToken token = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path); token.ThrowIfCancellationRequested();
        return Task.Run(() => Sample(Path.GetFullPath(path), token, false), token);
    }

    /// <summary>Gets one in-memory representative frame for UI previews; no frame is written to disk.</summary>
    public static Task<VideoFrameSamples> SamplePreviewAsync(string path, CancellationToken token = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path); token.ThrowIfCancellationRequested();
        return Task.Run(() => Sample(Path.GetFullPath(path), token, true), token);
    }

    public static IReadOnlyList<TimeSpan> SelectSamplePositions(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero) return UnknownDurationPositions();
        if (duration.TotalSeconds < 2) return [Clamp(TimeSpan.FromTicks(duration.Ticks / 2), duration)];
        if (duration.TotalSeconds <= 5) return [Clamp(TimeSpan.FromTicks(duration.Ticks / 4), duration), Clamp(TimeSpan.FromTicks(duration.Ticks * 3 / 4), duration)];
        return [Clamp(TimeSpan.FromSeconds(1), duration), Clamp(TimeSpan.FromTicks(duration.Ticks / 2), duration)];
    }

    public static IReadOnlyList<TimeSpan> UnknownDurationPositions() => [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3)];

    public static TimeSpan SelectPreviewPosition(TimeSpan? duration) => duration is { } known
        ? known.TotalSeconds > 1 ? Clamp(TimeSpan.FromSeconds(1), known) : Clamp(TimeSpan.FromTicks(known.Ticks / 2), known)
        : TimeSpan.FromSeconds(1);

    static VideoFrameSamples Sample(string path, CancellationToken token, bool preview)
    {
        var co = Native.CoInitializeEx(IntPtr.Zero, 0) >= 0; IMFSourceReader? reader = null; IMFAttributes? attributes = null; IMFMediaType? type = null;
        try
        {
            Check(Native.MFStartup(0x00020070, 0), VideoDecodeCode.VideoUnsupported, "Media Foundation could not start.");
            Check(Native.MFCreateAttributes(out attributes, 1), VideoDecodeCode.VideoDecodeFailed, "Could not create decoder attributes.");
            Check(attributes.SetUINT32(ref AdvancedProcessing, 1), VideoDecodeCode.VideoDecodeFailed, "Could not enable video processing.");
            var hr = Native.MFCreateSourceReaderFromURL(path, attributes, out reader);
            if (hr < 0) throw OpenFailure(hr);
            reader.SetStreamSelection(unchecked((int)0xfffffffe), false);
            hr = reader.SetStreamSelection(FirstVideoStream, true);
            if (hr < 0) throw new VideoDecodeException(VideoDecodeCode.VideoNoVideoStream, "No video stream was found.", hr);
            Check(Native.MFCreateMediaType(out type), VideoDecodeCode.VideoDecodeFailed, "Could not create RGB output type.");
            Check(type.SetGUID(ref MajorTypeKey, ref VideoType), VideoDecodeCode.VideoDecodeFailed, "Could not set video output type.");
            Check(type.SetGUID(ref SubtypeKey, ref Rgb32), VideoDecodeCode.VideoDecodeFailed, "Could not request RGB decoding.");
            Check(type.SetUINT64(ref FrameSizeKey, ((ulong)224 << 32) | 224), VideoDecodeCode.VideoDecodeFailed, "Could not request the analysis frame size.");
            hr = reader.SetCurrentMediaType(FirstVideoStream, IntPtr.Zero, type);
            if (hr < 0) throw new VideoDecodeException(hr == unchecked((int)0xc00d5212) ? VideoDecodeCode.VideoNoDecoder : VideoDecodeCode.VideoUnsupported, "Windows has no compatible decoder for this video.", hr);

            var duration = GetDuration(reader); var positions = preview
                ? [SelectPreviewPosition(duration)]
                : duration is { } known ? SelectSamplePositions(known) : UnknownDurationPositions();
            var frames = new List<Image<Rgb24>>(2); var diagnostics = new List<VideoFrameDiagnostic>(2);
            try
            {
                foreach (var position in positions) { token.ThrowIfCancellationRequested(); diagnostics.Add(Decode(reader, position, duration, token, out var frame)); if (frame is not null) frames.Add(frame); }
                if (frames.Count == 0) throw new VideoDecodeException(diagnostics.Any(x => x.Code == VideoDecodeCode.VideoDecodeTimeout) ? VideoDecodeCode.VideoDecodeTimeout : VideoDecodeCode.VideoDecodeFailed, "No video frame could be decoded.", diagnostics: diagnostics);
                return new(duration, positions, frames, diagnostics);
            }
            catch { foreach (var frame in frames) frame.Dispose(); throw; }
        }
        finally { Release(type); Release(reader); Release(attributes); Native.MFShutdown(); if (co) Native.CoUninitialize(); }
    }

    static TimeSpan? GetDuration(IMFSourceReader reader)
    {
        var key = DurationKey; var value = default(PropVariant);
        try { return reader.GetPresentationAttribute(MediaSource, ref key, out value) >= 0 && value.VarType is 20 or 21 && value.LongValue > 0 ? TimeSpan.FromTicks(value.LongValue) : null; }
        finally { Native.PropVariantClear(ref value); }
    }

    static VideoFrameDiagnostic Decode(IMFSourceReader reader, TimeSpan requested, TimeSpan? duration, CancellationToken token, out Image<Rgb24>? image)
    {
        image = null; var position = PropVariant.FromLong(requested.Ticks); var format = Guid.Empty; var watch = Stopwatch.StartNew();
        var hr = reader.SetCurrentPosition(ref format, ref position);
        if (hr < 0) return Failure(VideoDecodeCode.VideoDecodeFailed, $"Seek failed (0x{hr:X8}).");
        for (var attempt = 0; attempt < MaxSamplesAfterSeek; attempt++)
        {
            token.ThrowIfCancellationRequested();
            if (watch.Elapsed > DecodeGuard) return Failure(VideoDecodeCode.VideoDecodeTimeout, "Emergency decode guard elapsed.");
            IMFSample? sample = null;
            try
            {
                hr = reader.ReadSample(FirstVideoStream, 0, out _, out var flags, out var timestamp, out sample);
                if (hr < 0) return Failure(VideoDecodeCode.VideoDecodeFailed, $"ReadSample failed (0x{hr:X8}).");
                if ((flags & EndOfStream) != 0 && sample is null) return Failure(VideoDecodeCode.VideoDecodeFailed, "End of stream before a decoded frame.");
                if (sample is null) continue;
                image = CopyFrame(reader, sample, out var width, out var height);
                return new(duration, requested, TimeSpan.FromTicks(timestamp), width, height, watch.Elapsed, null, "Decoded");
            }
            catch (Exception ex) when (ex is COMException or InvalidDataException) { return Failure(VideoDecodeCode.VideoDecodeFailed, ex.Message); }
            finally { Release(sample); }
        }
        return Failure(VideoDecodeCode.VideoDecodeTimeout, "No decoded frame within the bounded read limit.");

        VideoFrameDiagnostic Failure(VideoDecodeCode code, string result) => new(duration, requested, null, 0, 0, watch.Elapsed, code, result);
    }

    static Image<Rgb24> CopyFrame(IMFSourceReader reader, IMFSample sample, out int width, out int height)
    {
        IMFMediaType? type = null; IMFMediaBuffer? buffer = null; var locked = false;
        try
        {
            Marshal.ThrowExceptionForHR(reader.GetCurrentMediaType(FirstVideoStream, out type));
            if (type.GetUINT64(ref FrameSizeKey, out var size) >= 0) { width = checked((int)(size >> 32)); height = checked((int)(size & 0xffffffff)); }
            else { width = 224; height = 224; }
            if (width <= 0 || height <= 0) throw new InvalidDataException("Decoded frame dimensions are invalid.");
            var stride = width * 4; if (type.GetUINT32(ref StrideKey, out var rawStride) >= 0) stride = unchecked((int)rawStride);
            Marshal.ThrowExceptionForHR(sample.ConvertToContiguousBuffer(out buffer)); Marshal.ThrowExceptionForHR(buffer.Lock(out var pointer, out _, out var length)); locked = true;
            var absoluteStride = Math.Abs(stride); if (length < absoluteStride * height) throw new InvalidDataException("Decoded frame buffer is incomplete.");
            var pixels = new byte[width * height * 4];
            for (var y = 0; y < height; y++) Marshal.Copy(IntPtr.Add(pointer, (stride < 0 ? height - 1 - y : y) * absoluteStride), pixels, y * width * 4, width * 4);
            using var bgra = SixLabors.ImageSharp.Image.LoadPixelData<Bgra32>(pixels, width, height); return bgra.CloneAs<Rgb24>();
        }
        finally { if (locked && buffer is not null) buffer.Unlock(); Release(buffer); Release(type); }
    }

    static VideoDecodeException OpenFailure(int hr) => hr switch
    {
        unchecked((int)0xc00d5212) => new(VideoDecodeCode.VideoNoDecoder, "Windows has no compatible decoder for this video.", hr),
        _ => new(VideoDecodeCode.VideoUnsupported, "Windows Media Foundation could not open this video.", hr)
    };
    static void Check(int hr, VideoDecodeCode code, string message) { if (hr < 0) throw new VideoDecodeException(code, message, hr); }
    static TimeSpan Clamp(TimeSpan value, TimeSpan duration) { var last = duration > TimeSpan.FromMilliseconds(50) ? duration - TimeSpan.FromMilliseconds(50) : TimeSpan.Zero; return value < TimeSpan.Zero ? TimeSpan.Zero : value > last ? last : value; }
    static void Release(object? value) { if (value is not null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value); }

    [StructLayout(LayoutKind.Explicit)] struct PropVariant { [FieldOffset(0)] public ushort VarType; [FieldOffset(8)] public long LongValue; public static PropVariant FromLong(long value) => new() { VarType = 20, LongValue = value }; }

    static class Native
    {
        [DllImport("ole32.dll")] public static extern int CoInitializeEx(IntPtr reserved, int coInit); [DllImport("ole32.dll")] public static extern void CoUninitialize();
        [DllImport("ole32.dll")] public static extern int PropVariantClear(ref PropVariant value); [DllImport("mfplat.dll")] public static extern int MFStartup(int version, int flags);
        [DllImport("mfplat.dll")] public static extern int MFShutdown(); [DllImport("mfplat.dll")] public static extern int MFCreateAttributes(out IMFAttributes attributes, int initialSize);
        [DllImport("mfplat.dll")] public static extern int MFCreateMediaType(out IMFMediaType mediaType);
        [DllImport("mfreadwrite.dll", CharSet = CharSet.Unicode)] public static extern int MFCreateSourceReaderFromURL(string url, IMFAttributes? attributes, out IMFSourceReader reader);
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("2CD2D921-C447-44A7-A13C-4ADABFC247E3")]
    interface IMFAttributes
    {
        [PreserveSig] int GetItem(ref Guid k, IntPtr v); [PreserveSig] int GetItemType(ref Guid k, out int t); [PreserveSig] int CompareItem(ref Guid k, IntPtr v, out bool r); [PreserveSig] int Compare(IMFAttributes a, int m, out bool r);
        [PreserveSig] int GetUINT32(ref Guid k, out uint v); [PreserveSig] int GetUINT64(ref Guid k, out ulong v); [PreserveSig] int GetDouble(ref Guid k, out double v); [PreserveSig] int GetGUID(ref Guid k, out Guid v);
        [PreserveSig] int GetStringLength(ref Guid k, out uint l); [PreserveSig] int GetString(ref Guid k, IntPtr v, uint s, out uint l); [PreserveSig] int GetAllocatedString(ref Guid k, out IntPtr v, out uint l);
        [PreserveSig] int GetBlobSize(ref Guid k, out uint s); [PreserveSig] int GetBlob(ref Guid k, IntPtr b, uint s, out uint z); [PreserveSig] int GetAllocatedBlob(ref Guid k, out IntPtr b, out uint s);
        [PreserveSig] int GetUnknown(ref Guid k, ref Guid i, out IntPtr v); [PreserveSig] int SetItem(ref Guid k, IntPtr v); [PreserveSig] int DeleteItem(ref Guid k); [PreserveSig] int DeleteAllItems();
        [PreserveSig] int SetUINT32(ref Guid k, uint v); [PreserveSig] int SetUINT64(ref Guid k, ulong v); [PreserveSig] int SetDouble(ref Guid k, double v); [PreserveSig] int SetGUID(ref Guid k, ref Guid v);
        [PreserveSig] int SetString(ref Guid k, string v); [PreserveSig] int SetBlob(ref Guid k, IntPtr b, uint s); [PreserveSig] int SetUnknown(ref Guid k, IntPtr u); [PreserveSig] int LockStore();
        [PreserveSig] int UnlockStore(); [PreserveSig] int GetCount(out uint c); [PreserveSig] int GetItemByIndex(uint i, out Guid k, IntPtr v); [PreserveSig] int CopyAllItems(IMFAttributes d);
    }
    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("44AE0FA8-EA31-4109-8D2E-4CAE4997C555")]
    interface IMFMediaType : IMFAttributes { [PreserveSig] int GetMajorType(out Guid t); [PreserveSig] int IsCompressedFormat(out bool c); [PreserveSig] int IsEqual(IMFMediaType t, out uint f); [PreserveSig] int GetRepresentation(Guid r, out IntPtr v); [PreserveSig] int FreeRepresentation(Guid r, IntPtr v); }
    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("70AE66F2-C809-4E4F-8915-BDCB406B7993")]
    interface IMFSourceReader
    {
        [PreserveSig] int GetStreamSelection(int i, out bool s); [PreserveSig] int SetStreamSelection(int i, [MarshalAs(UnmanagedType.Bool)] bool s); [PreserveSig] int GetNativeMediaType(int i, int n, out IMFMediaType t);
        [PreserveSig] int GetCurrentMediaType(int i, out IMFMediaType t); [PreserveSig] int SetCurrentMediaType(int i, IntPtr r, IMFMediaType t); [PreserveSig] int SetCurrentPosition(ref Guid f, ref PropVariant p);
        [PreserveSig] int ReadSample(int i, int c, out int a, out int f, out long t, out IMFSample? s); [PreserveSig] int Flush(int i); [PreserveSig] int GetServiceForStream(int i, ref Guid s, ref Guid q, out IntPtr v);
        [PreserveSig] int GetPresentationAttribute(int i, ref Guid k, out PropVariant v);
    }
    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("C40A00F2-B93A-4D80-AE8C-5A1C634F58E4")]
    interface IMFSample
    {
        [PreserveSig] int GetItem(ref Guid k, IntPtr v); [PreserveSig] int GetItemType(ref Guid k, out int t); [PreserveSig] int CompareItem(ref Guid k, IntPtr v, out bool r); [PreserveSig] int Compare(IMFAttributes a, int m, out bool r);
        [PreserveSig] int GetUINT32(ref Guid k, out uint v); [PreserveSig] int GetUINT64(ref Guid k, out ulong v); [PreserveSig] int GetDouble(ref Guid k, out double v); [PreserveSig] int GetGUID(ref Guid k, out Guid v);
        [PreserveSig] int GetStringLength(ref Guid k, out uint l); [PreserveSig] int GetString(ref Guid k, IntPtr v, uint s, out uint l); [PreserveSig] int GetAllocatedString(ref Guid k, out IntPtr v, out uint l);
        [PreserveSig] int GetBlobSize(ref Guid k, out uint s); [PreserveSig] int GetBlob(ref Guid k, IntPtr b, uint s, out uint z); [PreserveSig] int GetAllocatedBlob(ref Guid k, out IntPtr b, out uint s);
        [PreserveSig] int GetUnknown(ref Guid k, ref Guid i, out IntPtr v); [PreserveSig] int SetItem(ref Guid k, IntPtr v); [PreserveSig] int DeleteItem(ref Guid k); [PreserveSig] int DeleteAllItems();
        [PreserveSig] int SetUINT32(ref Guid k, uint v); [PreserveSig] int SetUINT64(ref Guid k, ulong v); [PreserveSig] int SetDouble(ref Guid k, double v); [PreserveSig] int SetGUID(ref Guid k, ref Guid v);
        [PreserveSig] int SetString(ref Guid k, string v); [PreserveSig] int SetBlob(ref Guid k, IntPtr b, uint s); [PreserveSig] int SetUnknown(ref Guid k, IntPtr u); [PreserveSig] int LockStore();
        [PreserveSig] int UnlockStore(); [PreserveSig] int GetCount(out uint c); [PreserveSig] int GetItemByIndex(uint i, out Guid k, IntPtr v); [PreserveSig] int CopyAllItems(IMFAttributes d);
        [PreserveSig] int GetSampleFlags(out int f); [PreserveSig] int SetSampleFlags(int f); [PreserveSig] int GetSampleTime(out long t); [PreserveSig] int SetSampleTime(long t); [PreserveSig] int GetSampleDuration(out long d);
        [PreserveSig] int SetSampleDuration(long d); [PreserveSig] int GetBufferCount(out int c); [PreserveSig] int GetBufferByIndex(int i, out IMFMediaBuffer b); [PreserveSig] int ConvertToContiguousBuffer(out IMFMediaBuffer b);
        [PreserveSig] int AddBuffer(IMFMediaBuffer b); [PreserveSig] int RemoveBufferByIndex(int i); [PreserveSig] int RemoveAllBuffers(); [PreserveSig] int GetTotalLength(out int l); [PreserveSig] int CopyToBuffer(IMFMediaBuffer b);
    }
    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("045FA593-8799-42B8-BC8D-8968C6453507")]
    interface IMFMediaBuffer { [PreserveSig] int Lock(out IntPtr b, out int m, out int c); [PreserveSig] int Unlock(); [PreserveSig] int GetCurrentLength(out int l); [PreserveSig] int SetCurrentLength(int l); [PreserveSig] int GetMaxLength(out int l); }
}

public enum VideoDecodeCode { VideoNoDecoder, VideoUnsupported, VideoNoVideoStream, VideoDecodeFailed, VideoDecodeTimeout }
public sealed record VideoFrameDiagnostic(TimeSpan? Duration, TimeSpan Requested, TimeSpan? Actual, int Width, int Height, TimeSpan DecodeTime, VideoDecodeCode? Code, string Result);
public sealed class VideoDecodeException(VideoDecodeCode code, string message, int? hresult = null, IReadOnlyList<VideoFrameDiagnostic>? diagnostics = null) : Exception(message)
{
    public VideoDecodeCode Code { get; } = code; public IReadOnlyList<VideoFrameDiagnostic> Diagnostics { get; } = diagnostics ?? [];
    public override string ToString() => hresult is { } hr ? $"{Code}: {Message} (0x{hr:X8})" : $"{Code}: {Message}";
}
public sealed class VideoFrameSamples(TimeSpan? duration, IReadOnlyList<TimeSpan> positions, IReadOnlyList<Image<Rgb24>> frames, IReadOnlyList<VideoFrameDiagnostic> diagnostics) : IDisposable
{
    public TimeSpan? Duration { get; } = duration; public IReadOnlyList<TimeSpan> Positions { get; } = positions; public IReadOnlyList<Image<Rgb24>> Frames { get; } = frames;
    public IReadOnlyList<VideoFrameDiagnostic> Diagnostics { get; } = diagnostics; public void Dispose() { foreach (var frame in Frames) frame.Dispose(); }
}
