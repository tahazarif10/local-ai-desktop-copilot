using System;
using System.IO;
using System.Text.Json;

namespace LocalCopilot_App.Diagnostics;

internal sealed record DiagnosticSession(
    string SessionId,
    string DirectoryPath,
    string LogFilePath);

internal sealed class DiagnosticLaunchDescriptor
{
    public int SchemaVersion
    {
        get;
        init;
    }

    public string? SessionId
    {
        get;
        init;
    }

    public string? SessionDirectory
    {
        get;
        init;
    }

    public DateTimeOffset CreatedUtc
    {
        get;
        init;
    }

    public DateTimeOffset ExpiresUtc
    {
        get;
        init;
    }
}

internal static class DiagnosticSessionParser
{
    public const int SchemaVersion =
        1;

    public const string ApplicationLogFileName =
        "app.log";

    public const string ArgumentPrefix =
        "--localcopilot-diagnostics=";

    private const int MaximumTokenLength =
        8192;

    private const int MaximumJsonLength =
        4096;

    private const int MaximumPathLength =
        2048;

    private static readonly TimeSpan MaximumClockSkew =
        TimeSpan.FromMinutes(
            5);

    private static readonly TimeSpan MaximumSessionLifetime =
        TimeSpan.FromHours(
            8);

    public static bool TryParse(
        string? launchArguments,
        DateTimeOffset utcNow,
        out DiagnosticSession? session)
    {
        session =
            null;

        if (string.IsNullOrWhiteSpace(
                launchArguments))
        {
            return false;
        }

        string? token =
            null;

        string[] arguments =
            launchArguments.Split(
                [' ', '\t', '\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries);

        foreach (string argument in arguments)
        {
            if (!argument.StartsWith(
                    ArgumentPrefix,
                    StringComparison.Ordinal))
            {
                continue;
            }

            if (token is not null)
            {
                return false;
            }

            token =
                argument[
                    ArgumentPrefix.Length..];
        }

        if (
            string.IsNullOrWhiteSpace(
                token) ||
            token.Length >
                MaximumTokenLength)
        {
            return false;
        }

        try
        {
            byte[] json =
                DecodeBase64Url(
                    token);

            if (json.Length >
                MaximumJsonLength)
            {
                return false;
            }

            DiagnosticLaunchDescriptor? descriptor =
                JsonSerializer.Deserialize<DiagnosticLaunchDescriptor>(
                    json);

            if (!TryValidate(
                    descriptor,
                    utcNow,
                    out Guid sessionId,
                    out string sessionDirectory))
            {
                return false;
            }

            session =
                new DiagnosticSession(
                    sessionId.ToString(
                        "D"),
                    sessionDirectory,
                    Path.Combine(
                        sessionDirectory,
                        ApplicationLogFileName));

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryValidate(
        DiagnosticLaunchDescriptor? descriptor,
        DateTimeOffset utcNow,
        out Guid sessionId,
        out string sessionDirectory)
    {
        sessionId =
            Guid.Empty;

        sessionDirectory =
            string.Empty;

        if (
            descriptor is null ||
            descriptor.SchemaVersion !=
                SchemaVersion ||
            !Guid.TryParseExact(
                descriptor.SessionId,
                "D",
                out sessionId) ||
            string.IsNullOrWhiteSpace(
                descriptor.SessionDirectory) ||
            descriptor.SessionDirectory.Length >
                MaximumPathLength ||
            !Path.IsPathFullyQualified(
                descriptor.SessionDirectory) ||
            descriptor.CreatedUtc ==
                default ||
            descriptor.ExpiresUtc ==
                default ||
            descriptor.ExpiresUtc <=
                descriptor.CreatedUtc ||
            descriptor.ExpiresUtc -
                descriptor.CreatedUtc >
                MaximumSessionLifetime ||
            descriptor.CreatedUtc >
                utcNow +
                MaximumClockSkew ||
            descriptor.ExpiresUtc <=
                utcNow)
        {
            return false;
        }

        string fullPath =
            Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(
                    descriptor.SessionDirectory));

        string? directoryName =
            Path.GetFileName(
                fullPath);

        if (
            string.IsNullOrWhiteSpace(
                directoryName) ||
            !directoryName.EndsWith(
                sessionId.ToString(
                    "N"),
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        sessionDirectory =
            fullPath;

        return true;
    }

    private static byte[] DecodeBase64Url(
        string token)
    {
        string normalized =
            token
                .Replace(
                    '-',
                    '+')
                .Replace(
                    '_',
                    '/');

        normalized =
            (normalized.Length % 4) switch
            {
                0 => normalized,
                2 => normalized + "==",
                3 => normalized + "=",
                _ => throw new FormatException(
                    "Invalid diagnostic token length.")
            };

        return Convert.FromBase64String(
            normalized);
    }
}
