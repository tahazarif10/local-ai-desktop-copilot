using LocalCopilot_App.Diagnostics;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace LocalCopilot_App.Services;

public sealed class InputActivityTracker :
    IDisposable
{
    private const int WhKeyboardLl =
        13;

    private const int WhMouseLl =
        14;

    private const uint WmKeyDown =
        0x0100;

    private const uint WmSysKeyDown =
        0x0104;

    private const uint WmLButtonDown =
        0x0201;

    private const uint WmRButtonDown =
        0x0204;

    private const uint WmMButtonDown =
        0x0207;

    private const uint WmMouseWheel =
        0x020A;

    private const uint WmXButtonDown =
        0x020B;

    private const uint WmMouseHWheel =
        0x020E;

    private readonly object _gate =
        new();

    private readonly HookProcedure _keyboardProcedure;

    private readonly HookProcedure _mouseProcedure;

    private nint _keyboardHook;

    private nint _mouseHook;

    private InputHookHealthMonitor?
        _healthMonitor;

    private long _epochId;

    private bool _enabled;

    private bool _disposed;

    public InputActivityTracker()
    {
        _keyboardProcedure =
            KeyboardHookCallback;

        _mouseProcedure =
            MouseHookCallback;
    }

    public event Action<InputActivityEvent>?
        ActivityObserved;

    public void Start(
        long epochId)
    {
        if (epochId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(epochId));
        }

        bool reused;

        uint installThreadId;

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(
                _disposed,
                this);

            reused =
                _keyboardHook !=
                    nint.Zero &&
                _mouseHook !=
                    nint.Zero;

            if (!reused)
            {
                InstallHooks();
            }

            _epochId =
                epochId;

            _enabled =
                true;

            installThreadId =
                _healthMonitor?.InstallThreadId ??
                0;
        }

        DiagnosticLog.Write(
            reused
                ? "INPUT.TRACKING_REUSE"
                : "INPUT.TRACKING_START",
            $"epoch={epochId} " +
            $"installThreadId={installThreadId}");
    }

    public void Stop(
        string reason)
    {
        StopCore(
            reason);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed =
                true;
        }

        StopCore(
            "dispose");

        GC.SuppressFinalize(
            this);
    }

    private void InstallHooks()
    {
        uint installThreadId =
            GetCurrentThreadId();

        InputHookHealthMonitor healthMonitor =
            new(
                installThreadId);

        _healthMonitor =
            healthMonitor;

        nint moduleHandle =
            GetModuleHandleW(
                null);

        if (moduleHandle ==
            nint.Zero)
        {
            _healthMonitor =
                null;

            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "Unable to resolve the application module handle.");
        }

        nint keyboardHook =
            SetWindowsHookExW(
                WhKeyboardLl,
                _keyboardProcedure,
                moduleHandle,
                0);

        if (keyboardHook ==
            nint.Zero)
        {
            _healthMonitor =
                null;

            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "Unable to install the keyboard activity hook.");
        }

        nint mouseHook =
            SetWindowsHookExW(
                WhMouseLl,
                _mouseProcedure,
                moduleHandle,
                0);

        if (mouseHook ==
            nint.Zero)
        {
            int error =
                Marshal.GetLastWin32Error();

            UnhookWindowsHookEx(
                keyboardHook);

            _healthMonitor =
                null;

            throw new Win32Exception(
                error,
                "Unable to install the mouse activity hook.");
        }

        _keyboardHook =
            keyboardHook;

        _mouseHook =
            mouseHook;

        DiagnosticLog.Write(
            "INPUT.HOOKS_INSTALLED",
            $"installThreadId={installThreadId} " +
            "keyboard=True mouse=True");
    }

    private void StopCore(
        string reason)
    {
        nint keyboardHook;
        nint mouseHook;
        long previousEpoch;
        bool wasActive;
        InputHookHealthMonitor? healthMonitor;

        lock (_gate)
        {
            keyboardHook =
                _keyboardHook;

            mouseHook =
                _mouseHook;

            previousEpoch =
                _epochId;

            wasActive =
                _enabled ||
                keyboardHook !=
                    nint.Zero ||
                mouseHook !=
                    nint.Zero;

            _enabled =
                false;

            _epochId =
                0;

            _keyboardHook =
                nint.Zero;

            _mouseHook =
                nint.Zero;

            healthMonitor =
                _healthMonitor;
        }

        HookRemovalResult keyboardRemoval =
            UnhookSafely(
                keyboardHook,
                "keyboard");

        HookRemovalResult mouseRemoval =
            UnhookSafely(
                mouseHook,
                "mouse");

        if (healthMonitor is not null)
        {
            LogHealth(
                previousEpoch,
                healthMonitor.Snapshot(),
                keyboardRemoval,
                mouseRemoval);

            lock (_gate)
            {
                if (ReferenceEquals(
                        _healthMonitor,
                        healthMonitor))
                {
                    _healthMonitor =
                        null;
                }
            }
        }

        if (wasActive)
        {
            DiagnosticLog.Write(
                "INPUT.TRACKING_STOP",
                $"epoch={previousEpoch} " +
                $"reason={NormalizeReason(reason)}");
        }
    }

    private nint KeyboardHookCallback(
        int code,
        nuint message,
        nint data)
    {
        long startedAt =
            Stopwatch.GetTimestamp();

        bool callbackFailed =
            false;

        bool subscriberFailed =
            false;

        InputActivityKind? activityKind =
            null;

        try
        {
            if (
                code >= 0 &&
                IsKeyboardActivity(
                    (uint)message))
            {
                activityKind =
                    InputActivityKind.KeyboardActivity;

                subscriberFailed =
                    !TryPublishActivity(
                        activityKind.Value);
            }
        }
        catch
        {
            callbackFailed =
                true;
        }

        try
        {
            return CallNextHookEx(
                nint.Zero,
                code,
                message,
                data);
        }
        catch
        {
            callbackFailed =
                true;

            return nint.Zero;
        }
        finally
        {
            RecordHookCallback(
                InputHookKind.Keyboard,
                activityKind,
                startedAt,
                callbackFailed,
                subscriberFailed);
        }
    }

    private nint MouseHookCallback(
        int code,
        nuint message,
        nint data)
    {
        long startedAt =
            Stopwatch.GetTimestamp();

        bool callbackFailed =
            false;

        bool subscriberFailed =
            false;

        InputActivityKind? activityKind =
            null;

        try
        {
            if (code >= 0)
            {
                activityKind =
                    ClassifyMouseMessage(
                        (uint)message);

                if (activityKind is not null)
                {
                    subscriberFailed =
                        !TryPublishActivity(
                            activityKind.Value);
                }
            }
        }
        catch
        {
            callbackFailed =
                true;
        }

        try
        {
            return CallNextHookEx(
                nint.Zero,
                code,
                message,
                data);
        }
        catch
        {
            callbackFailed =
                true;

            return nint.Zero;
        }
        finally
        {
            RecordHookCallback(
                InputHookKind.Mouse,
                activityKind,
                startedAt,
                callbackFailed,
                subscriberFailed);
        }
    }

    private bool TryPublishActivity(
        InputActivityKind kind)
    {
        long epochId;

        lock (_gate)
        {
            if (
                !_enabled ||
                _epochId <= 0)
            {
                return true;
            }

            epochId =
                _epochId;
        }

        InputActivityEvent activity =
            new(
                epochId,
                kind,
                Stopwatch.GetTimestamp());

        try
        {
            ActivityObserved?.Invoke(
                activity);

            return true;
        }
        catch
        {
            return false;
        }
    }

    private void RecordHookCallback(
        InputHookKind hookKind,
        InputActivityKind? activityKind,
        long startedAt,
        bool callbackFailed,
        bool subscriberFailed)
    {
        try
        {
            _healthMonitor?.RecordCallback(
                hookKind,
                activityKind,
                Stopwatch.GetTimestamp() -
                    startedAt,
                GetCurrentThreadId(),
                callbackFailed,
                subscriberFailed);
        }
        catch
        {
            // Health measurement must never affect the hook chain.
        }
    }

    private static bool IsKeyboardActivity(
        uint message)
    {
        return
            message ==
                WmKeyDown ||
            message ==
                WmSysKeyDown;
    }

    private static InputActivityKind? ClassifyMouseMessage(
        uint message)
    {
        return message switch
        {
            WmLButtonDown or
            WmRButtonDown or
            WmMButtonDown or
            WmXButtonDown =>
                InputActivityKind.MouseClick,

            WmMouseWheel or
            WmMouseHWheel =>
                InputActivityKind.MouseWheel,

            _ =>
                null
        };
    }

    private static HookRemovalResult UnhookSafely(
        nint hook,
        string hookName)
    {
        if (hook ==
            nint.Zero)
        {
            return new HookRemovalResult(
                false,
                true,
                0);
        }

        if (!UnhookWindowsHookEx(
                hook))
        {
            int error =
                Marshal.GetLastWin32Error();

            DiagnosticLog.Write(
                "INPUT.UNHOOK_ERROR",
                $"hook={hookName} " +
                $"error={error}");

            return new HookRemovalResult(
                true,
                false,
                error);
        }

        return new HookRemovalResult(
            true,
            true,
            0);
    }

    private static void LogHealth(
        long epochId,
        InputHookHealthSnapshot health,
        HookRemovalResult keyboardRemoval,
        HookRemovalResult mouseRemoval)
    {
        DiagnosticLog.Write(
            "INPUT.HOOK_HEALTH",
            $"epoch={epochId} " +
            $"installThreadId={health.InstallThreadId} " +
            $"callbacks={health.TotalCallbacks} " +
            $"keyboardCallbacks={health.KeyboardCallbacks} " +
            $"mouseCallbacks={health.MouseCallbacks} " +
            $"activities={health.TotalActivities} " +
            $"keyboardActivities={health.KeyboardActivities} " +
            $"mouseClicks={health.MouseClickActivities} " +
            $"mouseWheels={health.MouseWheelActivities} " +
            $"callbackErrors={health.CallbackErrors} " +
            $"subscriberErrors={health.SubscriberErrors} " +
            $"threadMismatches={health.ThreadMismatches} " +
            $"averageUs={FormatMicroseconds(health.AverageCallbackMicroseconds)} " +
            $"maximumUs={FormatMicroseconds(health.MaximumCallbackMicroseconds)} " +
            $"le100us={health.UpTo100Microseconds} " +
            $"le500us={health.UpTo500Microseconds} " +
            $"le1ms={health.UpTo1Millisecond} " +
            $"le5ms={health.UpTo5Milliseconds} " +
            $"le20ms={health.UpTo20Milliseconds} " +
            $"gt20ms={health.Over20Milliseconds} " +
            $"keyboardUnhookAttempted={keyboardRemoval.Attempted} " +
            $"keyboardUnhook={keyboardRemoval.Success} " +
            $"keyboardUnhookError={keyboardRemoval.ErrorCode} " +
            $"mouseUnhookAttempted={mouseRemoval.Attempted} " +
            $"mouseUnhook={mouseRemoval.Success} " +
            $"mouseUnhookError={mouseRemoval.ErrorCode} " +
            "scope=callback_including_call_next");
    }

    private static string FormatMicroseconds(
        double value)
    {
        return value.ToString(
            "0.0",
            CultureInfo.InvariantCulture);
    }

    private static string NormalizeReason(
        string reason)
    {
        return string.IsNullOrWhiteSpace(
                reason)
            ? "stopped"
            : reason;
    }

    private readonly record struct HookRemovalResult(
        bool Attempted,
        bool Success,
        int ErrorCode);

    [UnmanagedFunctionPointer(
        CallingConvention.Winapi)]
    private delegate nint HookProcedure(
        int code,
        nuint message,
        nint data);

    [DllImport(
        "user32.dll",
        EntryPoint = "SetWindowsHookExW",
        SetLastError = true)]
    private static extern nint SetWindowsHookExW(
        int hookId,
        HookProcedure procedure,
        nint moduleHandle,
        uint threadId);

    [DllImport(
        "user32.dll",
        SetLastError = true)]
    [return: MarshalAs(
        UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(
        nint hook);

    [DllImport(
        "user32.dll")]
    private static extern nint CallNextHookEx(
        nint hook,
        int code,
        nuint message,
        nint data);

    [DllImport(
        "kernel32.dll",
        EntryPoint = "GetModuleHandleW",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    private static extern nint GetModuleHandleW(
        string? moduleName);

    [DllImport(
        "kernel32.dll")]
    private static extern uint GetCurrentThreadId();
}
