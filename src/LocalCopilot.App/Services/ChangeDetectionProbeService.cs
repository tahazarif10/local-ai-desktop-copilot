using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Windows.Graphics.Capture;

namespace LocalCopilot_App.Services;

public sealed class ChangeDetectionProbeService
{
    private readonly object
        _stateGate =
            new();

    private readonly SemaphoreSlim
        _sampleGate =
            new(1, 1);

    private readonly Dictionary<int, ChangeDetector>
        _detectors =
            new();

    private nint
        _observedHandle;

    private uint
        _observedProcessId;

    private bool
        _observedAllowed;

    private bool
        _hasObservedTarget;

    public void ObserveContext(
        ContextEpoch epoch)
    {
        ArgumentNullException.ThrowIfNull(
            epoch);

        lock (_stateGate)
        {
            bool allowed =
                epoch.Privacy.Allows(
                    PrivacyCapability.CapturePixels);

            bool sameAllowedTarget =
                _hasObservedTarget &&
                _observedAllowed &&
                allowed &&
                _observedHandle ==
                    epoch.Snapshot.Handle &&
                _observedProcessId ==
                    epoch.Snapshot.ProcessId;

            if (!sameAllowedTarget)
            {
                ResetDetectorsNoLock();
            }

            _observedHandle =
                epoch.Snapshot.Handle;

            _observedProcessId =
                epoch.Snapshot.ProcessId;

            _observedAllowed =
                allowed;

            _hasObservedTarget =
                true;
        }
    }

    public async Task<ChangeProbeResult>
        SampleAsync(
            ContextEpoch epoch,
            int profileWidth,
            TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(
            epoch);

        if (profileWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(profileWidth));
        }

        if (!epoch.Privacy.Allows(
                PrivacyCapability.CapturePixels))
        {
            throw new UnauthorizedAccessException(
                "Privacy policy blocks change detection.");
        }

        epoch.CancellationToken.ThrowIfCancellationRequested();

        await _sampleGate.WaitAsync(
            epoch.CancellationToken).ConfigureAwait(
                false);

        try
        {
            epoch.CancellationToken.ThrowIfCancellationRequested();

            EnsureObservedContextMatches(
                epoch);

            Stopwatch totalStopwatch =
                Stopwatch.StartNew();

            GraphicsCaptureItem item =
                GraphicsCaptureItemFactory.CreateForWindow(
                    epoch.Snapshot.Handle);

            ChangeDetectionCaptureFrame capture =
                await ChangeDetectionFrameCaptureService.CaptureAsync(
                    item,
                    timeout,
                    profileWidth,
                    epoch.CancellationToken).ConfigureAwait(
                        false);

            epoch.CancellationToken.ThrowIfCancellationRequested();

            ChangeResult change;

            lock (_stateGate)
            {
                EnsureObservedContextMatchesNoLock(
                    epoch);

                if (!_detectors.TryGetValue(
                        profileWidth,
                        out ChangeDetector? detector))
                {
                    detector =
                        new ChangeDetector();

                    _detectors.Add(
                        profileWidth,
                        detector);
                }

                change =
                    detector.Process(
                        capture.LuminancePixels,
                        capture.OutputWidth,
                        capture.OutputHeight);
            }

            totalStopwatch.Stop();

            return new ChangeProbeResult(
                epoch.Id,
                profileWidth,
                capture,
                change,
                totalStopwatch.Elapsed.TotalMilliseconds);
        }
        finally
        {
            _sampleGate.Release();
        }
    }

    public void ResetAll()
    {
        lock (_stateGate)
        {
            ResetDetectorsNoLock();

            _hasObservedTarget =
                false;

            _observedAllowed =
                false;

            _observedHandle =
                nint.Zero;

            _observedProcessId =
                0;
        }
    }

    private void EnsureObservedContextMatches(
        ContextEpoch epoch)
    {
        lock (_stateGate)
        {
            EnsureObservedContextMatchesNoLock(
                epoch);
        }
    }

    private void EnsureObservedContextMatchesNoLock(
        ContextEpoch epoch)
    {
        if (!_hasObservedTarget ||
            !_observedAllowed ||
            _observedHandle !=
                epoch.Snapshot.Handle ||
            _observedProcessId !=
                epoch.Snapshot.ProcessId)
        {
            throw new InvalidOperationException(
                "Observed foreground context changed before " +
                "the change-detection sample completed.");
        }
    }

    private void ResetDetectorsNoLock()
    {
        foreach (ChangeDetector detector in
            _detectors.Values)
        {
            detector.Reset();
        }

        _detectors.Clear();
    }
}
