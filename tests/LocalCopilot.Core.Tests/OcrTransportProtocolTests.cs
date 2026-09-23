using LocalCopilot_App.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Buffers.Binary;
using System.Linq;
using System.Text;

namespace LocalCopilot.Core.Tests;

[TestClass]
public sealed class OcrTransportProtocolTests
{
    [TestMethod]
    public void SerializeRequest_IsDeterministicAndBounded()
    {
        byte[] pixels = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
        using OcrTransportRequestPayload request =
            new(
                requestId: 11,
                epochId: 22,
                deadline: DateTimeOffset.FromUnixTimeMilliseconds(2_000_000),
                regions: new[]
                {
                    new OcrTransportRegionPayload(
                        new CaptureRegion(10, 20, 4, 2),
                        strideBytes: 16,
                        OcrTransportPixelFormat.Bgra8,
                        pixels)
                });

        byte[] body =
            OcrTransportProtocol.SerializeRequest(
                request,
                DateTimeOffset.FromUnixTimeMilliseconds(1_995_000));

        try
        {
            Assert.AreEqual("LCOPROC1", Encoding.ASCII.GetString(body, 0, 8));
            Assert.AreEqual(
                OcrTransportProtocol.Version,
                BinaryPrimitives.ReadUInt16BigEndian(body.AsSpan(8, 2)));
            Assert.AreEqual(11L, BinaryPrimitives.ReadInt64BigEndian(body.AsSpan(12, 8)));
            Assert.AreEqual(22L, BinaryPrimitives.ReadInt64BigEndian(body.AsSpan(20, 8)));
            Assert.AreEqual(1, BinaryPrimitives.ReadInt32BigEndian(body.AsSpan(36, 4)));
            CollectionAssert.AreEqual(
                pixels,
                body.AsSpan(body.Length - pixels.Length).ToArray());
        }
        finally
        {
            Array.Clear(body, 0, body.Length);
        }
    }

    [TestMethod]
    public void SerializeRequest_RejectsExpiredAndOversizedPayloads()
    {
        using OcrTransportRequestPayload expired =
            Request(
                deadline: DateTimeOffset.FromUnixTimeSeconds(100),
                byteCount: 32);

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => OcrTransportProtocol.SerializeRequest(
                expired,
                DateTimeOffset.FromUnixTimeSeconds(101)));

        OcrTransportLimits tiny =
            OcrTransportLimits.M4_2_3Default with
            {
                MaxRequestBytes = 1024
            };

        using OcrTransportRequestPayload oversized =
            Request(
                deadline: DateTimeOffset.FromUnixTimeSeconds(110),
                byteCount: 1200);

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => OcrTransportProtocol.SerializeRequest(
                oversized,
                DateTimeOffset.FromUnixTimeSeconds(100),
                tiny));
    }

    [TestMethod]
    public void RequestDispose_ZeroesOwnedPixelBuffers()
    {
        byte[] pixels = Enumerable.Repeat((byte)0xA5, 32).ToArray();
        OcrTransportRegionPayload region =
            new(
                new CaptureRegion(0, 0, 4, 2),
                16,
                OcrTransportPixelFormat.Bgra8,
                pixels);

        OcrTransportRequestPayload request =
            new(
                1,
                2,
                DateTimeOffset.UtcNow.AddSeconds(5),
                new[] { region });

        request.Dispose();

        Assert.IsTrue(region.IsDisposed);
        Assert.IsTrue(pixels.All(value => value == 0));
    }

    [TestMethod]
    public void DeserializeResponse_ProducesClearableSensitiveText()
    {
        byte[] textBytes = Encoding.UTF8.GetBytes("خطا Error");
        byte[] body = new byte[32 + 8 + textBytes.Length];

        Encoding.ASCII.GetBytes("LCOPRS01").CopyTo(body, 0);
        BinaryPrimitives.WriteUInt16BigEndian(body.AsSpan(8, 2), 1);
        BinaryPrimitives.WriteUInt16BigEndian(body.AsSpan(10, 2), 0);
        BinaryPrimitives.WriteInt64BigEndian(body.AsSpan(12, 8), 5);
        BinaryPrimitives.WriteInt64BigEndian(body.AsSpan(20, 8), 7);
        BinaryPrimitives.WriteInt32BigEndian(body.AsSpan(28, 4), 1);
        BinaryPrimitives.WriteInt32BigEndian(body.AsSpan(32, 4), 0);
        BinaryPrimitives.WriteInt32BigEndian(body.AsSpan(36, 4), textBytes.Length);
        textBytes.CopyTo(body, 40);

        using OcrTransportResponsePayload response =
            OcrTransportProtocol.DeserializeResponse(body);

        Assert.AreEqual(OcrTransportResponseStatus.Ok, response.Status);
        Assert.AreEqual(5L, response.RequestId);
        Assert.AreEqual(7L, response.EpochId);
        Assert.AreEqual(1, response.Texts.Count);
        Assert.AreEqual(
            "خطا Error",
            new string(response.Texts[0].Text.Characters.Span));

        OcrSensitiveText sensitive = response.Texts[0].Text;
        response.Dispose();
        Assert.IsTrue(sensitive.IsDisposed);
    }

    [TestMethod]
    public void DeserializeResponse_RejectsTrailingOrOversizedText()
    {
        byte[] body = EmptyResponse(1, 2);
        Array.Resize(ref body, body.Length + 1);

        Assert.ThrowsExactly<FormatException>(
            () => OcrTransportProtocol.DeserializeResponse(body));

        OcrTransportLimits tinyText =
            OcrTransportLimits.M4_2_3Default with
            {
                MaxTextUtf8BytesPerRegion = 4
            };

        byte[] text = Encoding.UTF8.GetBytes("12345");
        byte[] response = new byte[32 + 8 + text.Length];
        Encoding.ASCII.GetBytes("LCOPRS01").CopyTo(response, 0);
        BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(8, 2), 1);
        BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(10, 2), 0);
        BinaryPrimitives.WriteInt64BigEndian(response.AsSpan(12, 8), 1);
        BinaryPrimitives.WriteInt64BigEndian(response.AsSpan(20, 8), 2);
        BinaryPrimitives.WriteInt32BigEndian(response.AsSpan(28, 4), 1);
        BinaryPrimitives.WriteInt32BigEndian(response.AsSpan(32, 4), 0);
        BinaryPrimitives.WriteInt32BigEndian(response.AsSpan(36, 4), text.Length);
        text.CopyTo(response, 40);

        Assert.ThrowsExactly<FormatException>(
            () => OcrTransportProtocol.DeserializeResponse(response, tinyText));
    }

    [TestMethod]
    public void RequestSignature_IsStableAndBindsBodyHash()
    {
        byte[] key = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();
        string first =
            OcrTransportProtocol.ComputeRequestSignatureBase64(
                key,
                1_700_000_000,
                "AABB",
                new string('1', 64));

        string second =
            OcrTransportProtocol.ComputeRequestSignatureBase64(
                key,
                1_700_000_000,
                "aabb",
                new string('1', 64));

        string changed =
            OcrTransportProtocol.ComputeRequestSignatureBase64(
                key,
                1_700_000_000,
                "aabb",
                new string('2', 64));

        Assert.AreEqual(first, second);
        Assert.AreNotEqual(first, changed);
    }

    [TestMethod]
    public void LimitsRejectUnknownOrUnboundedSettings()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => (OcrTransportLimits.M4_2_3Default with
            {
                MaxRegions = RegionOfInterestPlannerOptions.HardMaxRegions + 1
            }).Validate());

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => (OcrTransportLimits.M4_2_3Default with
            {
                MaxDeadline = TimeSpan.FromMinutes(1)
            }).Validate());
    }

    private static OcrTransportRequestPayload Request(
        DateTimeOffset deadline,
        int byteCount)
    {
        int width = byteCount / 4;
        return new OcrTransportRequestPayload(
            1,
            2,
            deadline,
            new[]
            {
                new OcrTransportRegionPayload(
                    new CaptureRegion(0, 0, width, 1),
                    byteCount,
                    OcrTransportPixelFormat.Bgra8,
                    Enumerable.Repeat((byte)0x5A, byteCount).ToArray())
            });
    }

    private static byte[] EmptyResponse(long requestId, long epochId)
    {
        byte[] body = new byte[32];
        Encoding.ASCII.GetBytes("LCOPRS01").CopyTo(body, 0);
        BinaryPrimitives.WriteUInt16BigEndian(body.AsSpan(8, 2), 1);
        BinaryPrimitives.WriteUInt16BigEndian(body.AsSpan(10, 2), 0);
        BinaryPrimitives.WriteInt64BigEndian(body.AsSpan(12, 8), requestId);
        BinaryPrimitives.WriteInt64BigEndian(body.AsSpan(20, 8), epochId);
        BinaryPrimitives.WriteInt32BigEndian(body.AsSpan(28, 4), 0);
        return body;
    }
}
