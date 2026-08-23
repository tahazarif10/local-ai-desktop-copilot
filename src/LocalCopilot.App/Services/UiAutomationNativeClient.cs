using System;
using System.Runtime.InteropServices;

namespace LocalCopilot_App.Services;

/// <summary>
/// Minimal ABI boundary for the M3.1 root probe. The COM interface pointer and
/// every returned UIA element remain owned by the dedicated MTA worker thread.
/// </summary>
internal sealed class UiAutomationNativeClient :
    IDisposable
{
    private const uint ClsContextInProcessServer =
        0x1;

    // IUnknown occupies slots 0-2. ElementFromHandle is the fourth member of
    // IUIAutomation, so its inherited IUIAutomation2 slot is 6.
    private const int ElementFromHandleSlot =
        6;

    // IUIAutomation contributes slots 3-57. IUIAutomation2 setters for the
    // connection and transaction timeouts are slots 61 and 63 respectively.
    private const int PutConnectionTimeoutSlot =
        61;

    private const int PutTransactionTimeoutSlot =
        63;

    private const int GenericFailure =
        unchecked((int)0x80004005);

    private static readonly Guid CUIAutomation8ClassId =
        new("E22AD333-B25F-460C-83D0-0581107395C9");

    private static readonly Guid IUIAutomation2InterfaceId =
        new("34723AFF-0C9D-49D0-9896-7AB52DF8CD8A");

    private nint _instance;

    private readonly ElementFromHandleDelegate
        _elementFromHandle;

    private readonly PutTimeoutDelegate
        _putConnectionTimeout;

    private readonly PutTimeoutDelegate
        _putTransactionTimeout;

    private UiAutomationNativeClient(
        nint instance)
    {
        _instance = instance;

        _elementFromHandle =
            Bind<ElementFromHandleDelegate>(
                instance,
                ElementFromHandleSlot);

        _putConnectionTimeout =
            Bind<PutTimeoutDelegate>(
                instance,
                PutConnectionTimeoutSlot);

        _putTransactionTimeout =
            Bind<PutTimeoutDelegate>(
                instance,
                PutTransactionTimeoutSlot);
    }

    public static int InitializeMta()
    {
        return CoInitializeEx(
            nint.Zero,
            coInit: 0);
    }

    public static void UninitializeMta()
    {
        CoUninitialize();
    }

    public static int TryCreate(
        uint connectionTimeoutMilliseconds,
        uint transactionTimeoutMilliseconds,
        out UiAutomationNativeClient? client)
    {
        client = null;

        Guid classId = CUIAutomation8ClassId;
        Guid interfaceId = IUIAutomation2InterfaceId;

        int hresult = CoCreateInstance(
            ref classId,
            nint.Zero,
            ClsContextInProcessServer,
            ref interfaceId,
            out nint instance);

        if (hresult < 0)
        {
            return hresult;
        }

        try
        {
            client = new UiAutomationNativeClient(instance);

            hresult = client._putConnectionTimeout(
                client._instance,
                connectionTimeoutMilliseconds);

            if (hresult >= 0)
            {
                hresult = client._putTransactionTimeout(
                    client._instance,
                    transactionTimeoutMilliseconds);
            }

            if (hresult >= 0)
            {
                return hresult;
            }
        }
        catch
        {
            hresult = GenericFailure;
        }

        if (client is not null)
        {
            client.Dispose();
        }
        else if (instance != nint.Zero)
        {
            _ = Marshal.Release(instance);
        }

        client = null;
        return hresult;
    }

    public int ElementFromHandle(
        nint hwnd,
        out nint element)
    {
        if (_instance == nint.Zero)
        {
            element = nint.Zero;
            return GenericFailure;
        }

        return _elementFromHandle(
            _instance,
            hwnd,
            out element);
    }

    public static void ReleaseElement(
        nint element)
    {
        if (element != nint.Zero)
        {
            _ = Marshal.Release(element);
        }
    }

    public void Dispose()
    {
        if (_instance == nint.Zero)
        {
            return;
        }

        _ = Marshal.Release(_instance);
        _instance = nint.Zero;
    }

    private static T Bind<T>(
        nint instance,
        int slot)
        where T : Delegate
    {
        nint vtable = Marshal.ReadIntPtr(instance);
        nint function = Marshal.ReadIntPtr(
            vtable,
            checked(slot * IntPtr.Size));

        return Marshal.GetDelegateForFunctionPointer<T>(
            function);
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int ElementFromHandleDelegate(
        nint instance,
        nint hwnd,
        out nint element);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int PutTimeoutDelegate(
        nint instance,
        uint timeoutMilliseconds);

    [DllImport(
        "ole32.dll",
        ExactSpelling = true)]
    private static extern int CoInitializeEx(
        nint reserved,
        uint coInit);

    [DllImport(
        "ole32.dll",
        ExactSpelling = true)]
    private static extern void CoUninitialize();

    [DllImport(
        "ole32.dll",
        ExactSpelling = true)]
    private static extern int CoCreateInstance(
        ref Guid classId,
        nint outerUnknown,
        uint classContext,
        ref Guid interfaceId,
        out nint instance);
}
