using LocalCopilot_App.Diagnostics;
using Microsoft.Graphics.Canvas;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;

namespace LocalCopilot_App.Services;

public sealed class PersistentChangeDetectionService
{
    private const int
        FrameBufferCount =
            2;

    private readonly object
        _lifecycleGate =
            new();

    private readonly IForegroundWindowIdentityValidator
        _identityValidator;

    private PersistentRunState?
        _active;

    public PersistentChangeDetectionService(
        IForegroundWindowIdentityValidator identityValidator)
    {
        _identityValidator =
            identityValidator ??
            throw new ArgumentNullException(
                nameof(identityValidator));
    }

    public event Action<PersistentChangeSample>?
        SampleReady;

    public event Action<PersistentChangeSessionEnded>?
        SessionEnded;

    public bool HasActiveSession
    {
        get
        {
            lock (_lifecycleGate)
            {
                return
                    _active is not null;
            }
        }
    }

    public bool IsRunning
    {
        get
        {
            lock (_lifecycleGate)
            {
                return
                    _active is not null &&
                    !_active.Cancellation
                        .IsCancellationRequested;
            }
        }
    }

    public void Start(
        ContextEpoch epoch,
        int profileWidth,
        TimeSpan sampleInterval)
    {
        ArgumentNullException.ThrowIfNull(
            epoch);

        if (profileWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(profileWidth));
        }

        if (sampleInterval <=
            TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sampleInterval));
        }

        if (!epoch.Privacy.Allows(
                PrivacyCapability.CapturePixels))
        {
            throw new UnauthorizedAccessException(
                "Privacy policy blocks persistent sensing.");
        }

        epoch.CancellationToken
            .ThrowIfCancellationRequested();

        if (!_identityValidator.IsCurrent(
                epoch.Snapshot))
        {
            throw new InvalidOperationException(
                "Capture target identity changed.");
        }

        if (!GraphicsCaptureSession.IsSupported())
        {
            throw new InvalidOperationException(
                "Windows Graphics Capture is not supported.");
        }

        lock (_lifecycleGate)
        {
            if (_active is not null)
            {
                throw new InvalidOperationException(
                    "Persistent change detection is already running.");
            }
        }

        GraphicsCaptureItem item =
            GraphicsCaptureItemFactory.CreateForWindow(
                epoch.Snapshot.Handle);

        CanvasDevice canvasDevice =
            new();

        IDirect3DDevice direct3DDevice =
            canvasDevice;

        Direct3D11CaptureFramePool framePool =
            Direct3D11CaptureFramePool.CreateFreeThreaded(
                direct3DDevice,
                DirectXPixelFormat.B8G8R8A8UIntNormalized,
                FrameBufferCount,
                item.Size);

        GraphicsCaptureSession session =
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

        PersistentRunState state =
            new(
                epoch,
                profileWidth,
                sampleInterval,
                item,
                canvasDevice,
                framePool,
                session,
                item.Size);

        state.FrameArrivedHandler =
            (sender, args) =>
                HandleFrameArrived(
                    state,
                    sender);

        state.ItemClosedHandler =
            (sender, args) =>
                RequestStop(
                    state,
                    "item_closed");

        try
        {
            state.EpochRegistration =
                epoch.CancellationToken.Register(
                    () =>
                    {
                        RequestStop(
                            state,
                            "context_cancelled");
                    });

            state.Cancellation.Token
                .ThrowIfCancellationRequested();

            framePool.FrameArrived +=
                state.FrameArrivedHandler;

            item.Closed +=
                state.ItemClosedHandler;

            lock (_lifecycleGate)
            {
                if (_active is not null)
                {
                    throw new InvalidOperationException(
                        "Persistent change detection became active concurrently.");
                }

                _active =
                    state;
            }

            session.StartCapture();

            DiagnosticLog.Write(
                "PERSIST.SESSION_START",
                $"epoch={epoch.Id} " +
                $"hwnd=0x{epoch.Snapshot.Handle.ToInt64():X} " +
                $"pid={epoch.Snapshot.ProcessId} " +
                $"process={epoch.Snapshot.ProcessName} " +
                $"profile={profileWidth} " +
                $"cadenceMs={sampleInterval.TotalMilliseconds:0} " +
                $"pool={item.Size.Width}x{item.Size.Height} " +
                $"buffers={FrameBufferCount}");

            state.WorkerTask =
                RunWorkerAsync(
                    state);
        }
        catch
        {
            lock (_lifecycleGate)
            {
                if (ReferenceEquals(
                        _active,
                        state))
                {
                    _active =
                        null;
                }
            }

            state.DisposeResources();

            throw;
        }
    }

    public void Stop(
        string reason)
    {
        if (string.IsNullOrWhiteSpace(
                reason))
        {
            reason =
                "stop_requested";
        }

        PersistentRunState? state;

        lock (_lifecycleGate)
        {
            state =
                _active;
        }

        if (state is null)
        {
            DiagnosticLog.Write(
                "PERSIST.STOP_NOOP",
                $"reason={reason}");

            return;
        }

        DiagnosticLog.Write(
            "PERSIST.STOP_REQUEST",
            $"epoch={state.Epoch.Id} " +
            $"reason={reason}");

        RequestStop(
            state,
            reason);

        Task? worker =
            state.WorkerTask;

        if (worker is null)
            return;

        worker
            .GetAwaiter()
            .GetResult();
    }

    private async Task RunWorkerAsync(
        PersistentRunState state)
    {
        string? errorType =
            null;

        string? errorMessage =
            null;

        bool hadError =
            false;

        try
        {
            using PeriodicTimer timer =
                new(
                    state.SampleInterval);

            while (
                await timer.WaitForNextTickAsync(
                        state.Cancellation.Token)
                    .ConfigureAwait(false))
            {
                state.Cancellation.Token
                    .ThrowIfCancellationRequested();

                Direct3D11CaptureFrame? frame =
                    TakeLatestFrame(
                        state);

                if (frame is null)
                    continue;

                SizeInt32 contentSize =
                    frame.ContentSize;

                if (contentSize.Width <= 0 ||
                    contentSize.Height <= 0)
                {
                    frame.Dispose();

                    throw new InvalidOperationException(
                        "Persistent capture returned invalid ContentSize.");
                }

                if (
                    contentSize.Width !=
                        state.PoolSize.Width ||
                    contentSize.Height !=
                        state.PoolSize.Height)
                {
                    frame.Dispose();

                    RecreateFramePool(
                        state,
                        contentSize);

                    continue;
                }

                PersistentChangeSample sample;

                using (frame)
                {
                    sample =
                        ProcessFrame(
                            state,
                            frame);
                }

                if (state.Cancellation
                    .IsCancellationRequested)
                {
                    long stale =
                        Interlocked.Increment(
                            ref state.StaleDropped);

                    DiagnosticLog.Write(
                        "PERSIST.SAMPLE_STALE",
                        $"epoch={state.Epoch.Id} " +
                        $"staleDropped={stale}");

                    continue;
                }

                DiagnosticLog.Write(
                    "PERSIST.SAMPLE_OK",
                    $"epoch={sample.EpochId} " +
                    $"profile={sample.ProfileWidth} " +
                    $"classification={sample.Change.Classification} " +
                    $"reason={sample.Change.Reason} " +
                    $"output={sample.OutputWidth}x{sample.OutputHeight} " +
                    $"processingMs={sample.ProcessingMilliseconds:0.000} " +
                    $"resizeMs={sample.ResizeMilliseconds:0.000} " +
                    $"readbackMs={sample.ReadbackMilliseconds:0.000} " +
                    $"lumaMs={sample.LuminanceMilliseconds:0.000} " +
                    $"diffMs={sample.Change.DiffMilliseconds:0.000} " +
                    $"changedPixelRatio={sample.Change.ChangedPixelRatio:0.000000} " +
                    $"changedTileRatio={sample.Change.ChangedTileRatio:0.000000} " +
                    $"framesArrived={sample.FramesArrived} " +
                    $"framesReplaced={sample.FramesReplaced} " +
                    $"samplesProcessed={sample.SamplesProcessed} " +
                    $"poolRecreates={sample.FramePoolRecreates} " +
                    $"staleDropped={sample.StaleDropped}");

                PublishSample(
                    sample);
            }
        }
        catch (OperationCanceledException)
            when (state.Cancellation
                .IsCancellationRequested)
        {
            // Expected stop path.
        }
        catch (Exception ex)
        {
            hadError =
                true;

            errorType =
                ex.GetType().Name;

            errorMessage =
                ex.Message;

            state.SetStopReasonIfUnset(
                "error");

            DiagnosticLog.Write(
                "PERSIST.SESSION_ERROR",
                $"epoch={state.Epoch.Id} " +
                $"type={errorType} " +
                $"message={errorMessage}");
        }
        finally
        {
            string stopReason =
                state.GetStopReason();

            long framesArrived =
                Interlocked.Read(
                    ref state.FramesArrived);

            long framesReplaced =
                Interlocked.Read(
                    ref state.FramesReplaced);

            long samplesProcessed =
                Interlocked.Read(
                    ref state.SamplesProcessed);

            long poolRecreates =
                Interlocked.Read(
                    ref state.FramePoolRecreates);

            long staleDropped =
                Interlocked.Read(
                    ref state.StaleDropped);

            state.DisposeResources();

            lock (_lifecycleGate)
            {
                if (ReferenceEquals(
                        _active,
                        state))
                {
                    _active =
                        null;
                }
            }

            DiagnosticLog.Write(
                "PERSIST.SESSION_END",
                $"epoch={state.Epoch.Id} " +
                $"reason={stopReason} " +
                $"hadError={hadError} " +
                $"framesArrived={framesArrived} " +
                $"framesReplaced={framesReplaced} " +
                $"samplesProcessed={samplesProcessed} " +
                $"poolRecreates={poolRecreates} " +
                $"staleDropped={staleDropped}");

            PublishSessionEnded(
                new PersistentChangeSessionEnded(
                    state.Epoch.Id,
                    stopReason,
                    hadError,
                    errorType,
                    errorMessage,
                    framesArrived,
                    framesReplaced,
                    samplesProcessed,
                    poolRecreates,
                    staleDropped));
        }
    }

    private static void HandleFrameArrived(
        PersistentRunState state,
        Direct3D11CaptureFramePool sender)
    {
        try
        {
            lock (state.FrameGate)
            {
                if (!state.AcceptingFrames ||
                    state.Cancellation
                        .IsCancellationRequested)
                {
                    return;
                }

                Direct3D11CaptureFrame? incoming =
                    sender.TryGetNextFrame();

                if (incoming is null)
                    return;

                Interlocked.Increment(
                    ref state.FramesArrived);

                if (state.LatestFrame is not null)
                {
                    state.LatestFrame.Dispose();

                    state.LatestFrame =
                        null;

                    Interlocked.Increment(
                        ref state.FramesReplaced);
                }

                state.LatestFrame =
                    incoming;
            }
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write(
                "PERSIST.FRAME_ERROR",
                $"epoch={state.Epoch.Id} " +
                $"type={ex.GetType().Name} " +
                $"message={ex.Message}");

            state.SetStopReasonIfUnset(
                "frame_error");

            RequestStop(
                state,
                "frame_error");
        }
    }

    private static Direct3D11CaptureFrame?
        TakeLatestFrame(
            PersistentRunState state)
    {
        lock (state.FrameGate)
        {
            Direct3D11CaptureFrame? frame =
                state.LatestFrame;

            state.LatestFrame =
                null;

            return frame;
        }
    }

    private static void RecreateFramePool(
        PersistentRunState state,
        SizeInt32 newSize)
    {
        state.Cancellation.Token
            .ThrowIfCancellationRequested();

        lock (state.FrameGate)
        {
            if (state.LatestFrame is not null)
            {
                state.LatestFrame.Dispose();

                state.LatestFrame =
                    null;

                Interlocked.Increment(
                    ref state.FramesReplaced);
            }

            IDirect3DDevice direct3DDevice =
                state.CanvasDevice;

            state.FramePool.Recreate(
                direct3DDevice,
                DirectXPixelFormat.B8G8R8A8UIntNormalized,
                FrameBufferCount,
                newSize);

            state.PoolSize =
                newSize;

            Interlocked.Increment(
                ref state.FramePoolRecreates);
        }

        if (state.RenderTarget is not null)
        {
            state.RenderTarget.Dispose();

            state.RenderTarget =
                null;

            state.RenderTargetWidth =
                0;

            state.RenderTargetHeight =
                0;
        }

        state.Detector.Reset();

        DiagnosticLog.Write(
            "PERSIST.POOL_RECREATE",
            $"epoch={state.Epoch.Id} " +
            $"size={newSize.Width}x{newSize.Height} " +
            $"count={Interlocked.Read(ref state.FramePoolRecreates)}");
    }

    private static PersistentChangeSample ProcessFrame(
        PersistentRunState state,
        Direct3D11CaptureFrame frame)
    {
        Stopwatch totalStopwatch =
            Stopwatch.StartNew();

        int contentWidth =
            frame.ContentSize.Width;

        int contentHeight =
            frame.ContentSize.Height;

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
                "Persistent frame has invalid source dimensions.");
        }

        double scaleFactor =
            sourceWidth > state.ProfileWidth
                ? state.ProfileWidth /
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

        if (
            state.RenderTarget is null ||
            state.RenderTargetWidth !=
                outputWidth ||
            state.RenderTargetHeight !=
                outputHeight)
        {
            state.RenderTarget?.Dispose();

            state.RenderTarget =
                new CanvasRenderTarget(
                    state.CanvasDevice,
                    outputWidth,
                    outputHeight,
                    96f);

            state.RenderTargetWidth =
                outputWidth;

            state.RenderTargetHeight =
                outputHeight;
        }

        Stopwatch resizeStopwatch =
            Stopwatch.StartNew();

        using CanvasBitmap sourceBitmap =
            CanvasBitmap.CreateFromDirect3D11Surface(
                state.CanvasDevice,
                frame.Surface);

        using (
            CanvasDrawingSession drawingSession =
                state.RenderTarget.CreateDrawingSession())
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

        Stopwatch readbackStopwatch =
            Stopwatch.StartNew();

        byte[] bgra =
            state.RenderTarget.GetPixelBytes();

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
                $"Unexpected persistent BGRA buffer size. " +
                $"Expected at least {expectedBgraLength}, " +
                $"received {bgra.Length}.");
        }

        Stopwatch luminanceStopwatch =
            Stopwatch.StartNew();

        byte[] luminance =
            new byte[pixelCount];

        int sourceIndex =
            0;

        for (
            int pixelIndex = 0;
            pixelIndex < pixelCount;
            pixelIndex++)
        {
            int blue =
                bgra[sourceIndex];

            int green =
                bgra[sourceIndex + 1];

            int red =
                bgra[sourceIndex + 2];

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

        ChangeResult change =
            state.Detector.Process(
                luminance,
                outputWidth,
                outputHeight);

        long samplesProcessed =
            Interlocked.Increment(
                ref state.SamplesProcessed);

        totalStopwatch.Stop();

        return new PersistentChangeSample(
            state.Epoch.Id,
            state.ProfileWidth,
            change,
            sourceWidth,
            sourceHeight,
            outputWidth,
            outputHeight,
            resizeStopwatch.Elapsed.TotalMilliseconds,
            readbackStopwatch.Elapsed.TotalMilliseconds,
            luminanceStopwatch.Elapsed.TotalMilliseconds,
            totalStopwatch.Elapsed.TotalMilliseconds,
            Interlocked.Read(
                ref state.FramesArrived),
            Interlocked.Read(
                ref state.FramesReplaced),
            samplesProcessed,
            Interlocked.Read(
                ref state.FramePoolRecreates),
            Interlocked.Read(
                ref state.StaleDropped));
    }

    private static void RequestStop(
        PersistentRunState state,
        string reason)
    {
        state.SetStopReasonIfUnset(
            reason);

        try
        {
            state.Cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The worker already completed cleanup.
        }
    }

    private void PublishSample(
        PersistentChangeSample sample)
    {
        try
        {
            SampleReady?.Invoke(
                sample);
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write(
                "PERSIST.SAMPLE_EVENT_ERROR",
                $"epoch={sample.EpochId} " +
                $"type={ex.GetType().Name}");
        }
    }

    private void PublishSessionEnded(
        PersistentChangeSessionEnded ended)
    {
        try
        {
            SessionEnded?.Invoke(
                ended);
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write(
                "PERSIST.END_EVENT_ERROR",
                $"epoch={ended.EpochId} " +
                $"type={ex.GetType().Name}");
        }
    }

    private sealed class PersistentRunState
    {
        private readonly object
            _reasonGate =
                new();

        private string?
            _stopReason;

        public PersistentRunState(
            ContextEpoch epoch,
            int profileWidth,
            TimeSpan sampleInterval,
            GraphicsCaptureItem item,
            CanvasDevice canvasDevice,
            Direct3D11CaptureFramePool framePool,
            GraphicsCaptureSession session,
            SizeInt32 poolSize)
        {
            Epoch =
                epoch;

            ProfileWidth =
                profileWidth;

            SampleInterval =
                sampleInterval;

            Item =
                item;

            CanvasDevice =
                canvasDevice;

            FramePool =
                framePool;

            Session =
                session;

            PoolSize =
                poolSize;
        }

        public ContextEpoch Epoch { get; }

        public int ProfileWidth { get; }

        public TimeSpan SampleInterval { get; }

        public GraphicsCaptureItem Item { get; }

        public CanvasDevice CanvasDevice { get; }

        public Direct3D11CaptureFramePool FramePool { get; }

        public GraphicsCaptureSession Session { get; }

        public CancellationTokenSource Cancellation { get; } =
            new();

        public ChangeDetector Detector { get; } =
            new();

        public object FrameGate { get; } =
            new();

        public SizeInt32 PoolSize { get; set; }

        public Direct3D11CaptureFrame?
            LatestFrame { get; set; }

        public CanvasRenderTarget?
            RenderTarget { get; set; }

        public int
            RenderTargetWidth { get; set; }

        public int
            RenderTargetHeight { get; set; }

        public bool
            AcceptingFrames { get; set; } =
                true;

        public Task?
            WorkerTask { get; set; }

        public CancellationTokenRegistration
            EpochRegistration { get; set; }

        public TypedEventHandler<
            Direct3D11CaptureFramePool,
            object>?
            FrameArrivedHandler { get; set; }

        public TypedEventHandler<
            GraphicsCaptureItem,
            object>?
            ItemClosedHandler { get; set; }

        public long FramesArrived;

        public long FramesReplaced;

        public long SamplesProcessed;

        public long FramePoolRecreates;

        public long StaleDropped;

        public void SetStopReasonIfUnset(
            string reason)
        {
            lock (_reasonGate)
            {
                _stopReason ??=
                    reason;
            }
        }

        public string GetStopReason()
        {
            lock (_reasonGate)
            {
                return
                    _stopReason ??
                    "completed";
            }
        }

        public void DisposeResources()
        {
            try
            {
                EpochRegistration.Dispose();
            }
            catch
            {
            }

            lock (FrameGate)
            {
                AcceptingFrames =
                    false;

                if (FrameArrivedHandler is not null)
                {
                    try
                    {
                        FramePool.FrameArrived -=
                            FrameArrivedHandler;
                    }
                    catch
                    {
                    }
                }

                if (LatestFrame is not null)
                {
                    try
                    {
                        LatestFrame.Dispose();
                    }
                    catch
                    {
                    }

                    LatestFrame =
                        null;
                }
            }

            if (ItemClosedHandler is not null)
            {
                try
                {
                    Item.Closed -=
                        ItemClosedHandler;
                }
                catch
                {
                }
            }

            try
            {
                RenderTarget?.Dispose();
            }
            catch
            {
            }

            RenderTarget =
                null;

            try
            {
                Session.Dispose();
            }
            catch
            {
            }

            try
            {
                FramePool.Dispose();
            }
            catch
            {
            }

            try
            {
                CanvasDevice.Dispose();
            }
            catch
            {
            }

            try
            {
                Cancellation.Dispose();
            }
            catch
            {
            }
        }
    }
}
