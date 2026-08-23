using LocalCopilot_App.Diagnostics;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace LocalCopilot_App.Services;

public sealed class UiAutomationProbeWorker :
    IDisposable
{
    private const uint NativeConnectionTimeoutMilliseconds =
        1500;

    private const uint NativeTransactionTimeoutMilliseconds =
        1500;

    private const int WorkerJoinTimeoutMilliseconds =
        5000;

    private readonly IForegroundWindowIdentityValidator
        _identityValidator;

    private readonly BoundedLatestWinsSlot<WorkItem>
        _pending =
            new();

    private readonly AutoResetEvent
        _workAvailable =
            new(initialState: false);

    private readonly ManualResetEvent
        _stopRequested =
            new(initialState: false);

    private readonly object
        _lifecycleGate =
            new();

    private Thread?
        _thread;

    private bool
        _disposed;

    private long
        _nextRequestId;

    public UiAutomationProbeWorker(
        IForegroundWindowIdentityValidator identityValidator)
    {
        _identityValidator =
            identityValidator ??
            throw new ArgumentNullException(
                nameof(identityValidator));
    }

    public UiAutomationProbeOperation Probe(
        ContextEpoch epoch,
        TimeSpan timeout)
    {
        return ProbeCore(
            epoch,
            timeout,
            diagnosticHold: TimeSpan.Zero,
            UiAutomationWorkKind.RootProbe,
            snapshotBudgets: null);
    }

    public UiAutomationProbeOperation CaptureStructuralSnapshot(
        ContextEpoch epoch,
        TimeSpan timeout,
        UiAutomationSnapshotBudgets? budgets = null)
    {
        UiAutomationSnapshotBudgets effectiveBudgets =
            budgets ??
            UiAutomationSnapshotBudgets.M3_2Default;

        effectiveBudgets.ValidateForNonTextSnapshot();

        return ProbeCore(
            epoch,
            timeout,
            diagnosticHold: TimeSpan.Zero,
            UiAutomationWorkKind.StructuralSnapshot,
            effectiveBudgets);
    }

    internal UiAutomationProbeOperation ProbeForDiagnostics(
        ContextEpoch epoch,
        TimeSpan timeout,
        TimeSpan diagnosticHold)
    {
        ValidateDiagnosticHold(diagnosticHold);

        return ProbeCore(
            epoch,
            timeout,
            diagnosticHold,
            UiAutomationWorkKind.RootProbe,
            snapshotBudgets: null);
    }

    internal UiAutomationProbeOperation
        CaptureStructuralSnapshotForDiagnostics(
            ContextEpoch epoch,
            TimeSpan timeout,
            TimeSpan diagnosticHold,
            UiAutomationSnapshotBudgets? budgets = null)
    {
        ValidateDiagnosticHold(diagnosticHold);

        UiAutomationSnapshotBudgets effectiveBudgets =
            budgets ??
            UiAutomationSnapshotBudgets.M3_2Default;

        effectiveBudgets.ValidateForNonTextSnapshot();

        return ProbeCore(
            epoch,
            timeout,
            diagnosticHold,
            UiAutomationWorkKind.StructuralSnapshot,
            effectiveBudgets);
    }

    private static void ValidateDiagnosticHold(
        TimeSpan diagnosticHold)
    {
        if (diagnosticHold <= TimeSpan.Zero ||
            diagnosticHold > TimeSpan.FromSeconds(2))
        {
            throw new ArgumentOutOfRangeException(
                nameof(diagnosticHold));
        }
    }

    private UiAutomationProbeOperation ProbeCore(
        ContextEpoch epoch,
        TimeSpan timeout,
        TimeSpan diagnosticHold,
        UiAutomationWorkKind kind,
        UiAutomationSnapshotBudgets? snapshotBudgets)
    {
        ArgumentNullException.ThrowIfNull(epoch);

        if (timeout <= TimeSpan.Zero ||
            timeout > TimeSpan.FromSeconds(10))
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeout));
        }

        WorkItem item =
            new(
                Interlocked.Increment(
                    ref _nextRequestId),
                epoch.Id,
                epoch.Snapshot.Handle,
                epoch.Snapshot.ProcessId,
                Stopwatch.GetTimestamp(),
                timeout,
                diagnosticHold,
                kind,
                snapshotBudgets,
                epoch.CancellationToken);

        WorkItem? replaced;

        lock (_lifecycleGate)
        {
            if (_disposed)
            {
                item.TryComplete(
                    item.CreateResult(
                        UiAutomationProbeOutcome.Cancelled,
                        UiAutomationProbeReason.WorkerStopped,
                        hresult: null,
                        workerThreadId: 0,
                        identityRevalidated: false));

                return item.Operation;
            }

            try
            {
                EnsureWorkerStarted();
            }
            catch (Exception ex)
            {
                item.TryComplete(
                    item.CreateResult(
                        UiAutomationProbeOutcome.Faulted,
                        UiAutomationProbeReason.WorkerInitializationFailed,
                        ex.HResult,
                        workerThreadId: 0,
                        identityRevalidated: false));

                return item.Operation;
            }

            if (!_pending.TryPublish(
                    item,
                    out replaced))
            {
                item.TryComplete(
                    item.CreateResult(
                        UiAutomationProbeOutcome.Cancelled,
                        UiAutomationProbeReason.WorkerStopped,
                        hresult: null,
                        workerThreadId: 0,
                        identityRevalidated: false));

                return item.Operation;
            }

            _workAvailable.Set();
        }

        if (replaced is not null)
        {
            replaced.TryComplete(
                replaced.CreateResult(
                    UiAutomationProbeOutcome.Cancelled,
                    UiAutomationProbeReason.Superseded,
                    hresult: null,
                    workerThreadId: 0,
                    identityRevalidated: false));
        }

        DiagnosticLog.Write(
            "UIA.QUEUE",
            $"request={item.RequestId} " +
            $"epoch={item.EpochId} " +
            $"kind={item.Kind} " +
            $"replacedRequest={replaced?.RequestId ?? 0} " +
            $"replacedEpoch={replaced?.EpochId ?? 0}");

        return item.Operation;
    }

    public void Dispose()
    {
        Thread? thread;
        WorkItem? pending;

        lock (_lifecycleGate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            pending = _pending.Complete();
            thread = _thread;
            _stopRequested.Set();
        }

        pending?.TryComplete(
            pending.CreateResult(
                UiAutomationProbeOutcome.Cancelled,
                UiAutomationProbeReason.WorkerStopped,
                hresult: null,
                workerThreadId: 0,
                identityRevalidated: false));

        bool joined =
            thread is null ||
            thread.Join(
                WorkerJoinTimeoutMilliseconds);

        DiagnosticLog.Write(
            "UIA.WORKER_DISPOSE",
            $"started={thread is not null} joined={joined}");

        if (joined)
        {
            _workAvailable.Dispose();
            _stopRequested.Dispose();
        }

        GC.SuppressFinalize(this);
    }

    private void EnsureWorkerStarted()
    {
        if (_thread is not null)
        {
            return;
        }

        Thread thread =
            new(WorkerMain)
            {
                IsBackground = true,
                Name = "LocalCopilot.UIA.MTA"
            };

        thread.SetApartmentState(
            ApartmentState.MTA);

        thread.Start();
        _thread = thread;
    }

    private void WorkerMain()
    {
        int workerThreadId =
            Environment.CurrentManagedThreadId;

        int apartmentHResult =
            UiAutomationNativeClient.InitializeMta();

        bool apartmentInitialized =
            apartmentHResult >= 0;

        UiAutomationNativeClient? client =
            null;

        ProcessIntegrityInspector? integrityInspector =
            null;

        int initializationHResult =
            apartmentHResult;

        try
        {
            if (apartmentInitialized)
            {
                initializationHResult =
                    UiAutomationNativeClient.TryCreate(
                        NativeConnectionTimeoutMilliseconds,
                        NativeTransactionTimeoutMilliseconds,
                        out client);
            }

            if (initializationHResult >= 0 &&
                !ProcessIntegrityInspector.TryCreate(
                    out integrityInspector,
                    out initializationHResult))
            {
                client?.Dispose();
                client = null;
            }

            DiagnosticLog.Write(
                "UIA.WORKER_START",
                $"thread={workerThreadId} " +
                $"apartment={Thread.CurrentThread.GetApartmentState()} " +
                $"hresult=0x{initializationHResult:X8}");

            WaitHandle[] waits =
            [
                _stopRequested,
                _workAvailable
            ];

            while (WaitHandle.WaitAny(waits) != 0)
            {
                while (_pending.TryTake(
                    out WorkItem? item))
                {
                    if (item is null)
                    {
                        continue;
                    }

                    UiAutomationProbeResult result;

                    try
                    {
                        item.MarkStarted();

                        result =
                            Execute(
                                item,
                                workerThreadId,
                                initializationHResult,
                                client,
                                integrityInspector);
                    }
                    catch (Exception ex)
                    {
                        result =
                            item.CreateResult(
                                UiAutomationProbeOutcome.Faulted,
                                UiAutomationProbeReason.NativeFailure,
                                ex.HResult,
                                workerThreadId,
                                identityRevalidated: false);
                    }

                    item.TryComplete(result);

                    if (_stopRequested.WaitOne(0))
                    {
                        break;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write(
                "UIA.WORKER_ERROR",
                $"thread={workerThreadId} " +
                $"type={ex.GetType().Name} " +
                $"hresult=0x{ex.HResult:X8}");
        }
        finally
        {
            WorkItem? pending =
                _pending.Complete();

            pending?.TryComplete(
                pending.CreateResult(
                    UiAutomationProbeOutcome.Cancelled,
                    UiAutomationProbeReason.WorkerStopped,
                    hresult: null,
                    workerThreadId,
                    identityRevalidated: false));

            client?.Dispose();

            if (apartmentInitialized)
            {
                UiAutomationNativeClient.UninitializeMta();
            }

            DiagnosticLog.Write(
                "UIA.WORKER_STOP",
                $"thread={workerThreadId}");
        }
    }

    private UiAutomationProbeResult Execute(
        WorkItem item,
        int workerThreadId,
        int initializationHResult,
        UiAutomationNativeClient? client,
        ProcessIntegrityInspector? integrityInspector)
    {
        if (initializationHResult < 0 ||
            client is null ||
            integrityInspector is null)
        {
            return item.CreateResult(
                UiAutomationProbeOutcome.Faulted,
                UiAutomationProbeReason.WorkerInitializationFailed,
                initializationHResult,
                workerThreadId,
                identityRevalidated: false);
        }

        if (item.DiagnosticHold > TimeSpan.Zero)
        {
            DiagnosticLog.Write(
                "UIA.DIAGNOSTIC_HOLD",
                $"request={item.RequestId} " +
                $"durationMs={item.DiagnosticHold.TotalMilliseconds:0}");

            if (_stopRequested.WaitOne(
                    item.DiagnosticHold))
            {
                return item.CreateResult(
                    UiAutomationProbeOutcome.Cancelled,
                    UiAutomationProbeReason.WorkerStopped,
                    hresult: null,
                    workerThreadId,
                    identityRevalidated: false);
            }
        }

        if (item.CancellationToken.IsCancellationRequested)
        {
            return item.CreateResult(
                UiAutomationProbeOutcome.Cancelled,
                UiAutomationProbeReason.RequestCancelled,
                hresult: null,
                workerThreadId,
                identityRevalidated: false);
        }

        if (item.HasExpired)
        {
            return item.CreateResult(
                UiAutomationProbeOutcome.Timeout,
                UiAutomationProbeReason.DeadlineExpired,
                hresult: null,
                workerThreadId,
                identityRevalidated: false);
        }

        bool identityRevalidated =
            _identityValidator.IsCurrent(
                new ForegroundWindowSnapshot(
                    item.Handle,
                    item.ProcessId,
                    string.Empty,
                    string.Empty));

        if (!identityRevalidated)
        {
            return item.CreateResult(
                UiAutomationProbeOutcome.Unavailable,
                UiAutomationProbeReason.IdentityChanged,
                hresult: null,
                workerThreadId,
                identityRevalidated: false);
        }

        if (item.CancellationToken.IsCancellationRequested)
        {
            return item.CreateResult(
                UiAutomationProbeOutcome.Cancelled,
                UiAutomationProbeReason.RequestCancelled,
                hresult: null,
                workerThreadId,
                identityRevalidated);
        }

        if (item.HasExpired)
        {
            return item.CreateResult(
                UiAutomationProbeOutcome.Timeout,
                UiAutomationProbeReason.DeadlineExpired,
                hresult: null,
                workerThreadId,
                identityRevalidated);
        }

        bool integrityInspected =
            integrityInspector.TryIsSameOrLowerIntegrity(
                item.ProcessId,
                out bool mayRead,
                out uint targetIntegrityLevel,
                out int accessHResult);

        DiagnosticLog.Write(
            "UIA.INTEGRITY_CHECK",
            $"request={item.RequestId} " +
            $"currentRid={integrityInspector.CurrentIntegrityLevel} " +
            $"targetRid={targetIntegrityLevel} " +
            $"inspected={integrityInspected} " +
            $"mayRead={mayRead} " +
            $"hresult=0x{accessHResult:X8}");

        if (!integrityInspected)
        {
            return item.CreateResult(
                UiAutomationProbeOutcome.Unavailable,
                UiAutomationProbeReason.AccessInspectionFailed,
                accessHResult,
                workerThreadId,
                identityRevalidated);
        }

        if (!mayRead)
        {
            return item.CreateResult(
                UiAutomationProbeOutcome.Unavailable,
                UiAutomationProbeReason.HigherIntegrity,
                accessHResult,
                workerThreadId,
                identityRevalidated);
        }

        if (item.CancellationToken.IsCancellationRequested)
        {
            return item.CreateResult(
                UiAutomationProbeOutcome.Cancelled,
                UiAutomationProbeReason.RequestCancelled,
                hresult: null,
                workerThreadId,
                identityRevalidated);
        }

        if (item.HasExpired)
        {
            return item.CreateResult(
                UiAutomationProbeOutcome.Timeout,
                UiAutomationProbeReason.DeadlineExpired,
                hresult: null,
                workerThreadId,
                identityRevalidated);
        }

        if (item.Kind ==
            UiAutomationWorkKind.StructuralSnapshot)
        {
            return ExecuteStructuralSnapshot(
                item,
                workerThreadId,
                client,
                identityRevalidated);
        }

        object? element =
            null;

        try
        {
            int hresult = client.ElementFromHandle(
                item.Handle,
                out element);

            if (item.CancellationToken.IsCancellationRequested)
            {
                return item.CreateResult(
                    UiAutomationProbeOutcome.Cancelled,
                    UiAutomationProbeReason.RequestCancelled,
                    hresult,
                    workerThreadId,
                    identityRevalidated);
            }

            UiAutomationProbeClassification classification =
                UiAutomationProbeNativeClassifier.Classify(
                    hresult,
                    element is not null,
                    item.HasExpired);

            return item.CreateResult(
                classification.Outcome,
                classification.Reason,
                classification.HResult,
                workerThreadId,
                identityRevalidated);
        }
        catch (Exception ex)
        {
            return item.CreateResult(
                UiAutomationProbeOutcome.Faulted,
                UiAutomationProbeReason.NativeFailure,
                ex.HResult,
                workerThreadId,
                identityRevalidated);
        }
        finally
        {
            UiAutomationNativeClient.ReleaseElement(
                element);
        }
    }

    private UiAutomationProbeResult ExecuteStructuralSnapshot(
        WorkItem item,
        int workerThreadId,
        UiAutomationNativeClient client,
        bool identityRevalidated)
    {
        UiAutomationSnapshotBudgets budgets =
            item.SnapshotBudgets ??
            throw new InvalidOperationException(
                "Structural snapshot budgets are required.");

        try
        {
            UiAutomationNativeSnapshotResult nativeResult =
                client.CaptureStructuralSnapshot(
                    item.Handle,
                    budgets,
                    () =>
                        item.CancellationToken
                            .IsCancellationRequested ||
                        _stopRequested.WaitOne(0));

            if (_stopRequested.WaitOne(0))
            {
                return item.CreateResult(
                    UiAutomationProbeOutcome.Cancelled,
                    UiAutomationProbeReason.WorkerStopped,
                    nativeResult.HResult,
                    workerThreadId,
                    identityRevalidated);
            }

            if (nativeResult.Cancelled ||
                item.CancellationToken.IsCancellationRequested)
            {
                return item.CreateResult(
                    UiAutomationProbeOutcome.Cancelled,
                    UiAutomationProbeReason.RequestCancelled,
                    nativeResult.HResult,
                    workerThreadId,
                    identityRevalidated);
            }

            UiAutomationProbeClassification classification =
                UiAutomationProbeNativeClassifier.Classify(
                    nativeResult.HResult,
                    nativeResult.Snapshot is not null,
                    item.HasExpired);

            if (classification.Outcome !=
                UiAutomationProbeOutcome.Available)
            {
                return item.CreateResult(
                    classification.Outcome,
                    classification.Reason,
                    classification.HResult,
                    workerThreadId,
                    identityRevalidated);
            }

            return item.CreateResult(
                UiAutomationProbeOutcome.Available,
                UiAutomationProbeReason.SnapshotCaptured,
                classification.HResult,
                workerThreadId,
                identityRevalidated,
                nativeResult.Snapshot);
        }
        catch (Exception ex)
        {
            return item.CreateResult(
                UiAutomationProbeOutcome.Faulted,
                UiAutomationProbeReason.NativeFailure,
                ex.HResult,
                workerThreadId,
                identityRevalidated);
        }
    }

    private sealed class WorkItem
    {
        private readonly TaskCompletionSource<bool>
            _started =
                new(
                    TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly TaskCompletionSource<UiAutomationProbeResult>
            _completion =
                new(
                    TaskCreationOptions.RunContinuationsAsynchronously);

        public WorkItem(
            long requestId,
            long epochId,
            nint handle,
            uint processId,
            long startedTimestamp,
            TimeSpan timeout,
            TimeSpan diagnosticHold,
            UiAutomationWorkKind kind,
            UiAutomationSnapshotBudgets? snapshotBudgets,
            CancellationToken cancellationToken)
        {
            RequestId = requestId;
            EpochId = epochId;
            Handle = handle;
            ProcessId = processId;
            StartedTimestamp = startedTimestamp;
            Timeout = timeout;
            DiagnosticHold = diagnosticHold;
            Kind = kind;
            SnapshotBudgets = snapshotBudgets;
            CancellationToken = cancellationToken;
        }

        public long RequestId { get; }

        public long EpochId { get; }

        public nint Handle { get; }

        public uint ProcessId { get; }

        public long StartedTimestamp { get; }

        public TimeSpan Timeout { get; }

        public TimeSpan DiagnosticHold { get; }

        public UiAutomationWorkKind Kind { get; }

        public UiAutomationSnapshotBudgets? SnapshotBudgets { get; }

        public CancellationToken CancellationToken { get; }

        public UiAutomationProbeOperation Operation =>
            new(
                RequestId,
                _started.Task,
                _completion.Task);

        public bool HasExpired =>
            Elapsed >= Timeout;

        private TimeSpan Elapsed =>
            Stopwatch.GetElapsedTime(
                StartedTimestamp);

        public UiAutomationProbeResult CreateResult(
            UiAutomationProbeOutcome outcome,
            UiAutomationProbeReason reason,
            int? hresult,
            int workerThreadId,
            bool identityRevalidated,
            UiAutomationStructuralSnapshot? snapshot = null)
        {
            return new UiAutomationProbeResult(
                RequestId,
                EpochId,
                outcome,
                reason,
                Elapsed,
                hresult,
                workerThreadId,
                identityRevalidated,
                snapshot);
        }

        public void MarkStarted()
        {
            _started.TrySetResult(true);
        }

        public void TryComplete(
            UiAutomationProbeResult result)
        {
            _started.TrySetResult(true);

            if (!_completion.TrySetResult(result))
            {
                return;
            }

            string hresult =
                result.HResult.HasValue
                    ? $"0x{result.HResult.Value:X8}"
                    : "none";

            DiagnosticLog.Write(
                "UIA.REQUEST_COMPLETE",
                $"request={result.RequestId} " +
                $"epoch={result.EpochId} " +
                $"kind={Kind} " +
                $"outcome={result.Outcome} " +
                $"reason={result.Reason} " +
                $"elapsedMs={result.Elapsed.TotalMilliseconds:0.000} " +
                $"hresult={hresult} " +
                $"workerThread={result.WorkerThreadId} " +
                $"identityRevalidated={result.IdentityRevalidated} " +
                $"snapshotProduced={result.Snapshot is not null}");
        }
    }

    private enum UiAutomationWorkKind
    {
        RootProbe,
        StructuralSnapshot
    }
}

public sealed record UiAutomationProbeOperation(
    long RequestId,
    Task Started,
    Task<UiAutomationProbeResult> Completion);
