using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace LocalCopilot_App.Services;

public enum OcrTransportPixelFormat : ushort
{
    Bgra8 = 1
}

public enum OcrTransportResponseStatus : ushort
{
    Ok = 0,
    BadRequest = 1,
    Unauthorized = 2,
    Busy = 3,
    DeadlineExceeded = 4,
    InferenceUnavailable = 5,
    InternalError = 6
}

public sealed record OcrTransportLimits(
    int MaxRegions,
    int MaxRequestBytes,
    int MaxResponseBytes,
    int MaxTextUtf8BytesPerRegion,
    TimeSpan MaxDeadline,
    TimeSpan MaxClockSkew)
{
    public static OcrTransportLimits M4_2_3Default { get; } =
        new(
            MaxRegions: 4,
            MaxRequestBytes: 16 * 1024 * 1024,
            MaxResponseBytes: 64 * 1024,
            MaxTextUtf8BytesPerRegion: 16 * 1024,
            MaxDeadline: TimeSpan.FromSeconds(15),
            MaxClockSkew: TimeSpan.FromSeconds(30));

    public void Validate()
    {
        if (MaxRegions <= 0 || MaxRegions > RegionOfInterestPlannerOptions.HardMaxRegions)
            throw new ArgumentOutOfRangeException(nameof(MaxRegions));
        if (MaxRequestBytes < 1024 || MaxRequestBytes > 64 * 1024 * 1024)
            throw new ArgumentOutOfRangeException(nameof(MaxRequestBytes));
        if (MaxResponseBytes < 1024 || MaxResponseBytes > 1024 * 1024)
            throw new ArgumentOutOfRangeException(nameof(MaxResponseBytes));
        if (MaxTextUtf8BytesPerRegion <= 0 ||
            MaxTextUtf8BytesPerRegion > MaxResponseBytes)
            throw new ArgumentOutOfRangeException(nameof(MaxTextUtf8BytesPerRegion));
        if (MaxDeadline <= TimeSpan.Zero || MaxDeadline > TimeSpan.FromSeconds(30))
            throw new ArgumentOutOfRangeException(nameof(MaxDeadline));
        if (MaxClockSkew <= TimeSpan.Zero || MaxClockSkew > TimeSpan.FromMinutes(5))
            throw new ArgumentOutOfRangeException(nameof(MaxClockSkew));
    }
}

public sealed class OcrTransportRegionPayload : IDisposable
{
    private byte[]? _pixels;

    public OcrTransportRegionPayload(
        CaptureRegion bounds,
        int strideBytes,
        OcrTransportPixelFormat pixelFormat,
        byte[] pixels)
    {
        if (bounds.IsEmpty)
            throw new ArgumentException("OCR region must be non-empty.", nameof(bounds));
        if (bounds.X < 0 || bounds.Y < 0)
            throw new ArgumentOutOfRangeException(nameof(bounds));
        if (!Enum.IsDefined(pixelFormat))
            throw new ArgumentOutOfRangeException(nameof(pixelFormat));
        ArgumentNullException.ThrowIfNull(pixels);

        int minimumStride = checked(bounds.Width * 4);
        if (pixelFormat == OcrTransportPixelFormat.Bgra8 &&
            strideBytes < minimumStride)
            throw new ArgumentOutOfRangeException(nameof(strideBytes));

        int requiredBytes = checked(strideBytes * bounds.Height);
        if (pixels.Length != requiredBytes)
            throw new ArgumentException(
                "Pixel buffer length must exactly match stride x height.",
                nameof(pixels));

        Bounds = bounds;
        StrideBytes = strideBytes;
        PixelFormat = pixelFormat;
        _pixels = pixels;
    }

    public CaptureRegion Bounds { get; }
    public int StrideBytes { get; }
    public OcrTransportPixelFormat PixelFormat { get; }
    public int ByteLength => _pixels?.Length ?? 0;
    public bool IsDisposed => _pixels is null;

    public ReadOnlyMemory<byte> Pixels =>
        _pixels is not null
            ? _pixels.AsMemory()
            : throw new ObjectDisposedException(nameof(OcrTransportRegionPayload));

    public void Dispose()
    {
        byte[]? pixels = Interlocked.Exchange(ref _pixels, null);
        if (pixels is not null)
            CryptographicOperations.ZeroMemory(pixels);
        GC.SuppressFinalize(this);
    }

    ~OcrTransportRegionPayload() => Dispose();

    public override string ToString() =>
        IsDisposed
            ? "<disposed-ocr-region>"
            : $"<ocr-region width={Bounds.Width} height={Bounds.Height} bytes={ByteLength}>";
}

public sealed class OcrTransportRequestPayload : IDisposable
{
    private readonly ReadOnlyCollection<OcrTransportRegionPayload> _regions;
    private int _disposed;

    public OcrTransportRequestPayload(
        long requestId,
        long epochId,
        DateTimeOffset deadline,
        IEnumerable<OcrTransportRegionPayload> regions)
    {
        if (requestId <= 0)
            throw new ArgumentOutOfRangeException(nameof(requestId));
        if (epochId <= 0)
            throw new ArgumentOutOfRangeException(nameof(epochId));
        ArgumentNullException.ThrowIfNull(regions);

        OcrTransportRegionPayload[] copied = regions.ToArray();
        if (copied.Length == 0)
            throw new ArgumentException("OCR transport request requires regions.", nameof(regions));
        if (copied.Any(region => region is null))
            throw new ArgumentException("OCR transport regions cannot contain null.", nameof(regions));

        RequestId = requestId;
        EpochId = epochId;
        Deadline = deadline;
        _regions = Array.AsReadOnly(copied);
    }

    public long RequestId { get; }
    public long EpochId { get; }
    public DateTimeOffset Deadline { get; }
    public IReadOnlyList<OcrTransportRegionPayload> Regions => _regions;
    public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        foreach (OcrTransportRegionPayload region in _regions)
            region.Dispose();

        GC.SuppressFinalize(this);
    }

    ~OcrTransportRequestPayload() => Dispose();
}

public sealed class OcrSensitiveText : IDisposable
{
    private char[]? _characters;

    internal OcrSensitiveText(char[] characters, int utf8ByteCount)
    {
        ArgumentNullException.ThrowIfNull(characters);
        _characters = characters;
        CharacterCount = characters.Length;
        Utf8ByteCount = utf8ByteCount;
    }

    public int CharacterCount { get; }
    public int Utf8ByteCount { get; }
    public bool IsDisposed => _characters is null;

    public ReadOnlyMemory<char> Characters =>
        _characters is not null
            ? _characters.AsMemory()
            : throw new ObjectDisposedException(nameof(OcrSensitiveText));

    public void Dispose()
    {
        char[]? characters = Interlocked.Exchange(ref _characters, null);
        if (characters is not null)
            Array.Clear(characters, 0, characters.Length);
        GC.SuppressFinalize(this);
    }

    ~OcrSensitiveText() => Dispose();

    public override string ToString() =>
        IsDisposed
            ? "<disposed-ocr-text>"
            : $"<sensitive-ocr-text chars={CharacterCount} utf8Bytes={Utf8ByteCount}>";
}

public sealed record OcrTransportTextResult(
    int RegionIndex,
    OcrSensitiveText Text) : IDisposable
{
    public void Dispose() => Text.Dispose();
}

public sealed class OcrTransportResponsePayload : IDisposable
{
    private readonly ReadOnlyCollection<OcrTransportTextResult> _texts;
    private int _disposed;

    internal OcrTransportResponsePayload(
        OcrTransportResponseStatus status,
        long requestId,
        long epochId,
        IEnumerable<OcrTransportTextResult> texts)
    {
        Status = status;
        RequestId = requestId;
        EpochId = epochId;
        _texts = Array.AsReadOnly(texts.ToArray());
    }

    public OcrTransportResponseStatus Status { get; }
    public long RequestId { get; }
    public long EpochId { get; }
    public IReadOnlyList<OcrTransportTextResult> Texts => _texts;
    public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        foreach (OcrTransportTextResult text in _texts)
            text.Dispose();
        GC.SuppressFinalize(this);
    }

    ~OcrTransportResponsePayload() => Dispose();
}

public static class OcrTransportProtocol
{
    public const ushort Version = 1;
    public const string RequestPath = "/v1/ocr";
    private static readonly byte[] RequestMagic = Encoding.ASCII.GetBytes("LCOPROC1");
    private static readonly byte[] ResponseMagic = Encoding.ASCII.GetBytes("LCOPRS01");
    private const int RequestFixedHeaderBytes = 40;
    private const int RequestRegionHeaderBytes = 28;
    private const int ResponseFixedHeaderBytes = 32;

    public static byte[] SerializeRequest(
        OcrTransportRequestPayload request,
        DateTimeOffset now,
        OcrTransportLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.IsDisposed)
            throw new ObjectDisposedException(nameof(request));

        OcrTransportLimits effective = limits ?? OcrTransportLimits.M4_2_3Default;
        effective.Validate();

        if (request.Regions.Count <= 0 || request.Regions.Count > effective.MaxRegions)
            throw new ArgumentOutOfRangeException(nameof(request), "Region count exceeds transport limits.");

        TimeSpan remaining = request.Deadline - now;
        if (remaining <= TimeSpan.Zero || remaining > effective.MaxDeadline)
            throw new ArgumentOutOfRangeException(nameof(request), "Deadline is outside transport limits.");

        long pixelBytes = request.Regions.Sum(region => (long)region.ByteLength);
        long totalBytes = checked(
            RequestFixedHeaderBytes +
            ((long)request.Regions.Count * RequestRegionHeaderBytes) +
            pixelBytes);

        if (totalBytes > effective.MaxRequestBytes || totalBytes > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(request), "Request payload exceeds transport byte limit.");

        byte[] body = new byte[(int)totalBytes];
        Span<byte> span = body;
        RequestMagic.CopyTo(span);
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(8, 2), Version);
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(10, 2), 0);
        BinaryPrimitives.WriteInt64BigEndian(span.Slice(12, 8), request.RequestId);
        BinaryPrimitives.WriteInt64BigEndian(span.Slice(20, 8), request.EpochId);
        BinaryPrimitives.WriteInt64BigEndian(
            span.Slice(28, 8),
            request.Deadline.ToUnixTimeMilliseconds());
        BinaryPrimitives.WriteInt32BigEndian(span.Slice(36, 4), request.Regions.Count);

        int headerOffset = RequestFixedHeaderBytes;
        int payloadOffset = checked(
            RequestFixedHeaderBytes +
            request.Regions.Count * RequestRegionHeaderBytes);

        foreach (OcrTransportRegionPayload region in request.Regions)
        {
            if (region.IsDisposed)
                throw new ObjectDisposedException(nameof(OcrTransportRegionPayload));

            CaptureRegion bounds = region.Bounds;
            Span<byte> descriptor = span.Slice(headerOffset, RequestRegionHeaderBytes);
            BinaryPrimitives.WriteInt32BigEndian(descriptor.Slice(0, 4), bounds.X);
            BinaryPrimitives.WriteInt32BigEndian(descriptor.Slice(4, 4), bounds.Y);
            BinaryPrimitives.WriteInt32BigEndian(descriptor.Slice(8, 4), bounds.Width);
            BinaryPrimitives.WriteInt32BigEndian(descriptor.Slice(12, 4), bounds.Height);
            BinaryPrimitives.WriteInt32BigEndian(descriptor.Slice(16, 4), region.StrideBytes);
            BinaryPrimitives.WriteUInt16BigEndian(descriptor.Slice(20, 2), (ushort)region.PixelFormat);
            BinaryPrimitives.WriteUInt16BigEndian(descriptor.Slice(22, 2), 0);
            BinaryPrimitives.WriteInt32BigEndian(descriptor.Slice(24, 4), region.ByteLength);

            region.Pixels.Span.CopyTo(span.Slice(payloadOffset, region.ByteLength));
            headerOffset += RequestRegionHeaderBytes;
            payloadOffset += region.ByteLength;
        }

        return body;
    }

    public static OcrTransportResponsePayload DeserializeResponse(
        ReadOnlySpan<byte> body,
        OcrTransportLimits? limits = null)
    {
        OcrTransportLimits effective = limits ?? OcrTransportLimits.M4_2_3Default;
        effective.Validate();

        if (body.Length < ResponseFixedHeaderBytes || body.Length > effective.MaxResponseBytes)
            throw new FormatException("OCR response size is invalid.");

        if (!body.Slice(0, 8).SequenceEqual(ResponseMagic))
            throw new FormatException("OCR response magic is invalid.");

        ushort version = BinaryPrimitives.ReadUInt16BigEndian(body.Slice(8, 2));
        if (version != Version)
            throw new FormatException("OCR response version is unsupported.");

        OcrTransportResponseStatus status =
            (OcrTransportResponseStatus)BinaryPrimitives.ReadUInt16BigEndian(body.Slice(10, 2));
        if (!Enum.IsDefined(status))
            throw new FormatException("OCR response status is invalid.");

        long requestId = BinaryPrimitives.ReadInt64BigEndian(body.Slice(12, 8));
        long epochId = BinaryPrimitives.ReadInt64BigEndian(body.Slice(20, 8));
        int textCount = BinaryPrimitives.ReadInt32BigEndian(body.Slice(28, 4));

        if (requestId <= 0 || epochId <= 0 || textCount < 0 || textCount > effective.MaxRegions)
            throw new FormatException("OCR response metadata is invalid.");

        int offset = ResponseFixedHeaderBytes;
        List<OcrTransportTextResult> texts = new(textCount);
        UTF8Encoding strictUtf8 = new(false, true);

        try
        {
            for (int i = 0; i < textCount; i++)
            {
                if (body.Length - offset < 8)
                    throw new FormatException("OCR response text header is truncated.");

                int regionIndex = BinaryPrimitives.ReadInt32BigEndian(body.Slice(offset, 4));
                int utf8Length = BinaryPrimitives.ReadInt32BigEndian(body.Slice(offset + 4, 4));
                offset += 8;

                if (regionIndex < 0 ||
                    utf8Length < 0 ||
                    utf8Length > effective.MaxTextUtf8BytesPerRegion ||
                    body.Length - offset < utf8Length)
                    throw new FormatException("OCR response text descriptor is invalid.");

                char[] characters = strictUtf8
                    .GetString(body.Slice(offset, utf8Length))
                    .ToCharArray();

                texts.Add(
                    new OcrTransportTextResult(
                        regionIndex,
                        new OcrSensitiveText(characters, utf8Length)));

                offset += utf8Length;
            }

            if (offset != body.Length)
                throw new FormatException("OCR response contains trailing bytes.");

            return new OcrTransportResponsePayload(
                status,
                requestId,
                epochId,
                texts);
        }
        catch
        {
            foreach (OcrTransportTextResult text in texts)
                text.Dispose();
            throw;
        }
    }

    public static string ComputeBodySha256Hex(ReadOnlySpan<byte> body) =>
        Convert.ToHexString(SHA256.HashData(body)).ToLowerInvariant();

    public static string ComputeRequestSignatureBase64(
        ReadOnlySpan<byte> authenticationKey,
        long timestampUnixSeconds,
        string nonceHex,
        string bodySha256Hex)
    {
        if (authenticationKey.Length < 32)
            throw new ArgumentOutOfRangeException(nameof(authenticationKey));
        if (timestampUnixSeconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(timestampUnixSeconds));
        if (string.IsNullOrWhiteSpace(nonceHex))
            throw new ArgumentException("Nonce is required.", nameof(nonceHex));
        if (string.IsNullOrWhiteSpace(bodySha256Hex))
            throw new ArgumentException("Body hash is required.", nameof(bodySha256Hex));

        string canonical =
            "POST\n" +
            RequestPath + "\n" +
            timestampUnixSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\n" +
            nonceHex.Trim().ToLowerInvariant() + "\n" +
            bodySha256Hex.Trim().ToLowerInvariant();

        byte[] canonicalBytes = Encoding.ASCII.GetBytes(canonical);
        try
        {
            using HMACSHA256 hmac = new(authenticationKey.ToArray());
            return Convert.ToBase64String(hmac.ComputeHash(canonicalBytes));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(canonicalBytes);
        }
    }
}
