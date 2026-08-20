using LocalCopilot_App.Diagnostics;
using System;
using System.ComponentModel;
using System.Diagnostics;
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
        }

        DiagnosticLog.Write(
            reused
                ? "INPUT.TRACKING_REUSE"
                : "INPUT.TRACKING_START",
            $"epoch={epochId}");
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
        nint moduleHandle =
            GetModuleHandleW(
                null);

        if (moduleHandle ==
            nint.Zero)
        {
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

            throw new Win32Exception(
                error,
                "Unable to install the mouse activity hook.");
        }

        _keyboardHook =
            keyboardHook;

        _mouseHook =
            mouseHook;
    }

    private void StopCore(
        string reason)
    {
        nint keyboardHook;
        nint mouseHook;
        long previousEpoch;
        bool wasActive;

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
        }

        UnhookSafely(
            keyboardHook,
            "keyboard");

        UnhookSafely(
            mouseHook,
            "mouse");

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
        try
        {
            if (
                code >= 0 &&
                IsKeyboardActivity(
                    (uint)message))
            {
                PublishActivity(
                    InputActivityKind.KeyboardActivity);
            }
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write(
                "INPUT.HOOK_CALLBACK_ERROR",
                $"hook=keyboard type={ex.GetType().Name}");
        }

        return CallNextHookEx(
            nint.Zero,
            code,
            message,
            data);
    }

    private nint MouseHookCallback(
        int code,
        nuint message,
        nint data)
    {
        try
        {
            if (code >= 0)
            {
                InputActivityKind? kind =
                    ClassifyMouseMessage(
                        (uint)message);

                if (kind is not null)
                {
                    PublishActivity(
                        kind.Value);
                }
            }
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write(
                "INPUT.HOOK_CALLBACK_ERROR",
                $"hook=mouse type={ex.GetType().Name}");
        }

        return CallNextHookEx(
            nint.Zero,
            code,
            message,
            data);
    }

    private void PublishActivity(
        InputActivityKind kind)
    {
        long epochId;

        lock (_gate)
        {
            if (
                !_enabled ||
                _epochId <= 0)
            {
                return;
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
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write(
                "INPUT.ACTIVITY_EVENT_ERROR",
                $"epoch={epochId} " +
                $"type={ex.GetType().Name}");
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

    private static void UnhookSafely(
        nint hook,
        string hookName)
    {
        if (hook ==
            nint.Zero)
        {
            return;
        }

        if (!UnhookWindowsHookEx(
                hook))
        {
            DiagnosticLog.Write(
                "INPUT.UNHOOK_ERROR",
                $"hook={hookName} " +
                $"error={Marshal.GetLastWin32Error()}");
        }
    }

    private static string NormalizeReason(
        string reason)
    {
        return string.IsNullOrWhiteSpace(
                reason)
            ? "stopped"
            : reason;
    }

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
}
