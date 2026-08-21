using LocalCopilot_App.Diagnostics;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace LocalCopilot_App.Services;

public sealed class ForegroundWindowObserver : IDisposable
{
    private const uint EventSystemForeground =
        0x0003;

    private const uint WineventOutOfContext =
        0x0000;

    private const uint WineventSkipOwnProcess =
        0x0002;

    [UnmanagedFunctionPointer(
        CallingConvention.Winapi)]
    private delegate void WinEventDelegate(
        nint hWinEventHook,
        uint eventType,
        nint hwnd,
        int idObject,
        int idChild,
        uint eventThread,
        uint eventTime);

    [DllImport(
        "user32.dll",
        SetLastError = true)]
    private static extern nint SetWinEventHook(
        uint eventMin,
        uint eventMax,
        nint hmodWinEventProc,
        WinEventDelegate lpfnWinEventProc,
        uint idProcess,
        uint idThread,
        uint flags);

    [DllImport(
        "user32.dll",
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWinEvent(
        nint hWinEventHook);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    private readonly WinEventDelegate _callback;

    private GCHandle _callbackHandle;

    private nint _hook;

    private int _installingManagedThreadId;

    private uint _installingNativeThreadId;

    private bool _dispatching;

    private nint _pendingHwnd;

    public ForegroundWindowObserver()
    {
        _callback =
            WinEventCallback;

        DiagnosticLog.Write(
            "HOOK.CTOR",
            $"managedThread={Environment.CurrentManagedThreadId} " +
            $"nativeThread={GetCurrentThreadId()}");
    }

    public event Action<nint>?
        ForegroundWindowChanged;

    public bool IsRunning =>
        _hook != nint.Zero;

    public void Start()
    {
        DiagnosticLog.Write(
            "HOOK.START_BEGIN",
            $"isRunning={IsRunning} " +
            $"managedThread={Environment.CurrentManagedThreadId} " +
            $"nativeThread={GetCurrentThreadId()}");

        if (IsRunning)
            return;

        _installingManagedThreadId =
            Environment.CurrentManagedThreadId;

        _installingNativeThreadId =
            GetCurrentThreadId();

        _callbackHandle =
            GCHandle.Alloc(_callback);

        _hook =
            SetWinEventHook(
                EventSystemForeground,
                EventSystemForeground,
                nint.Zero,
                _callback,
                0,
                0,
                WineventOutOfContext |
                WineventSkipOwnProcess);

        int error =
            Marshal.GetLastWin32Error();

        DiagnosticLog.Write(
            "HOOK.START_RESULT",
            $"hook=0x{_hook.ToInt64():X} " +
            $"lastError={error} " +
            $"managedThread={_installingManagedThreadId} " +
            $"nativeThread={_installingNativeThreadId}");

        if (_hook != nint.Zero)
            return;

        if (_callbackHandle.IsAllocated)
            _callbackHandle.Free();

        _installingManagedThreadId = 0;
        _installingNativeThreadId = 0;

        throw new Win32Exception(
            error,
            "SetWinEventHook failed.");
    }

    public bool Stop()
    {
        DiagnosticLog.Write(
            "HOOK.STOP_BEGIN",
            $"hook=0x{_hook.ToInt64():X} " +
            $"currentManaged={Environment.CurrentManagedThreadId} " +
            $"installedManaged={_installingManagedThreadId} " +
            $"currentNative={GetCurrentThreadId()} " +
            $"installedNative={_installingNativeThreadId}");

        if (!IsRunning)
            return true;

        if (Environment.CurrentManagedThreadId !=
            _installingManagedThreadId)
        {
            DiagnosticLog.Write(
                "HOOK.STOP_ERROR",
                "Wrong managed thread.");

            return false;
        }

        bool success =
            UnhookWinEvent(_hook);

        int error =
            Marshal.GetLastWin32Error();

        DiagnosticLog.Write(
            "HOOK.STOP_RESULT",
            $"success={success} lastError={error}");

        if (!success)
            return false;

        _hook =
            nint.Zero;

        _installingManagedThreadId =
            0;

        _installingNativeThreadId =
            0;

        if (_callbackHandle.IsAllocated)
            _callbackHandle.Free();

        return true;
    }

    private void WinEventCallback(
        nint hWinEventHook,
        uint eventType,
        nint hwnd,
        int idObject,
        int idChild,
        uint eventThread,
        uint eventTime)
    {
        nint currentForeground =
            GetForegroundWindow();

        DiagnosticLog.Write(
            "HOOK.CALLBACK",
            $"hook=0x{hWinEventHook.ToInt64():X} " +
            $"event=0x{eventType:X} " +
            $"hwnd=0x{hwnd.ToInt64():X} " +
            $"currentForeground=0x{currentForeground.ToInt64():X} " +
            $"idObject={idObject} " +
            $"idChild={idChild} " +
            $"eventThread={eventThread} " +
            $"eventTime={eventTime} " +
            $"managedThread={Environment.CurrentManagedThreadId} " +
            $"nativeThread={GetCurrentThreadId()}");

        if (eventType != EventSystemForeground)
        {
            DiagnosticLog.Write(
                "HOOK.REJECT",
                "Wrong event type.");

            return;
        }

        if (hwnd == nint.Zero)
        {
            DiagnosticLog.Write(
                "HOOK.REJECT",
                "HWND=0.");

            return;
        }

        _pendingHwnd =
            hwnd;

        DiagnosticLog.Write(
            "HOOK.PENDING",
            $"hwnd=0x{hwnd.ToInt64():X} " +
            $"dispatching={_dispatching}");

        if (_dispatching)
            return;

        _dispatching =
            true;

        try
        {
            while (_pendingHwnd != nint.Zero)
            {
                nint nextHwnd =
                    _pendingHwnd;

                _pendingHwnd =
                    nint.Zero;

                DiagnosticLog.Write(
                    "HOOK.RAISE",
                    $"hwnd=0x{nextHwnd.ToInt64():X}");

                try
                {
                    ForegroundWindowChanged?.
                        Invoke(nextHwnd);

                    DiagnosticLog.Write(
                        "HOOK.RAISE_DONE",
                        $"hwnd=0x{nextHwnd.ToInt64():X}");
                }
                catch (Exception ex)
                {
                    DiagnosticLog.WriteException(
                        "HOOK.RAISE_ERROR",
                        ex);
                }
            }
        }
        finally
        {
            _dispatching =
                false;

            DiagnosticLog.Write(
                "HOOK.CALLBACK_END",
                "dispatching=false");
        }
    }

    public void Dispose()
    {
        DiagnosticLog.Write(
            "HOOK.DISPOSE",
            "Dispose called.");

        Stop();

        GC.SuppressFinalize(this);
    }
}
