using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace LocalCopilot_App.Diagnostics;

public static class DiagnosticLog
{
    public const string ApplicationLogFileName =
        DiagnosticSessionParser.ApplicationLogFileName;

    private const int MaximumAreaLength =
        96;

    private const int MaximumMessageLength =
        4096;

    private static readonly object Gate =
        new();

    private static readonly UTF8Encoding Utf8 =
        new(false);

    private static DiagnosticSession? _session;

    private static bool _initialized;

    public static bool IsEnabled
    {
        get
        {
            lock (Gate)
            {
                return
                    _initialized &&
                    _session is not null;
            }
        }
    }

    public static void Initialize(
        IReadOnlyList<string>? processArguments)
    {
        Initialize(
            processArguments,
            DateTimeOffset.UtcNow);
    }

    public static void ResetSession()
    {
        DiagnosticSession? session =
            GetSession();

        if (session is null)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(
                session.DirectoryPath);

            string header =
                "=== LocalCopilot diagnostic session ===" +
                Environment.NewLine +
                $"schema={DiagnosticSessionParser.SchemaVersion} " +
                $"sessionId={session.SessionId}" +
                Environment.NewLine;

            lock (Gate)
            {
                File.WriteAllText(
                    session.LogFilePath,
                    header,
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
        DiagnosticSession? session =
            GetSession();

        if (session is null)
        {
            return;
        }

        try
        {
            string safeArea =
                Sanitize(
                    area,
                    MaximumAreaLength,
                    "UNKNOWN");

            string safeMessage =
                Sanitize(
                    message,
                    MaximumMessageLength,
                    "none");

            string line =
                $"{DateTimeOffset.Now:O}" +
                $" | MThread={Environment.CurrentManagedThreadId}" +
                $" | {safeArea}" +
                $" | {safeMessage}";

            lock (Gate)
            {
                Directory.CreateDirectory(
                    session.DirectoryPath);

                File.AppendAllText(
                    session.LogFilePath,
                    line + Environment.NewLine,
                    Utf8);
            }

            Debug.WriteLine(
                line);
        }
        catch
        {
            // Diagnostics must never crash the app.
        }
    }

    public static void WriteException(
        string area,
        Exception exception,
        string? metadata = null)
    {
        if (exception is null)
        {
            return;
        }

        string prefix =
            string.IsNullOrWhiteSpace(
                metadata)
                ? string.Empty
                : metadata.Trim() + " ";

        Write(
            area,
            prefix +
            $"type={exception.GetType().Name} " +
            $"hresult=0x{exception.HResult:X8}");
    }

    internal static void Initialize(
        IReadOnlyList<string>? processArguments,
        DateTimeOffset utcNow)
    {
        lock (Gate)
        {
            if (_initialized)
            {
                return;
            }

            _initialized =
                true;

            if (DiagnosticSessionParser.TryParse(
                    processArguments,
                    utcNow,
                    out DiagnosticSession? session))
            {
                _session =
                    session;
            }
        }
    }

    internal static string SanitizeForTests(
        string value,
        int maximumLength)
    {
        return Sanitize(
            value,
            maximumLength,
            "none");
    }

    internal static string? CurrentLogFilePath
    {
        get
        {
            lock (Gate)
            {
                return _session?.LogFilePath;
            }
        }
    }

    internal static void ResetForTests()
    {
        lock (Gate)
        {
            _session =
                null;

            _initialized =
                false;
        }
    }

    private static DiagnosticSession? GetSession()
    {
        lock (Gate)
        {
            return
                _initialized
                    ? _session
                    : null;
        }
    }

    private static string Sanitize(
        string? value,
        int maximumLength,
        string fallback)
    {
        if (
            string.IsNullOrWhiteSpace(
                value) ||
            maximumLength <= 0)
        {
            return fallback;
        }

        StringBuilder builder =
            new(
                Math.Min(
                    value.Length,
                    maximumLength));

        foreach (char character in value)
        {
            if (builder.Length >=
                maximumLength)
            {
                break;
            }

            switch (character)
            {
                case '\r':
                    AppendEscaped(
                        builder,
                        "\\r",
                        maximumLength);
                    break;

                case '\n':
                    AppendEscaped(
                        builder,
                        "\\n",
                        maximumLength);
                    break;

                case '\t':
                    AppendEscaped(
                        builder,
                        "\\t",
                        maximumLength);
                    break;

                default:
                    builder.Append(
                        char.IsControl(
                            character)
                            ? '?'
                            : character);
                    break;
            }
        }

        return builder.Length == 0
            ? fallback
            : builder.ToString();
    }

    private static void AppendEscaped(
        StringBuilder builder,
        string escaped,
        int maximumLength)
    {
        int available =
            maximumLength -
            builder.Length;

        if (available <= 0)
        {
            return;
        }

        builder.Append(
            escaped.AsSpan(
                0,
                Math.Min(
                    escaped.Length,
                    available)));
    }
}
