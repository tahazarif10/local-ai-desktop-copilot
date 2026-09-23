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
        byte[] encoded =
            File.ReadAllBytes(
                AuthenticationKeyFile);

        try
        {
            int start = 0;
            int end = encoded.Length;

            while (start < end &&
                IsAsciiWhitespace(encoded[start]))
            {
                start++;
            }

            while (end > start &&
                IsAsciiWhitespace(encoded[end - 1]))
            {
                end--;
            }

            int hexLength =
                end - start;

            if (hexLength < 64 ||
                hexLength > 128 ||
                hexLength % 2 != 0)
            {
                throw new InvalidDataException(
                    "OCR authentication key file has invalid length.");
            }

            byte[] key =
                new byte[hexLength / 2];

            try
            {
                for (int index = 0;
                    index < key.Length;
                    index++)
                {
                    int high =
                        HexNibble(
                            encoded[start + index * 2]);

                    int low =
                        HexNibble(
                            encoded[start + index * 2 + 1]);

                    if (high < 0 || low < 0)
                    {
                        throw new InvalidDataException(
                            "OCR authentication key file is not hexadecimal.");
                    }

                    key[index] =
                        (byte)((high << 4) | low);
                }

                return key;
            }
            catch
            {
                CryptographicOperations.ZeroMemory(
                    key);

                throw;
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(
                encoded);
        }
    }

    private static bool IsAsciiWhitespace(
        byte value) =>
        value is
            0x09 or
            0x0A or
            0x0D or
            0x20;

    private static int HexNibble(
        byte value)
    {
        if (value >= (byte)'0' &&
            value <= (byte)'9')
        {
            return value - (byte)'0';
        }

        if (value >= (byte)'a' &&
            value <= (byte)'f')
        {
            return value - (byte)'a' + 10;
        }

        if (value >= (byte)'A' &&
            value <= (byte)'F')
        {
            return value - (byte)'A' + 10;
        }

        return -1;
    }
}
