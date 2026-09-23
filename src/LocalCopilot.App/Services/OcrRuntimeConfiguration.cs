using System;
using System.IO;
using System.Security.Cryptography;

namespace LocalCopilot_App.Services;

internal sealed record OcrRuntimeConfiguration(
    Uri ServerUri,
    string ServerCertificateSha256,
    string AuthenticationKeyFile)
{
    public const string ServerUriVariable =
        "LOCALCOPILOT_OCR_SERVER_URI";
    public const string CertificateSha256Variable =
        "LOCALCOPILOT_OCR_SERVER_CERT_SHA256";
    public const string AuthenticationKeyFileVariable =
        "LOCALCOPILOT_OCR_AUTH_KEY_FILE";

    public static bool TryLoad(
        out OcrRuntimeConfiguration? configuration)
    {
        configuration = null;

        string? uriText =
            Environment.GetEnvironmentVariable(
                ServerUriVariable);
        string? certificate =
            Environment.GetEnvironmentVariable(
                CertificateSha256Variable);
        string? authenticationKeyFile =
            Environment.GetEnvironmentVariable(
                AuthenticationKeyFileVariable);

        if (string.IsNullOrWhiteSpace(uriText) ||
            string.IsNullOrWhiteSpace(certificate) ||
            string.IsNullOrWhiteSpace(authenticationKeyFile))
        {
            return false;
        }

        if (!Uri.TryCreate(
                uriText.Trim(),
                UriKind.Absolute,
                out Uri? uri) ||
            !string.Equals(
                uri.Scheme,
                Uri.UriSchemeHttps,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string certificatePin =
            certificate.Trim();

        if (certificatePin.Length != 64)
        {
            return false;
        }

        try
        {
            _ = Convert.FromHexString(certificatePin);
        }
        catch (FormatException)
        {
            return false;
        }

        string keyPath;
        try
        {
            keyPath =
                Path.GetFullPath(
                    authenticationKeyFile.Trim());
        }
        catch
        {
            return false;
        }

        if (!Path.IsPathFullyQualified(keyPath) ||
            !File.Exists(keyPath))
        {
            return false;
        }

        configuration =
            new OcrRuntimeConfiguration(
                uri,
                certificatePin.ToLowerInvariant(),
                keyPath);

        return true;
    }

    public OcrServerTransport CreateTransport()
    {
        byte[] authenticationKey =
            ReadAuthenticationKey();

        try
        {
            return new OcrServerTransport(
                new OcrServerTransportOptions(
                    ServerUri,
                    ServerCertificateSha256,
                    authenticationKey,
                    OcrTransportLimits.M4_2_3Default));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(
                authenticationKey);
        }
    }

    private byte[] ReadAuthenticationKey()
    {
        string text =
            File.ReadAllText(
                    AuthenticationKeyFile,
                    System.Text.Encoding.ASCII)
                .Trim();

        if (text.Length < 64 ||
            text.Length > 128 ||
            text.Length % 2 != 0)
        {
            throw new InvalidDataException(
                "OCR authentication key file has invalid length.");
        }

        byte[] key;
        try
        {
            key =
                Convert.FromHexString(text);
        }
        catch (FormatException exception)
        {
            throw new InvalidDataException(
                "OCR authentication key file is not hexadecimal.",
                exception);
        }

        if (key.Length < 32 || key.Length > 64)
        {
            CryptographicOperations.ZeroMemory(key);
            throw new InvalidDataException(
                "OCR authentication key must contain 32-64 bytes.");
        }

        return key;
    }
}
