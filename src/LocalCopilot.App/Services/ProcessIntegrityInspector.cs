using System;
using System.Runtime.InteropServices;

namespace LocalCopilot_App.Services;

internal sealed class ProcessIntegrityInspector
{
    private const uint ProcessQueryLimitedInformation =
        0x1000;

    private const uint TokenQuery =
        0x0008;

    private const int TokenIntegrityLevel =
        25;

    private const int ErrorInvalidData =
        13;

    private const int AccessDenied =
        unchecked((int)0x80070005);

    private readonly uint _currentIntegrityLevel;

    private ProcessIntegrityInspector(
        uint currentIntegrityLevel)
    {
        _currentIntegrityLevel =
            currentIntegrityLevel;
    }

    public static bool TryCreate(
        out ProcessIntegrityInspector? inspector,
        out int hresult)
    {
        inspector = null;

        if (!TryReadIntegrityLevel(
                GetCurrentProcess(),
                out uint integrityLevel,
                out hresult))
        {
            return false;
        }

        inspector =
            new ProcessIntegrityInspector(
                integrityLevel);

        return true;
    }

    public bool TryIsSameOrLowerIntegrity(
        uint processId,
        out bool mayRead,
        out int hresult)
    {
        mayRead = false;

        nint process = OpenProcess(
            ProcessQueryLimitedInformation,
            inheritHandle: false,
            processId);

        if (process == nint.Zero)
        {
            hresult = HResultFromWin32(
                Marshal.GetLastWin32Error());

            return false;
        }

        try
        {
            if (!TryReadIntegrityLevel(
                    process,
                    out uint targetIntegrityLevel,
                    out hresult))
            {
                return false;
            }

            mayRead =
                targetIntegrityLevel <=
                _currentIntegrityLevel;

            if (!mayRead)
            {
                hresult = AccessDenied;
            }

            return true;
        }
        finally
        {
            _ = CloseHandle(process);
        }
    }

    private static bool TryReadIntegrityLevel(
        nint process,
        out uint integrityLevel,
        out int hresult)
    {
        integrityLevel = 0;
        hresult = 0;

        if (!OpenProcessToken(
                process,
                TokenQuery,
                out nint token))
        {
            hresult = HResultFromWin32(
                Marshal.GetLastWin32Error());

            return false;
        }

        try
        {
            _ = GetTokenInformation(
                token,
                TokenIntegrityLevel,
                nint.Zero,
                tokenInformationLength: 0,
                out uint requiredLength);

            if (requiredLength == 0)
            {
                hresult = HResultFromWin32(
                    Marshal.GetLastWin32Error());

                return false;
            }

            nint buffer = Marshal.AllocHGlobal(
                checked((int)requiredLength));

            try
            {
                if (!GetTokenInformation(
                        token,
                        TokenIntegrityLevel,
                        buffer,
                        requiredLength,
                        out _))
                {
                    hresult = HResultFromWin32(
                        Marshal.GetLastWin32Error());

                    return false;
                }

                nint sid = Marshal.ReadIntPtr(buffer);

                if (sid == nint.Zero)
                {
                    hresult = HResultFromWin32(
                        ErrorInvalidData);

                    return false;
                }

                nint countPointer =
                    GetSidSubAuthorityCount(sid);

                if (countPointer == nint.Zero)
                {
                    hresult = HResultFromWin32(
                        ErrorInvalidData);

                    return false;
                }

                byte count = Marshal.ReadByte(
                    countPointer);

                if (count == 0)
                {
                    hresult = HResultFromWin32(
                        ErrorInvalidData);

                    return false;
                }

                nint integrityPointer =
                    GetSidSubAuthority(
                        sid,
                        checked((uint)(count - 1)));

                if (integrityPointer == nint.Zero)
                {
                    hresult = HResultFromWin32(
                        ErrorInvalidData);

                    return false;
                }

                integrityLevel = unchecked(
                    (uint)Marshal.ReadInt32(
                        integrityPointer));

                return true;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        finally
        {
            _ = CloseHandle(token);
        }
    }

    private static int HResultFromWin32(
        int error)
    {
        return error <= 0
            ? error
            : unchecked(
                (int)(0x80070000u |
                ((uint)error & 0xFFFFu)));
    }

    [DllImport(
        "kernel32.dll",
        ExactSpelling = true)]
    private static extern nint GetCurrentProcess();

    [DllImport(
        "kernel32.dll",
        ExactSpelling = true,
        SetLastError = true)]
    private static extern nint OpenProcess(
        uint desiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandle,
        uint processId);

    [DllImport(
        "kernel32.dll",
        ExactSpelling = true,
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(
        nint handle);

    [DllImport(
        "advapi32.dll",
        ExactSpelling = true,
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(
        nint processHandle,
        uint desiredAccess,
        out nint tokenHandle);

    [DllImport(
        "advapi32.dll",
        ExactSpelling = true,
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(
        nint tokenHandle,
        int tokenInformationClass,
        nint tokenInformation,
        uint tokenInformationLength,
        out uint returnLength);

    [DllImport(
        "advapi32.dll",
        ExactSpelling = true)]
    private static extern nint GetSidSubAuthorityCount(
        nint sid);

    [DllImport(
        "advapi32.dll",
        ExactSpelling = true)]
    private static extern nint GetSidSubAuthority(
        nint sid,
        uint subAuthorityIndex);
}
