using LocalCopilot_App.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace LocalCopilot.Core.Tests;

[TestClass]
public sealed class OcrTransportProtocolTests
{
    [TestMethod]
    public async Task Request_RoundTripsBoundedMetadataAndPngBytes()
    {
        DateTimeOffset now =
            new(2026, 9, 23, 18, 30, 0, TimeSpan.Zero);

        byte[] sourcePng = FakePng(320, 120, extraBytes: 32);

        using OcrTransportRequest request =
            new(
                requestId: 41,
                epochId: 9,
                deadlineUtc: now.AddSeconds(5),
                regions:
                [
                    new OcrEncodedRegion(
                        320,
                        120,
                        sourcePng.ToArray())
                ]);

        using MemoryStream encoded = new();

        await OcrTransportCodec.WriteRequestAsync(
            encoded,
            request,
            now,
            CancellationToken.None);

        encoded.Position = 0;

        using OcrTransportRequest decoded =
            await OcrTransportCodec.ReadRequestAsync(
                encoded,
                now,
                CancellationToken.None);

        Assert.AreEqual(41L, decoded.RequestId);
        Assert.AreEqual(9L, decoded.EpochId);
        Assert.AreEqual(1, decoded.Regions.Count);
        Assert.AreEqual(320, decoded.Regions[0].Width);
        Assert.AreEqual(120, decoded.Regions[0].Height);
        CollectionAssert.AreEqual(
            sourcePng,
            decoded.Regions[0].PngBytes.ToArray());

        StringAssert.Contains(
            decoded.ToString(),
            "content=redacted");
    }

    [TestMethod]
    public void Request_DisposeZerosOwnedPngBytes()
    {
        byte[] owned =
            FakePng(64, 32, extraBytes: 8);

        OcrEncodedRegion region =
            new(
                64,
                32,
                owned);

        OcrTransportRequest request =
            new(
                requestId: 1,
                epochId: 2,
                deadlineUtc: DateTimeOffset.UtcNow.AddSeconds(5),
                regions: [region]);

        request.Dispose();

        Assert.IsTrue(region.IsDisposed);
        Assert.IsTrue(owned.All(value => value == 0));
    }

    [TestMethod]
    public async Task RequestDecode_RejectsUnsupportedVersion()
    {
        DateTimeOffset now =
            new(2026, 9, 23, 18, 30, 0, TimeSpan.Zero);

        byte[] encoded =
            await EncodeValidRequestAsync(now);

        BinaryPrimitives.WriteUInt16BigEndian(
            encoded.AsSpan(4, 2),
            checked((ushort)(
                OcrTransportLimits.ProtocolVersion + 1)));

        using MemoryStream stream =
            new(encoded, writable: false);

        bool rejected = false;

        try
        {
            using OcrTransportRequest _ =
                await OcrTransportCodec.ReadRequestAsync(
                    stream,
                    now,
                    CancellationToken.None);
        }
        catch (InvalidDataException)
        {
            rejected = true;
        }

        Assert.IsTrue(rejected);
    }

    [TestMethod]
    public async Task RequestDecode_RejectsOversizeDeclaredRegionBeforePayload()
    {
        DateTimeOffset now =
            new(2026, 9, 23, 18, 30, 0, TimeSpan.Zero);

        byte[] encoded =
            await EncodeValidRequestAsync(now);

        BinaryPrimitives.WriteInt32BigEndian(
            encoded.AsSpan(44, 4),
            checked(
                OcrTransportLimits.MaxEncodedRegionBytes + 1));

        using MemoryStream stream =
            new(encoded.AsMemory(0, 48).ToArray(), writable: false);

        bool rejected = false;

        try
        {
            using OcrTransportRequest _ =
                await OcrTransportCodec.ReadRequestAsync(
                    stream,
                    now,
                    CancellationToken.None);
        }
        catch (InvalidDataException)
        {
            rejected = true;
        }

        Assert.IsTrue(rejected);
    }

    [TestMethod]
    public async Task RequestDecode_RejectsExpiredDeadline()
    {
        DateTimeOffset now =
            new(2026, 9, 23, 18, 30, 0, TimeSpan.Zero);

        byte[] encoded =
            await EncodeValidRequestAsync(now);

        BinaryPrimitives.WriteInt64BigEndian(
            encoded.AsSpan(24, 8),
            now.AddMilliseconds(-1).ToUnixTimeMilliseconds());

        using MemoryStream stream =
            new(encoded, writable: false);

        bool rejected = false;

        try
        {
            using OcrTransportRequest _ =
                await OcrTransportCodec.ReadRequestAsync(
                    stream,
                    now,
                    CancellationToken.None);
        }
        catch (InvalidDataException)
        {
            rejected = true;
        }

        Assert.IsTrue(rejected);
    }

    [TestMethod]
    public async Task RequestDecode_PreCancelledTokenReturnsNoPartialPayload()
    {
        DateTimeOffset now =
            new(2026, 9, 23, 18, 30, 0, TimeSpan.Zero);

        byte[] encoded =
            await EncodeValidRequestAsync(now);

        using MemoryStream stream =
            new(encoded, writable: false);

        using CancellationTokenSource cancellation =
            new();

        cancellation.Cancel();

        bool cancelled = false;

        try
        {
            using OcrTransportRequest _ =
                await OcrTransportCodec.ReadRequestAsync(
                    stream,
                    now,
                    cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }

        Assert.IsTrue(cancelled);
    }

    [TestMethod]
    public async Task Response_RoundTripsBoundedSensitiveUtf8()
    {
        byte[] sourceText =
            Encoding.UTF8.GetBytes("bounded ocr result");

        using OcrTransportResponse response =
            new(
                requestId: 41,
                epochId: 9,
                OcrTransportOutcome.Success,
                OcrTransportReason.None,
                [
                    new OcrSensitiveUtf8Text(
                        sourceText.ToArray())
                ]);

        using MemoryStream encoded = new();

        await OcrTransportCodec.WriteResponseAsync(
            encoded,
            response,
            CancellationToken.None);

        encoded.Position = 0;

        using OcrTransportResponse decoded =
            await OcrTransportCodec.ReadResponseAsync(
                encoded,
                CancellationToken.None);

        Assert.AreEqual(
            OcrTransportOutcome.Success,
            decoded.Outcome);
        Assert.AreEqual(
            OcrTransportReason.None,
            decoded.Reason);
        Assert.AreEqual(1, decoded.Texts.Count);
        CollectionAssert.AreEqual(
            sourceText,
            decoded.Texts[0].Utf8Bytes.ToArray());

        Assert.IsFalse(
            decoded.ToString().Contains(
                "bounded ocr result",
                StringComparison.Ordinal));
        StringAssert.Contains(
            decoded.ToString(),
            "content=redacted");
    }

    [TestMethod]
    public void Response_DisposeZerosOwnedTextBytes()
    {
        byte[] owned =
            Encoding.UTF8.GetBytes("sensitive result");

        OcrSensitiveUtf8Text text =
            new(owned);

        OcrTransportResponse response =
            new(
                requestId: 1,
                epochId: 2,
                OcrTransportOutcome.Success,
                OcrTransportReason.None,
                [text]);

        response.Dispose();

        Assert.IsTrue(text.IsDisposed);
        Assert.IsTrue(owned.All(value => value == 0));
    }

    [TestMethod]
    public void Region_RejectsNonPngOrMismatchedDimensions()
    {
        byte[] png =
            FakePng(100, 50, extraBytes: 4);

        Assert.ThrowsExactly<ArgumentException>(
            () => new OcrEncodedRegion(
                100,
                51,
                png.ToArray()));

        Assert.ThrowsExactly<ArgumentException>(
            () => new OcrEncodedRegion(
                100,
                50,
                new byte[24]));
    }

    [TestMethod]
    public async Task ResponseDecode_RejectsOversizeDeclaredText()
    {
        using OcrTransportResponse response =
            new(
                requestId: 7,
                epochId: 8,
                OcrTransportOutcome.Success,
                OcrTransportReason.None,
                [
                    new OcrSensitiveUtf8Text(
                        Encoding.UTF8.GetBytes("ok"))
                ]);

        using MemoryStream encoded = new();

        await OcrTransportCodec.WriteResponseAsync(
            encoded,
            response,
            CancellationToken.None);

        byte[] bytes = encoded.ToArray();

        BinaryPrimitives.WriteInt32BigEndian(
            bytes.AsSpan(32, 4),
            checked(
                OcrTransportLimits.MaxRegionTextUtf8Bytes + 1));

        using MemoryStream malformed =
            new(bytes.AsMemory(0, 36).ToArray(), writable: false);

        bool rejected = false;

        try
        {
            using OcrTransportResponse _ =
                await OcrTransportCodec.ReadResponseAsync(
                    malformed,
                    CancellationToken.None);
        }
        catch (InvalidDataException)
        {
            rejected = true;
        }

        Assert.IsTrue(rejected);
    }

    private static async Task<byte[]> EncodeValidRequestAsync(
        DateTimeOffset now)
    {
        using OcrTransportRequest request =
            new(
                requestId: 41,
                epochId: 9,
                deadlineUtc: now.AddSeconds(5),
                regions:
                [
                    new OcrEncodedRegion(
                        320,
                        120,
                        FakePng(
                            320,
                            120,
                            extraBytes: 16))
                ]);

        using MemoryStream encoded = new();

        await OcrTransportCodec.WriteRequestAsync(
            encoded,
            request,
            now,
            CancellationToken.None);

        return encoded.ToArray();
    }

    private static byte[] FakePng(
        int width,
        int height,
        int extraBytes)
    {
        byte[] bytes =
            new byte[checked(24 + extraBytes)];

        ReadOnlySpan<byte> signature =
            [137, 80, 78, 71, 13, 10, 26, 10];

        signature.CopyTo(bytes);
        BinaryPrimitives.WriteUInt32BigEndian(
            bytes.AsSpan(8, 4),
            13);
        "IHDR"u8.CopyTo(bytes.AsSpan(12, 4));
        BinaryPrimitives.WriteInt32BigEndian(
            bytes.AsSpan(16, 4),
            width);
        BinaryPrimitives.WriteInt32BigEndian(
            bytes.AsSpan(20, 4),
            height);

        return bytes;
    }
}
