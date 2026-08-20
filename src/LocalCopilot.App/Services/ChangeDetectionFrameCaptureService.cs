using Microsoft.Graphics.Canvas;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;

namespace LocalCopilot_App.Services;

public static class ChangeDetectionFrameCaptureService
{
    public static async Task<ChangeDetectionCaptureFrame>
        CaptureAsync(
            GraphicsCaptureItem item,
            TimeSpan timeout,
            int maxOutputWidth,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(
            item);

        if (maxOutputWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxOutputWidth));
        }

        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeout));
        }

        cancellationToken.ThrowIfCancellationRequested();

        using CanvasDevice canvasDevice =
            new();

        IDirect3DDevice direct3DDevice =
            canvasDevice;

        using Direct3D11CaptureFramePool framePool =
            Direct3D11CaptureFramePool.CreateFreeThreaded(
                direct3DDevice,
                DirectXPixelFormat.B8G8R8A8UIntNormalized,
                1,
                item.Size);

        using GraphicsCaptureSession session =
            framePool.CreateCaptureSession(
                item);

        if (OperatingSystem.IsWindowsVersionAtLeast(
                10,
                0,
                19041))
        {
            session.IsCursorCaptureEnabled =
                false;
        }

        TaskCompletionSource<Direct3D11CaptureFrame> completion =
            new(
                TaskCreationOptions.RunContinuationsAsynchronously);

        int frameAccepted =
            0;

        Stopwatch totalStopwatch =
            Stopwatch.StartNew();

        void FrameArrived(
            Direct3D11CaptureFramePool sender,
            object args)
        {
            if (Interlocked.CompareExchange(
                    ref frameAccepted,
                    1,
                    0) != 0)
            {
                return;
            }

            try
            {
                Direct3D11CaptureFrame? frame =
                    sender.TryGetNextFrame();

                if (frame is null)
                {
                    Interlocked.Exchange(
                        ref frameAccepted,
                        0);

                    return;
                }

                if (!completion.TrySetResult(
                        frame))
                {
                    frame.Dispose();
                }
            }
            catch (Exception ex)
            {
                completion.TrySetException(
                    ex);
            }
        }

        void ItemClosed(
            GraphicsCaptureItem sender,
            object args)
        {
            completion.TrySetException(
                new InvalidOperationException(
                    "Capture target closed before a frame arrived."));
        }

        framePool.FrameArrived +=
            FrameArrived;

        item.Closed +=
            ItemClosed;

        using CancellationTokenSource linkedCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);

        linkedCancellation.CancelAfter(
            timeout);

        using CancellationTokenRegistration registration =
            linkedCancellation.Token.Register(
                () =>
                {
                    completion.TrySetCanceled(
                        linkedCancellation.Token);
                });

        try
        {
            session.StartCapture();

            Direct3D11CaptureFrame frame;

            try
            {
                frame =
                    await completion.Task.ConfigureAwait(
                        false);
            }
            catch (OperationCanceledException)
                when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException(
                    $"No capture frame arrived within " +
                    $"{timeout.TotalSeconds:0.0} seconds.");
            }

            using (frame)
            {
                cancellationToken.ThrowIfCancellationRequested();

                double frameMilliseconds =
                    totalStopwatch.Elapsed.TotalMilliseconds;

                int contentWidth =
                    frame.ContentSize.Width;

                int contentHeight =
                    frame.ContentSize.Height;

                if (contentWidth <= 0 ||
                    contentHeight <= 0)
                {
                    throw new InvalidOperationException(
                        "Captured frame has invalid ContentSize.");
                }

                var surfaceDescription =
                    frame.Surface.Description;

                int sourceWidth =
                    Math.Min(
                        contentWidth,
                        surfaceDescription.Width);

                int sourceHeight =
                    Math.Min(
                        contentHeight,
                        surfaceDescription.Height);

                if (sourceWidth <= 0 ||
                    sourceHeight <= 0)
                {
                    throw new InvalidOperationException(
                        "Captured frame has invalid source dimensions.");
                }

                double scaleFactor =
                    sourceWidth > maxOutputWidth
                        ? maxOutputWidth /
                            (double)sourceWidth
                        : 1.0;

                int outputWidth =
                    Math.Max(
                        1,
                        (int)Math.Round(
                            sourceWidth *
                            scaleFactor));

                int outputHeight =
                    Math.Max(
                        1,
                        (int)Math.Round(
                            sourceHeight *
                            scaleFactor));

                Stopwatch resizeStopwatch =
                    Stopwatch.StartNew();

                using CanvasBitmap sourceBitmap =
                    CanvasBitmap.CreateFromDirect3D11Surface(
                        canvasDevice,
                        frame.Surface);

                using CanvasRenderTarget renderTarget =
                    new(
                        canvasDevice,
                        outputWidth,
                        outputHeight,
                        96f);

                using (
                    CanvasDrawingSession drawingSession =
                        renderTarget.CreateDrawingSession())
                {
                    Rect sourceRect =
                        new(
                            0,
                            0,
                            sourceWidth,
                            sourceHeight);

                    Rect destinationRect =
                        new(
                            0,
                            0,
                            outputWidth,
                            outputHeight);

                    drawingSession.DrawImage(
                        sourceBitmap,
                        destinationRect,
                        sourceRect,
                        1.0f,
                        CanvasImageInterpolation.Linear);
                }

                resizeStopwatch.Stop();

                cancellationToken.ThrowIfCancellationRequested();

                Stopwatch readbackStopwatch =
                    Stopwatch.StartNew();

                byte[] bgra =
                    renderTarget.GetPixelBytes();

                readbackStopwatch.Stop();

                int pixelCount =
                    checked(
                        outputWidth *
                        outputHeight);

                int expectedBgraLength =
                    checked(
                        pixelCount * 4);

                if (bgra.Length <
                    expectedBgraLength)
                {
                    throw new InvalidOperationException(
                        $"Unexpected BGRA buffer size. " +
                        $"Expected at least {expectedBgraLength}, " +
                        $"received {bgra.Length}.");
                }

                Stopwatch luminanceStopwatch =
                    Stopwatch.StartNew();

                byte[] luminance =
                    new byte[pixelCount];

                int sourceIndex =
                    0;

                for (int pixelIndex = 0;
                    pixelIndex < pixelCount;
                    pixelIndex++)
                {
                    int blue =
                        bgra[sourceIndex];

                    int green =
                        bgra[sourceIndex + 1];

                    int red =
                        bgra[sourceIndex + 2];

                    // Integer BT.601-style luma approximation.
                    // BGRA input:
                    // Y ~= 0.299R + 0.587G + 0.114B
                    luminance[pixelIndex] =
                        (byte)(
                            (
                                77 * red +
                                150 * green +
                                29 * blue +
                                128
                            ) >> 8);

                    sourceIndex +=
                        4;
                }

                luminanceStopwatch.Stop();

                totalStopwatch.Stop();

                return new ChangeDetectionCaptureFrame(
                    contentWidth,
                    contentHeight,
                    surfaceDescription.Width,
                    surfaceDescription.Height,
                    sourceWidth,
                    sourceHeight,
                    outputWidth,
                    outputHeight,
                    scaleFactor,
                    bgra.LongLength,
                    luminance.LongLength,
                    frameMilliseconds,
                    resizeStopwatch.Elapsed.TotalMilliseconds,
                    readbackStopwatch.Elapsed.TotalMilliseconds,
                    luminanceStopwatch.Elapsed.TotalMilliseconds,
                    totalStopwatch.Elapsed.TotalMilliseconds,
                    luminance);
            }
        }
        finally
        {
            item.Closed -=
                ItemClosed;

            framePool.FrameArrived -=
                FrameArrived;
        }
    }
}