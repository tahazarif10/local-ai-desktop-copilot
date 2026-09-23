using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace LocalCopilot_App.Services;

public enum OcrTransportOutcome : ushort
{
    Success = 0,
    Rejected = 1,
    Unavailable = 2,
    Timeout = 3,
    Cancelled = 4,
    Busy = 5,
    Faulted = 6
}

public enum OcrTransportReason : ushort
{
    None = 0,
    InvalidRequest = 1,
    UnsupportedVersion = 2,
    DeadlineExpired = 3,
    PayloadLimit = 4,
    AuthenticationFailed = 5,
    WorkerUnavailable = 6,
    WorkerTimeout = 7,
    Cancelled = 8,
    Busy = 9,
    InternalFailure = 10
}

public static class OcrTransportLimits
{
    public const ushort ProtocolVersion = 1;
    public const int MaxRegions = 4;
    public const int MaxRegionDimension = 8192;
    public const long MaxRegionPixels = 4_194_304;
    public const int MaxEncodedRegionBytes = 16 * 1024 * 1024;
    public const int MaxRequestEncodedBytes = 32 * 1024 * 1024;
    public const int MaxRegionTextUtf8Bytes = 16 * 1024;
    public const int MaxResponseTextUtf8Bytes = 64 * 1024;

    public static TimeSpan MaxRequestDeadline { get; } =
        TimeSpan.FromSeconds(15);
}

public sealed class OcrEncodedRegion : IDisposable
{
    private byte[]? _pngBytes;

    public OcrEncodedRegion(
        int width,
        int height,
        byte[] ownedPngBytes)
    {
        ArgumentNullException.ThrowIfNull(ownedPngBytes);

        if (width <= 0 ||
            height <= 0 ||
            width > OcrTransportLimits.MaxRegionDimension ||
            height > OcrTransportLimits.MaxRegionDimension ||
            checked((long)width * height) >
                OcrTransportLimits.MaxRegionPixels)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width),
                "OCR transport region dimensions exceed the protocol limit.");
        }

        if (ownedPngBytes.Length <= 0 ||
            ownedPngBytes.Length >
                OcrTransportLimits.MaxEncodedRegionBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ownedPngBytes),
                "OCR transport region byte length exceeds the protocol limit.");
        }

        Width = width;
        Height = height;
        _pngBytes = ownedPngBytes;
    }

    public int Width { get; }

    public int Height { get; }

    public int EncodedByteCount =>
        _pngBytes?.Length ??
        throw new ObjectDisposedException(nameof(OcrEncodedRegion));

    public ReadOnlyMemory<byte> PngBytes =>
        _pngBytes is not null
            ? _pngBytes.AsMemory()
            : throw new ObjectDisposedException(nameof(OcrEncodedRegion));

    public bool IsDisposed => _pngBytes is null;

    public void Dispose()
    {
        byte[]? bytes =
            Interlocked.Exchange(ref _pngBytes, null);

        if (bytes is not null)
        {
            Array.Clear(bytes);
        }

        GC.SuppressFinalize(this);
    }

    ~OcrEncodedRegion()
    {
        Dispose();
    }

    public override string ToString() =>
        IsDisposed
            ? "<ocr-region disposed>"
            : $"<ocr-region {Width}x{Height} bytes={EncodedByteCount} content=redacted>";
}

public sealed class OcrTransportRequest : IDisposable
{
    private readonly ReadOnlyCollection<OcrEncodedRegion> _regions;
    private int _disposed;

    public OcrTransportRequest(
        long requestId,
        long epochId,
        DateTimeOffset deadlineUtc,
        IEnumerable<OcrEncodedRegion> regions)
    {
        ArgumentNullException.ThrowIfNull(regions);

        if (requestId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(requestId));
        }

        if (epochId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(epochId));
        }

        if (deadlineUtc == default)
        {
            throw new ArgumentOutOfRangeException(nameof(deadlineUtc));
        }

        OcrEncodedRegion[] copied = regions.ToArray();

        if (copied.Length == 0 ||
            copied.Length > OcrTransportLimits.MaxRegions ||
            copied.Any(region => region is null || region.IsDisposed))
        {
            throw new ArgumentException(
                "OCR transport request has an invalid region set.",
                nameof(regions));
        }

        long totalEncodedBytes =
            copied.Sum(region => (long)region.EncodedByteCount);

        if (totalEncodedBytes >
            OcrTransportLimits.MaxRequestEncodedBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(regions),
                "OCR transport request exceeds the encoded byte limit.");
        }

        RequestId = requestId;
        EpochId = epochId;
        DeadlineUtc = deadlineUtc.ToUniversalTime();
        TotalEncodedBytes = totalEncodedBytes;
        _regions = Array.AsReadOnly(copied);
    }

    public long RequestId { get; }

    public long EpochId { get; }

    public DateTimeOffset DeadlineUtc { get; }

    public long TotalEncodedBytes { get; }

    public IReadOnlyList<OcrEncodedRegion> Regions => _regions;

    public bool IsDisposed =>
        Volatile.Read(ref _disposed) != 0;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        foreach (OcrEncodedRegion region in _regions)
        {
            region.Dispose();
        }

        GC.SuppressFinalize(this);
    }

    ~OcrTransportRequest()
    {
        Dispose();
    }

    public override string ToString() =>
        $"<ocr-request request={RequestId} epoch={EpochId} regions={_regions.Count} " +
        $"encodedBytes={TotalEncodedBytes} content=redacted disposed={IsDisposed}>";
}

public sealed class OcrSensitiveUtf8Text : IDisposable
{
    private byte[]? _utf8Bytes;

    public OcrSensitiveUtf8Text(byte[] ownedUtf8Bytes)
    {
        ArgumentNullException.ThrowIfNull(ownedUtf8Bytes);

        if (ownedUtf8Bytes.Length >
            OcrTransportLimits.MaxRegionTextUtf8Bytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ownedUtf8Bytes),
                "OCR text exceeds the per-region protocol limit.");
        }

        _utf8Bytes = ownedUtf8Bytes;
    }

    public int ByteCount =>
        _utf8Bytes?.Length ??
        throw new ObjectDisposedException(nameof(OcrSensitiveUtf8Text));

    public ReadOnlyMemory<byte> Utf8Bytes =>
        _utf8Bytes is not null
            ? _utf8Bytes.AsMemory()
            : throw new ObjectDisposedException(nameof(OcrSensitiveUtf8Text));

    public bool IsDisposed => _utf8Bytes is null;

    public void Dispose()
    {
        byte[]? bytes =
            Interlocked.Exchange(ref _utf8Bytes, null);

        if (bytes is not null)
        {
            Array.Clear(bytes);
        }

        GC.SuppressFinalize(this);
    }

    ~OcrSensitiveUtf8Text()
    {
        Dispose();
    }

    public override string ToString() =>
        IsDisposed
            ? "<ocr-text disposed>"
            : $"<ocr-text utf8Bytes={ByteCount} content=redacted>";
}

public sealed class OcrTransportResponse : IDisposable
{
    private readonly ReadOnlyCollection<OcrSensitiveUtf8Text> _texts;
    private int _disposed;

    public OcrTransportResponse(
        long requestId,
        long epochId,
        OcrTransportOutcome outcome,
        OcrTransportReason reason,
        IEnumerable<OcrSensitiveUtf8Text> texts)
    {
        ArgumentNullException.ThrowIfNull(texts);

        if (requestId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(requestId));
        }

        if (epochId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(epochId));
        }

        if (!Enum.IsDefined(outcome))
        {
            throw new ArgumentOutOfRangeException(nameof(outcome));
        }

        if (!Enum.IsDefined(reason))
        {
            throw new ArgumentOutOfRangeException(nameof(reason));
        }

        OcrSensitiveUtf8Text[] copied = texts.ToArray();

        if (copied.Length > OcrTransportLimits.MaxRegions ||
            copied.Any(text => text is null || text.IsDisposed))
        {
            throw new ArgumentException(
                "OCR transport response has an invalid text set.",
                nameof(texts));
        }

        if (outcome == OcrTransportOutcome.Success &&
            (reason != OcrTransportReason.None ||
             copied.Length == 0))
        {
            throw new ArgumentException(
                "Successful OCR responses require text slots and no failure reason.",
                nameof(texts));
        }

        if (outcome != OcrTransportOutcome.Success &&
            copied.Length != 0)
        {
            throw new ArgumentException(
                "Failed OCR responses must not carry OCR text.",
                nameof(texts));
        }

        long totalBytes =
            copied.Sum(text => (long)text.ByteCount);

        if (totalBytes > OcrTransportLimits.MaxResponseTextUtf8Bytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(texts),
                "OCR response exceeds the total text byte limit.");
        }

        RequestId = requestId;
        EpochId = epochId;
        Outcome = outcome;
        Reason = reason;
        TotalTextUtf8Bytes = totalBytes;
        _texts = Array.AsReadOnly(copied);
    }

    public long RequestId { get; }

    public long EpochId { get; }

    public OcrTransportOutcome Outcome { get; }

    public OcrTransportReason Reason { get; }

    public long TotalTextUtf8Bytes { get; }

    public IReadOnlyList<OcrSensitiveUtf8Text> Texts => _texts;

    public bool IsDisposed =>
        Volatile.Read(ref _disposed) != 0;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        foreach (OcrSensitiveUtf8Text text in _texts)
        {
            text.Dispose();
        }

        GC.SuppressFinalize(this);
    }

    ~OcrTransportResponse()
    {
        Dispose();
    }

    public override string ToString() =>
        $"<ocr-response request={RequestId} epoch={EpochId} outcome={Outcome} " +
        $"reason={Reason} regions={_texts.Count} utf8Bytes={TotalTextUtf8Bytes} " +
        $"content=redacted disposed={IsDisposed}>";
}

public static class OcrTransportCodec
{
    private const int RequestHeaderBytes = 36;
    private const int RequestRegionHeaderBytes = 12;
    private const int ResponseHeaderBytes = 32;
    private const int ResponseTextHeaderBytes = 4;

    private static ReadOnlySpan<byte> RequestMagic => "LCOR"u8;

    private static ReadOnlySpan<byte> ResponseMagic => "LCOS"u8;

    public static async ValueTask WriteRequestAsync(
        Stream destination,
        OcrTransportRequest request,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(request);

        cancellationToken.ThrowIfCancellationRequested();

        if (request.IsDisposed)
        {
            throw new ObjectDisposedException(nameof(OcrTransportRequest));
        }

        ValidateDeadline(request.DeadlineUtc, utcNow);

        byte[] header = new byte[RequestHeaderBytes];
        RequestMagic.CopyTo(header);
        BinaryPrimitives.WriteUInt16BigEndian(
            header.AsSpan(4, 2),
            OcrTransportLimits.ProtocolVersion);
        BinaryPrimitives.WriteUInt16BigEndian(
            header.AsSpan(6, 2),
            0);
        BinaryPrimitives.WriteInt64BigEndian(
            header.AsSpan(8, 8),
            request.RequestId);
        BinaryPrimitives.WriteInt64BigEndian(
            header.AsSpan(16, 8),
            request.EpochId);
        BinaryPrimitives.WriteInt64BigEndian(
            header.AsSpan(24, 8),
            request.DeadlineUtc.ToUnixTimeMilliseconds());
        BinaryPrimitives.WriteInt32BigEndian(
            header.AsSpan(32, 4),
            request.Regions.Count);

        await destination.WriteAsync(
            header,
            cancellationToken).ConfigureAwait(false);

        foreach (OcrEncodedRegion region in request.Regions)
        {
            byte[] regionHeader =
                new byte[RequestRegionHeaderBytes];

            BinaryPrimitives.WriteInt32BigEndian(
                regionHeader.AsSpan(0, 4),
                region.Width);
            BinaryPrimitives.WriteInt32BigEndian(
                regionHeader.AsSpan(4, 4),
                region.Height);
            BinaryPrimitives.WriteInt32BigEndian(
                regionHeader.AsSpan(8, 4),
                region.EncodedByteCount);

            await destination.WriteAsync(
                regionHeader,
                cancellationToken).ConfigureAwait(false);

            await destination.WriteAsync(
                region.PngBytes,
                cancellationToken).ConfigureAwait(false);
        }
    }

    public static async ValueTask<OcrTransportRequest> ReadRequestAsync(
        Stream source,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();

        byte[] header = new byte[RequestHeaderBytes];
        await ReadExactlyAsync(
            source,
            header,
            cancellationToken).ConfigureAwait(false);

        if (!header.AsSpan(0, 4).SequenceEqual(RequestMagic))
        {
            throw new InvalidDataException("Invalid OCR request magic.");
        }

        ushort version =
            BinaryPrimitives.ReadUInt16BigEndian(
                header.AsSpan(4, 2));

        if (version != OcrTransportLimits.ProtocolVersion)
        {
            throw new InvalidDataException(
                "Unsupported OCR transport protocol version.");
        }

        if (BinaryPrimitives.ReadUInt16BigEndian(
                header.AsSpan(6, 2)) != 0)
        {
            throw new InvalidDataException(
                "OCR request reserved flags must be zero.");
        }

        long requestId =
            BinaryPrimitives.ReadInt64BigEndian(
                header.AsSpan(8, 8));
        long epochId =
            BinaryPrimitives.ReadInt64BigEndian(
                header.AsSpan(16, 8));
        long deadlineUnixMs =
            BinaryPrimitives.ReadInt64BigEndian(
                header.AsSpan(24, 8));
        int regionCount =
            BinaryPrimitives.ReadInt32BigEndian(
                header.AsSpan(32, 4));

        if (requestId <= 0 ||
            epochId <= 0 ||
            regionCount <= 0 ||
            regionCount > OcrTransportLimits.MaxRegions)
        {
            throw new InvalidDataException(
                "OCR request metadata is outside protocol limits.");
        }

        DateTimeOffset deadlineUtc;
        try
        {
            deadlineUtc =
                DateTimeOffset.FromUnixTimeMilliseconds(deadlineUnixMs);
        }
        catch (ArgumentOutOfRangeException exc)
        {
            throw new InvalidDataException(
                "OCR request deadline is invalid.",
                exc);
        }

        ValidateDeadline(deadlineUtc, utcNow);

        List<OcrEncodedRegion> regions =
            new(regionCount);

        long totalEncodedBytes = 0;

        try
        {
            for (int index = 0; index < regionCount; index++)
            {
                byte[] regionHeader =
                    new byte[RequestRegionHeaderBytes];

                await ReadExactlyAsync(
                    source,
                    regionHeader,
                    cancellationToken).ConfigureAwait(false);

                int width =
                    BinaryPrimitives.ReadInt32BigEndian(
                        regionHeader.AsSpan(0, 4));
                int height =
                    BinaryPrimitives.ReadInt32BigEndian(
                        regionHeader.AsSpan(4, 4));
                int byteCount =
                    BinaryPrimitives.ReadInt32BigEndian(
                        regionHeader.AsSpan(8, 4));

                ValidateRegionMetadata(
                    width,
                    height,
                    byteCount);

                totalEncodedBytes =
                    checked(totalEncodedBytes + byteCount);

                if (totalEncodedBytes >
                    OcrTransportLimits.MaxRequestEncodedBytes)
                {
                    throw new InvalidDataException(
                        "OCR request exceeds the total encoded byte limit.");
                }

                byte[] bytes = new byte[byteCount];

                try
                {
                    await ReadExactlyAsync(
                        source,
                        bytes,
                        cancellationToken).ConfigureAwait(false);

                    regions.Add(
                        new OcrEncodedRegion(
                            width,
                            height,
                            bytes));
                }
                catch
                {
                    Array.Clear(bytes);
                    throw;
                }
            }

            return new OcrTransportRequest(
                requestId,
                epochId,
                deadlineUtc,
                regions);
        }
        catch
        {
            foreach (OcrEncodedRegion region in regions)
            {
                region.Dispose();
            }

            throw;
        }
    }

    public static async ValueTask WriteResponseAsync(
        Stream destination,
        OcrTransportResponse response,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(response);

        cancellationToken.ThrowIfCancellationRequested();

        if (response.IsDisposed)
        {
            throw new ObjectDisposedException(nameof(OcrTransportResponse));
        }

        byte[] header = new byte[ResponseHeaderBytes];
        ResponseMagic.CopyTo(header);
        BinaryPrimitives.WriteUInt16BigEndian(
            header.AsSpan(4, 2),
            OcrTransportLimits.ProtocolVersion);
        BinaryPrimitives.WriteUInt16BigEndian(
            header.AsSpan(6, 2),
            (ushort)response.Outcome);
        BinaryPrimitives.WriteUInt16BigEndian(
            header.AsSpan(8, 2),
            (ushort)response.Reason);
        BinaryPrimitives.WriteUInt16BigEndian(
            header.AsSpan(10, 2),
            0);
        BinaryPrimitives.WriteInt64BigEndian(
            header.AsSpan(12, 8),
            response.RequestId);
        BinaryPrimitives.WriteInt64BigEndian(
            header.AsSpan(20, 8),
            response.EpochId);
        BinaryPrimitives.WriteInt32BigEndian(
            header.AsSpan(28, 4),
            response.Texts.Count);

        await destination.WriteAsync(
            header,
            cancellationToken).ConfigureAwait(false);

        foreach (OcrSensitiveUtf8Text text in response.Texts)
        {
            byte[] textHeader =
                new byte[ResponseTextHeaderBytes];

            BinaryPrimitives.WriteInt32BigEndian(
                textHeader,
                text.ByteCount);

            await destination.WriteAsync(
                textHeader,
                cancellationToken).ConfigureAwait(false);

            if (text.ByteCount > 0)
            {
                await destination.WriteAsync(
                    text.Utf8Bytes,
                    cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public static async ValueTask<OcrTransportResponse> ReadResponseAsync(
        Stream source,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();

        byte[] header = new byte[ResponseHeaderBytes];
        await ReadExactlyAsync(
            source,
            header,
            cancellationToken).ConfigureAwait(false);

        if (!header.AsSpan(0, 4).SequenceEqual(ResponseMagic))
        {
            throw new InvalidDataException("Invalid OCR response magic.");
        }

        ushort version =
            BinaryPrimitives.ReadUInt16BigEndian(
                header.AsSpan(4, 2));

        if (version != OcrTransportLimits.ProtocolVersion)
        {
            throw new InvalidDataException(
                "Unsupported OCR transport protocol version.");
        }

        OcrTransportOutcome outcome =
            (OcrTransportOutcome)
            BinaryPrimitives.ReadUInt16BigEndian(
                header.AsSpan(6, 2));

        OcrTransportReason reason =
            (OcrTransportReason)
            BinaryPrimitives.ReadUInt16BigEndian(
                header.AsSpan(8, 2));

        if (!Enum.IsDefined(outcome) ||
            !Enum.IsDefined(reason) ||
            BinaryPrimitives.ReadUInt16BigEndian(
                header.AsSpan(10, 2)) != 0)
        {
            throw new InvalidDataException(
                "OCR response metadata is invalid.");
        }

        long requestId =
            BinaryPrimitives.ReadInt64BigEndian(
                header.AsSpan(12, 8));
        long epochId =
            BinaryPrimitives.ReadInt64BigEndian(
                header.AsSpan(20, 8));
        int textCount =
            BinaryPrimitives.ReadInt32BigEndian(
                header.AsSpan(28, 4));

        if (requestId <= 0 ||
            epochId <= 0 ||
            textCount < 0 ||
            textCount > OcrTransportLimits.MaxRegions)
        {
            throw new InvalidDataException(
                "OCR response metadata is outside protocol limits.");
        }

        if (outcome == OcrTransportOutcome.Success &&
            (reason != OcrTransportReason.None ||
             textCount == 0))
        {
            throw new InvalidDataException(
                "Successful OCR response metadata is inconsistent.");
        }

        if (outcome != OcrTransportOutcome.Success &&
            textCount != 0)
        {
            throw new InvalidDataException(
                "Failed OCR responses must not carry text.");
        }

        List<OcrSensitiveUtf8Text> texts =
            new(textCount);

        long totalBytes = 0;

        try
        {
            for (int index = 0; index < textCount; index++)
            {
                byte[] textHeader =
                    new byte[ResponseTextHeaderBytes];

                await ReadExactlyAsync(
                    source,
                    textHeader,
                    cancellationToken).ConfigureAwait(false);

                int byteCount =
                    BinaryPrimitives.ReadInt32BigEndian(textHeader);

                if (byteCount < 0 ||
                    byteCount >
                        OcrTransportLimits.MaxRegionTextUtf8Bytes)
                {
                    throw new InvalidDataException(
                        "OCR response text length exceeds protocol limits.");
                }

                totalBytes =
                    checked(totalBytes + byteCount);

                if (totalBytes >
                    OcrTransportLimits.MaxResponseTextUtf8Bytes)
                {
                    throw new InvalidDataException(
                        "OCR response exceeds the total text byte limit.");
                }

                byte[] bytes = new byte[byteCount];

                try
                {
                    if (byteCount > 0)
                    {
                        await ReadExactlyAsync(
                            source,
                            bytes,
                            cancellationToken).ConfigureAwait(false);
                    }

                    texts.Add(
                        new OcrSensitiveUtf8Text(bytes));
                }
                catch
                {
                    Array.Clear(bytes);
                    throw;
                }
            }

            return new OcrTransportResponse(
                requestId,
                epochId,
                outcome,
                reason,
                texts);
        }
        catch
        {
            foreach (OcrSensitiveUtf8Text text in texts)
            {
                text.Dispose();
            }

            throw;
        }
    }

    private static async ValueTask ReadExactlyAsync(
        Stream source,
        Memory<byte> destination,
        CancellationToken cancellationToken)
    {
        int offset = 0;

        while (offset < destination.Length)
        {
            int read =
                await source.ReadAsync(
                    destination[offset..],
                    cancellationToken).ConfigureAwait(false);

            if (read == 0)
            {
                throw new EndOfStreamException(
                    "OCR transport frame ended unexpectedly.");
            }

            offset = checked(offset + read);
        }
    }

    private static void ValidateDeadline(
        DateTimeOffset deadlineUtc,
        DateTimeOffset utcNow)
    {
        DateTimeOffset now = utcNow.ToUniversalTime();
        DateTimeOffset deadline = deadlineUtc.ToUniversalTime();

        if (deadline <= now ||
            deadline - now > OcrTransportLimits.MaxRequestDeadline)
        {
            throw new InvalidDataException(
                "OCR request deadline is outside protocol limits.");
        }
    }

    private static void ValidateRegionMetadata(
        int width,
        int height,
        int byteCount)
    {
        if (width <= 0 ||
            height <= 0 ||
            width > OcrTransportLimits.MaxRegionDimension ||
            height > OcrTransportLimits.MaxRegionDimension ||
            checked((long)width * height) >
                OcrTransportLimits.MaxRegionPixels ||
            byteCount <= 0 ||
            byteCount > OcrTransportLimits.MaxEncodedRegionBytes)
        {
            throw new InvalidDataException(
                "OCR region metadata is outside protocol limits.");
        }
    }
}
