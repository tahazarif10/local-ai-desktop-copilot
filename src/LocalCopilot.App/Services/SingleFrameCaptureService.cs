using Microsoft.Graphics.Canvas;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;

namespace LocalCopilot_App.Services;

public sealed record SingleFrameCaptureInfo(
    int ContentWidth,
    int ContentHeight,
    int SurfaceWidth,
    int SurfaceHeight,
    int OutputWidth,
    int OutputHeight,
    double ScaleFactor,
    DirectXPixelFormat SurfacePixelFormat,
    BitmapPixelFormat BitmapPixelFormat,
    BitmapAlphaMode BitmapAlphaMode,
    int PlaneStride,
    long CpuBytes,
    double FrameMilliseconds,
    double ResizeMilliseconds,
    double CopyMilliseconds,
    double TotalMilliseconds);

public static class SingleFrameCaptureService
{
    public static async Task<SingleFrameCaptureInfo> CaptureAsync(
        GraphicsCaptureItem item,
        TimeSpan timeout,
        int maxOutputWidth = 2560)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (maxOutputWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxOutputWidth));
        }

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
            framePool.CreateCaptureSession(item);

        TaskCompletionSource<Direct3D11CaptureFrame> completion =
            new(
                TaskCreationOptions.RunContinuationsAsynchronously);

        int frameAccepted = 0;

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

                if (!completion.TrySetResult(frame))
                {
                    frame.Dispose();
                }
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        }

        framePool.FrameArrived += FrameArrived;

        try
        {
            session.StartCapture();

            Task completed =
                await Task.WhenAny(
                    completion.Task,
                    Task.Delay(timeout));

            if (completed != completion.Task)
            {
                throw new TimeoutException(
                    $"No capture frame arrived within " +
                    $"{timeout.TotalSeconds:0.0} seconds.");
            }

            using Direct3D11CaptureFrame frame =
                await completion.Task;

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

            double scaleFactor =
                contentWidth > maxOutputWidth
                    ? maxOutputWidth /
                        (double)contentWidth
                    : 1.0;

            int outputWidth =
                Math.Max(
                    1,
                    (int)Math.Round(
                        contentWidth * scaleFactor));

            int outputHeight =
                Math.Max(
                    1,
                    (int)Math.Round(
                        contentHeight * scaleFactor));

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

            using (CanvasDrawingSession drawingSession =
                renderTarget.CreateDrawingSession())
            {
                Rect sourceRect =
                    new(
                        0,
                        0,
                        contentWidth,
                        contentHeight);

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

            Stopwatch copyStopwatch =
                Stopwatch.StartNew();

            using SoftwareBitmap softwareBitmap =
                await SoftwareBitmap.CreateCopyFromSurfaceAsync(
                    renderTarget);

            copyStopwatch.Stop();

            using BitmapBuffer bitmapBuffer =
                softwareBitmap.LockBuffer(
                    BitmapBufferAccessMode.Read);

            BitmapPlaneDescription plane =
                bitmapBuffer.GetPlaneDescription(0);

            long cpuBytes =
                Math.Abs((long)plane.Stride) *
                plane.Height;

            totalStopwatch.Stop();

            return new SingleFrameCaptureInfo(
                contentWidth,
                contentHeight,
                surfaceDescription.Width,
                surfaceDescription.Height,
                outputWidth,
                outputHeight,
                scaleFactor,
                surfaceDescription.Format,
                softwareBitmap.BitmapPixelFormat,
                softwareBitmap.BitmapAlphaMode,
                plane.Stride,
                cpuBytes,
                frameMilliseconds,
                resizeStopwatch.Elapsed.TotalMilliseconds,
                copyStopwatch.Elapsed.TotalMilliseconds,
                totalStopwatch.Elapsed.TotalMilliseconds);
        }
        finally
        {
            framePool.FrameArrived -= FrameArrived;
        }
    }
}