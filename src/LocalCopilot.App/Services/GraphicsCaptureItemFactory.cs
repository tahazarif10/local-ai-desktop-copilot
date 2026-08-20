using System;
using System.Runtime.InteropServices;
using Windows.Graphics.Capture;
using WinRT;

namespace LocalCopilot_App.Services;

public sealed class GraphicsCaptureTargetUnavailableException :
    InvalidOperationException
{
    public GraphicsCaptureTargetUnavailableException(
        nint handle,
        Exception innerException)
        : base(
            "The window is not available as a Windows Graphics Capture target.",
            innerException)
    {
        Handle =
            handle;

        HResult =
            innerException.HResult;
    }

    public nint Handle { get; }
}

public static class GraphicsCaptureItemFactory
{
    private const int EInvalidArg =
        unchecked((int)0x80070057);

    private static readonly Guid GraphicsCaptureItemGuid =
        new("79C3F95B-31F7-4EC2-A464-632EF5D30760");

    [ComImport]
    [Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureItemInterop
    {
        nint CreateForWindow(
            nint window,
            in Guid iid);

        nint CreateForMonitor(
            nint monitor,
            in Guid iid);
    }

    public static GraphicsCaptureItem CreateForWindow(
        nint hwnd)
    {
        if (hwnd == nint.Zero)
            throw new ArgumentException(
                "HWND must not be zero.",
                nameof(hwnd));

        IGraphicsCaptureItemInterop interop =
            GraphicsCaptureItem
                .As<IGraphicsCaptureItemInterop>();

        nint itemPointer;

        try
        {
            itemPointer =
                interop.CreateForWindow(
                    hwnd,
                    GraphicsCaptureItemGuid);
        }
        catch (Exception ex)
            when (ex.HResult == EInvalidArg)
        {
            throw new GraphicsCaptureTargetUnavailableException(
                hwnd,
                ex);
        }

        if (itemPointer == nint.Zero)
            throw new InvalidOperationException(
                "CreateForWindow returned a null capture item.");

        try
        {
            return MarshalInterface<GraphicsCaptureItem>
                .FromAbi(itemPointer);
        }
        finally
        {
            Marshal.Release(itemPointer);
        }
    }
}