using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace LocalCopilot_App.Diagnostics;

public static class DiagnosticLog
{
    public const string FilePath =
        @"H:\DevCache\LocalCopilot\m1-3-app.log";

    public const string EnableFlagPath =
        @"H:\DevCache\LocalCopilot\diagnostics.enabled";

    private static readonly object Gate = new();

    private static readonly UTF8Encoding Utf8 =
        new(false);

    public static bool IsEnabled
    {
        get
        {
            try
            {
                return File.Exists(EnableFlagPath);
            }
            catch
            {
                return false;
            }
        }
    }

    public static void ResetSession()
    {
        if (!IsEnabled)
            return;

        try
        {
            Directory.CreateDirectory(
                Path.GetDirectoryName(FilePath)!);

            lock (Gate)
            {
                File.WriteAllText(
                    FilePath,
                    "=== LocalCopilot M1.3 diagnostic session ===" +
                    Environment.NewLine,
                    Utf8);
            }
        }
        catch
        {
            // Diagnostics must never crash the app.
        }
    }

    public static void Write(
        string area,
        string message)
    {
        if (!IsEnabled)
            return;

        try
        {
            string safe =
                message
                    .Replace("\r", "\\r")
                    .Replace("\n", "\\n");

            string line =
                $"{DateTimeOffset.Now:O}" +
                $" | MThread={Environment.CurrentManagedThreadId}" +
                $" | {area}" +
                $" | {safe}";

            lock (Gate)
            {
                File.AppendAllText(
                    FilePath,
                    line + Environment.NewLine,
                    Utf8);
            }

            Debug.WriteLine(line);
        }
        catch
        {
            // Diagnostics must never crash the app.
        }
    }
}
