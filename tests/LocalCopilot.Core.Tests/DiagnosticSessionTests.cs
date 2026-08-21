using LocalCopilot_App.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text;
using System.Text.Json;

namespace LocalCopilot.Core.Tests;

[TestClass]
public sealed class DiagnosticSessionTests
{
    [TestCleanup]
    public void Cleanup()
    {
        DiagnosticLog.ResetForTests();
    }

    [TestMethod]
    public void TryParse_ValidProcessArguments_ResolvesSessionLog()
    {
        DateTimeOffset now =
            new(
                2026,
                8,
                21,
                12,
                0,
                0,
                TimeSpan.Zero);

        Guid sessionId =
            Guid.NewGuid();

        string sessionDirectory =
            CreateSessionDirectory(
                sessionId);

        IReadOnlyList<string> arguments =
            CreateArguments(
                CreateDescriptor(
                    sessionId,
                    sessionDirectory,
                    now));

        bool parsed =
            DiagnosticSessionParser.TryParse(
                arguments,
                now,
                out DiagnosticSession? session);

        Assert.IsTrue(
            parsed);

        Assert.IsNotNull(
            session);

        Assert.AreEqual(
            sessionId.ToString(
                "D"),
            session!.SessionId);

        Assert.AreEqual(
            Path.GetFullPath(
                sessionDirectory),
            session.DirectoryPath);

        Assert.AreEqual(
            Path.Combine(
                sessionDirectory,
                DiagnosticLog.ApplicationLogFileName),
            session.LogFilePath);
    }

    [TestMethod]
    public void TryParse_MissingDuplicateMalformedOrExpiredToken_FailsClosed()
    {
        DateTimeOffset now =
            DateTimeOffset.UtcNow;

        Guid sessionId =
            Guid.NewGuid();

        IReadOnlyList<string> valid =
            CreateArguments(
                CreateDescriptor(
                    sessionId,
                    CreateSessionDirectory(
                        sessionId),
                    now));

        DiagnosticLaunchDescriptor expired =
            CreateDescriptor(
                sessionId,
                CreateSessionDirectory(
                    sessionId),
                now -
                    TimeSpan.FromHours(2));

        expired =
            new DiagnosticLaunchDescriptor
            {
                SchemaVersion =
                    expired.SchemaVersion,
                SessionId =
                    expired.SessionId,
                SessionDirectory =
                    expired.SessionDirectory,
                CreatedUtc =
                    expired.CreatedUtc,
                ExpiresUtc =
                    now -
                    TimeSpan.FromMinutes(1)
            };

        Assert.IsFalse(
            TryParse(
                null,
                now));

        Assert.IsFalse(
            TryParse(
                [
                    "LocalCopilot.App.exe",
                    valid[^1],
                    valid[^1]
                ],
                now));

        Assert.IsFalse(
            TryParse(
                [
                    DiagnosticSessionParser.ArgumentPrefix +
                    "not-base64"
                ],
                now));

        Assert.IsFalse(
            TryParse(
                CreateArguments(
                    expired),
                now));
    }

    [TestMethod]
    public void TryParse_RelativeOrMismatchedSessionDirectory_FailsClosed()
    {
        DateTimeOffset now =
            DateTimeOffset.UtcNow;

        Guid sessionId =
            Guid.NewGuid();

        DiagnosticLaunchDescriptor relative =
            CreateDescriptor(
                sessionId,
                Path.Combine(
                    "relative",
                    sessionId.ToString(
                        "N")),
                now);

        DiagnosticLaunchDescriptor mismatched =
            CreateDescriptor(
                sessionId,
                CreateSessionDirectory(
                    Guid.NewGuid()),
                now);

        Assert.IsFalse(
            TryParse(
                CreateArguments(
                    relative),
                now));

        Assert.IsFalse(
            TryParse(
                CreateArguments(
                    mismatched),
                now));
    }

    [TestMethod]
    public void DiagnosticLog_InvalidLaunch_IsDisabledAndInitializationIsOneShot()
    {
        DateTimeOffset now =
            DateTimeOffset.UtcNow;

        Guid sessionId =
            Guid.NewGuid();

        IReadOnlyList<string> valid =
            CreateArguments(
                CreateDescriptor(
                    sessionId,
                    CreateSessionDirectory(
                        sessionId),
                    now));

        DiagnosticLog.Initialize(
            processArguments: null,
            utcNow: now);

        DiagnosticLog.Initialize(
            valid,
            now);

        Assert.IsFalse(
            DiagnosticLog.IsEnabled);

        Assert.IsNull(
            DiagnosticLog.CurrentLogFilePath);
    }

    [TestMethod]
    public void DiagnosticLog_ValidLaunch_WritesBoundedSingleLineSessionLog()
    {
        DateTimeOffset now =
            DateTimeOffset.UtcNow;

        Guid sessionId =
            Guid.NewGuid();

        string sessionDirectory =
            CreateSessionDirectory(
                sessionId);

        try
        {
            DiagnosticLog.Initialize(
                CreateArguments(
                    CreateDescriptor(
                        sessionId,
                        sessionDirectory,
                        now)),
                now);

            DiagnosticLog.ResetSession();

            DiagnosticLog.Write(
                "TEST.EVENT",
                "first\r\nsecond\tvalue");

            DiagnosticLog.WriteException(
                "TEST.ERROR",
                new InvalidOperationException(
                    "sensitive provider content"),
                "operation=test");

            Assert.IsTrue(
                DiagnosticLog.IsEnabled);

            string expectedPath =
                Path.Combine(
                    sessionDirectory,
                    DiagnosticLog.ApplicationLogFileName);

            Assert.AreEqual(
                expectedPath,
                DiagnosticLog.CurrentLogFilePath);

            string content =
                File.ReadAllText(
                    expectedPath);

            Assert.Contains(
                $"sessionId={sessionId:D}",
                content);

            Assert.Contains(
                "TEST.EVENT | first\\r\\nsecond\\tvalue",
                content);

            Assert.Contains(
                "TEST.ERROR | operation=test " +
                "type=InvalidOperationException hresult=0x",
                content);

            Assert.DoesNotContain(
                "sensitive provider content",
                content);
        }
        finally
        {
            DiagnosticLog.ResetForTests();

            if (Directory.Exists(
                    sessionDirectory))
            {
                Directory.Delete(
                    sessionDirectory,
                    recursive: true);
            }
        }
    }

    [TestMethod]
    public void Sanitize_BoundsAndEscapesDiagnosticFields()
    {
        string sanitized =
            DiagnosticLog.SanitizeForTests(
                "a\r\nb\t\u0001c",
                8);

        Assert.AreEqual(
            "a\\r\\nb\\t",
            sanitized);
    }

    private static bool TryParse(
        IReadOnlyList<string>? arguments,
        DateTimeOffset now)
    {
        return DiagnosticSessionParser.TryParse(
            arguments,
            now,
            out _);
    }

    private static DiagnosticLaunchDescriptor CreateDescriptor(
        Guid sessionId,
        string sessionDirectory,
        DateTimeOffset now)
    {
        return new DiagnosticLaunchDescriptor
        {
            SchemaVersion =
                DiagnosticSessionParser.SchemaVersion,
            SessionId =
                sessionId.ToString(
                    "D"),
            SessionDirectory =
                sessionDirectory,
            CreatedUtc =
                now,
            ExpiresUtc =
                now +
                TimeSpan.FromHours(4)
        };
    }

    private static IReadOnlyList<string> CreateArguments(
        DiagnosticLaunchDescriptor descriptor)
    {
        byte[] json =
            JsonSerializer.SerializeToUtf8Bytes(
                descriptor);

        string token =
            Convert.ToBase64String(
                    json)
                .TrimEnd('=')
                .Replace(
                    '+',
                    '-')
                .Replace(
                    '/',
                    '_');

        return
        [
            @"C:\Program Files\LocalCopilot\LocalCopilot.App.exe",
            "--unrelated=value",
            DiagnosticSessionParser.ArgumentPrefix +
            token
        ];
    }

    private static string CreateSessionDirectory(
        Guid sessionId)
    {
        return Path.Combine(
            Path.GetTempPath(),
            "LocalCopilot.Core.Tests",
            "session-" +
            sessionId.ToString(
                "N"));
    }
}
