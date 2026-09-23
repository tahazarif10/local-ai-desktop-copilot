using Microsoft.Graphics.Canvas;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;

namespace LocalCopilot_App.Services;

internal sealed class OcrRegionCaptureResult : IDisposable
{
    private readonly ReadOnlyCollection<OcrTransportRegionPayload> _regions;
    private int _disposed;

    internal OcrRegionCaptureResult(
        int sourceWidth,
        int sourceHeight,
        IEnumerable<OcrTransportRegionPayload> regions,
        double frameMilliseconds,
        double cropMilliseconds,
        double totalMilliseconds)
    {
        if (sourceWidth <= 0)
            throw new ArgumentOutOfRangeException(nameof(sourceWidth));
        if (sourceHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(sourceHeight));
        ArgumentNullException.ThrowIfNull(regions);

        OcrTransportRegionPayload[] copied =
            System.Linq.Enumerable.ToArray(regions);

        if (copied.Length == 0)
            throw new ArgumentException("OCR capture requires at least one ROI.", nameof(regions));

        SourceWidth = sourceWidth;
        SourceHeight = sourceHeight;
        _regions = Array.AsReadOnly(copied);
        FrameMilliseconds = frameMilliseconds;
        CropMilliseconds = cropMilliseconds;
        TotalMilliseconds = totalMilliseconds;
    }

    public int SourceWidth { get; }
    public int SourceHeight { get; }
    public IReadOnlyList<OcrTransportRegionPayload> Regions => _regions;
    public double FrameMilliseconds { get; }
    public double CropMilliseconds { get; }
    public double TotalMilliseconds { get; }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        foreach (OcrTransportRegionPayload region in _regions)
            region.Dispose();

        GC.SuppressFinalize(this);
    }

    ~OcrRegionCaptureResult() => Dispose();
}

internal static class OcrRegionCaptureService
{
    public static async Task<OcrRegionCaptureResult> CaptureAsync(
        GraphicsCaptureItem item,
        OcrIntegrationRequest request,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(request);

        if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromSeconds(10))
            throw new ArgumentOutOfRangeException(nameof(timeout));

        using CanvasDevice canvasDevice = new();
        IDirect3DDevice direct3DDevice = canvasDevice;

        using Direct3D11CaptureFramePool framePool =
            Direct3D11CaptureFramePool.CreateFreeThreaded(
                direct3DDevice,
                DirectXPixelFormat.B8G8R8A8UIntNormalized,
                1,
                item.Size);

        using GraphicsCaptureSession session =
            framePool.CreateCaptureSession(item);

        TaskCompletionSource<Direct3D11CaptureFrame> completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        int frameAccepted = 0;
        Stopwatch total = Stopwatch.StartNew();

        void FrameArrived(
            Direct3D11CaptureFramePool sender,
            object args)
        {
            if (Interlocked.CompareExchange(ref frameAccepted, 1, 0) != 0)
                return;

            try
            {
                Direct3D11CaptureFrame? frame = sender.TryGetNextFrame();
                if (frame is null)
                {
                    Interlocked.Exchange(ref frameAccepted, 0);
                    return;
                }

                if (!completion.TrySetResult(frame))
                    frame.Dispose();
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        }

        framePool.FrameArrived += FrameArrived;

        try
        {
            session.StartCapture();

            using CancellationTokenSource timeoutCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken);
            timeoutCancellation.CancelAfter(timeout);

            Task cancellationTask =
                Task.Delay(
                    Timeout.InfiniteTimeSpan,
                    timeoutCancellation.Token);

            Task completed =
                await Task.WhenAny(
                        completion.Task,
                        cancellationTask)
                    .ConfigureAwait(false);

            if (completed != completion.Task)
            {
                cancellationToken.ThrowIfCancellationRequested();
                throw new TimeoutException("OCR ROI capture deadline expired.");
            }

            using Direct3D11CaptureFrame frame =
                await completion.Task.ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();

            double frameMilliseconds =
                total.Elapsed.TotalMilliseconds;

            int contentWidth = frame.ContentSize.Width;
            int contentHeight = frame.ContentSize.Height;

            if (contentWidth != request.SourceWidth ||
                contentHeight != request.SourceHeight)
            {
                throw new InvalidOperationException(
                    "Captured content size changed after ROI planning.");
            }

            using CanvasBitmap sourceBitmap =
                CanvasBitmap.CreateFromDirect3D11Surface(
                    canvasDevice,
                    frame.Surface);

            Stopwatch crop = Stopwatch.StartNew();
            List<OcrTransportRegionPayload> captured =
                new(request.RegionPlan.Regions.Count);

            try
            {
                foreach (PlannedCaptureRegion planned in request.RegionPlan.Regions)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    CaptureRegion bounds = planned.Bounds;

                    using CanvasRenderTarget target =
                        new(
                            canvasDevice,
                            bounds.Width,
                            bounds.Height,
                            96f);

                    using (CanvasDrawingSession drawing =
                        target.CreateDrawingSession())
                    {
                        drawing.DrawImage(
                            sourceBitmap,
                            new Rect(
                                0,
                                0,
                                bounds.Width,
                                bounds.Height),
                            new Rect(
                                bounds.X,
                                bounds.Y,
                                bounds.Width,
                                bounds.Height),
                            1.0f,
                            CanvasImageInterpolation.NearestNeighbor);
                    }

                    byte[] pixels = target.GetPixelBytes();
                    int expected = checked(bounds.Width * bounds.Height * 4);

                    if (pixels.Length != expected)
                    {
                        Array.Clear(pixels, 0, pixels.Length);
                        throw new InvalidOperationException(
                            "Unexpected OCR ROI BGRA byte count.");
                    }

                    captured.Add(
                        new OcrTransportRegionPayload(
                            bounds,
                            checked(bounds.Width * 4),
                            OcrTransportPixelFormat.Bgra8,
                            pixels));
                }

                crop.Stop();
                total.Stop();

                return new OcrRegionCaptureResult(
                    contentWidth,
                    contentHeight,
                    captured,
                    frameMilliseconds,
                    crop.Elapsed.TotalMilliseconds,
                    total.Elapsed.TotalMilliseconds);
            }
            catch
            {
                foreach (OcrTransportRegionPayload region in captured)
                    region.Dispose();
                throw;
            }
        }
        finally
        {
            framePool.FrameArrived -= FrameArrived;
        }
    }
}
