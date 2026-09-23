using System;
using System.Buffers;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

namespace LocalCopilot_App.Services;

internal sealed record OcrServerTransportOptions(
    Uri BaseUri,
    string ServerCertificateSha256,
    byte[] AuthenticationKey,
    OcrTransportLimits Limits)
{
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(BaseUri);
        ArgumentNullException.ThrowIfNull(AuthenticationKey);
        ArgumentNullException.ThrowIfNull(Limits);

        if (!string.Equals(BaseUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("OCR server transport requires HTTPS.", nameof(BaseUri));
        if (!BaseUri.IsAbsoluteUri)
            throw new ArgumentException("OCR server URI must be absolute.", nameof(BaseUri));
        if (string.IsNullOrWhiteSpace(ServerCertificateSha256) ||
            ServerCertificateSha256.Trim().Length != 64)
            throw new ArgumentException("Server certificate pin must be SHA-256 hex.", nameof(ServerCertificateSha256));
        if (AuthenticationKey.Length < 32 || AuthenticationKey.Length > 64)
            throw new ArgumentOutOfRangeException(nameof(AuthenticationKey));

        Limits.Validate();

        try
        {
            _ = Convert.FromHexString(ServerCertificateSha256.Trim());
        }
        catch (FormatException exception)
        {
            throw new ArgumentException(
                "Server certificate pin must be hexadecimal.",
                nameof(ServerCertificateSha256),
                exception);
        }
    }
}

internal sealed class OcrServerTransport : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly OcrTransportLimits _limits;
    private readonly byte[] _authenticationKey;
    private readonly byte[] _serverCertificateSha256;
    private int _disposed;

    public OcrServerTransport(OcrServerTransportOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        _limits = options.Limits;
        _authenticationKey = (byte[])options.AuthenticationKey.Clone();
        _serverCertificateSha256 =
            Convert.FromHexString(options.ServerCertificateSha256.Trim());

        HttpClientHandler handler = new();
        handler.ServerCertificateCustomValidationCallback =
            ValidateServerCertificate;

        _httpClient =
            new HttpClient(handler, disposeHandler: true)
            {
                BaseAddress = options.BaseUri,
                Timeout = Timeout.InfiniteTimeSpan
            };
    }

    public async Task<OcrTransportResponsePayload> SendAsync(
        OcrTransportRequestPayload request,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref _disposed) != 0,
            this);

        ArgumentNullException.ThrowIfNull(request);

        DateTimeOffset now = DateTimeOffset.UtcNow;
        byte[] body =
            OcrTransportProtocol.SerializeRequest(
                request,
                now,
                _limits);

        try
        {
            long timestamp = now.ToUnixTimeSeconds();
            string nonce =
                Convert.ToHexString(
                    RandomNumberGenerator.GetBytes(16))
                .ToLowerInvariant();
            string bodyHash =
                OcrTransportProtocol.ComputeBodySha256Hex(body);
            string signature =
                OcrTransportProtocol.ComputeRequestSignatureBase64(
                    _authenticationKey,
                    timestamp,
                    nonce,
                    bodyHash);

            using HttpRequestMessage message =
                new(
                    HttpMethod.Post,
                    OcrTransportProtocol.RequestPath);

            message.Content = new ByteArrayContent(body);
            message.Content.Headers.ContentType =
                new MediaTypeHeaderValue("application/octet-stream");

            message.Headers.TryAddWithoutValidation("X-LC-KeyId", "v1");
            message.Headers.TryAddWithoutValidation(
                "X-LC-Timestamp",
                timestamp.ToString(System.Globalization.CultureInfo.InvariantCulture));
            message.Headers.TryAddWithoutValidation("X-LC-Nonce", nonce);
            message.Headers.TryAddWithoutValidation(
                "X-LC-Body-SHA256",
                bodyHash);
            message.Headers.TryAddWithoutValidation(
                "X-LC-Signature",
                signature);

            TimeSpan remaining = request.Deadline - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero)
                throw new TimeoutException("OCR request deadline expired before dispatch.");

            using CancellationTokenSource deadline =
                CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken);
            deadline.CancelAfter(remaining);

            using HttpResponseMessage response =
                await _httpClient.SendAsync(
                        message,
                        HttpCompletionOption.ResponseHeadersRead,
                        deadline.Token)
                    .ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
                throw new UnauthorizedAccessException("OCR server authentication failed.");
            if (response.StatusCode == HttpStatusCode.RequestEntityTooLarge)
                throw new InvalidDataException("OCR server rejected request size.");
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                throw new InvalidOperationException("OCR server is busy.");
            if (response.StatusCode != HttpStatusCode.OK)
                throw new HttpRequestException(
                    $"OCR server returned HTTP {(int)response.StatusCode}.");

            long? contentLength = response.Content.Headers.ContentLength;
            if (contentLength is null ||
                contentLength <= 0 ||
                contentLength > _limits.MaxResponseBytes)
            {
                throw new InvalidDataException("OCR response length is invalid.");
            }

            byte[] responseBody =
                await ReadBoundedAsync(
                        response,
                        (int)contentLength.Value,
                        deadline.Token)
                    .ConfigureAwait(false);

            try
            {
                OcrTransportResponsePayload parsed =
                    OcrTransportProtocol.DeserializeResponse(
                        responseBody,
                        _limits);

                if (parsed.RequestId != request.RequestId ||
                    parsed.EpochId != request.EpochId)
                {
                    parsed.Dispose();
                    throw new InvalidDataException(
                        "OCR response identity does not match the request.");
                }

                return parsed;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(responseBody);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(body);
        }
    }

    private async Task<byte[]> ReadBoundedAsync(
        HttpResponseMessage response,
        int expectedLength,
        CancellationToken cancellationToken)
    {
        byte[] buffer =
            ArrayPool<byte>.Shared.Rent(expectedLength);
        byte[] result =
            new byte[expectedLength];

        try
        {
            using Stream stream =
                await response.Content
                    .ReadAsStreamAsync(cancellationToken)
                    .ConfigureAwait(false);

            int offset = 0;
            while (offset < expectedLength)
            {
                int read =
                    await stream.ReadAsync(
                            buffer.AsMemory(
                                0,
                                Math.Min(
                                    buffer.Length,
                                    expectedLength - offset)),
                            cancellationToken)
                        .ConfigureAwait(false);

                if (read == 0)
                    throw new EndOfStreamException(
                        "OCR response ended before Content-Length.");

                buffer.AsSpan(0, read)
                    .CopyTo(result.AsSpan(offset));
                offset += read;
            }

            int trailing =
                await stream.ReadAsync(
                        buffer.AsMemory(0, 1),
                        cancellationToken)
                    .ConfigureAwait(false);

            if (trailing != 0)
                throw new InvalidDataException(
                    "OCR response exceeded Content-Length.");

            return result;
        }
        catch
        {
            CryptographicOperations.ZeroMemory(result);
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(
                buffer.AsSpan(0, Math.Min(buffer.Length, expectedLength)));
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private bool ValidateServerCertificate(
        HttpRequestMessage _,
        X509Certificate2? certificate,
        X509Chain? __,
        SslPolicyErrors ___)
    {
        if (certificate is null)
            return false;

        byte[] actual =
            SHA256.HashData(
                certificate.RawData);

        try
        {
            return CryptographicOperations.FixedTimeEquals(
                actual,
                _serverCertificateSha256);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(actual);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _httpClient.Dispose();
        CryptographicOperations.ZeroMemory(_authenticationKey);
        CryptographicOperations.ZeroMemory(_serverCertificateSha256);
        GC.SuppressFinalize(this);
    }

    ~OcrServerTransport()
    {
        Dispose();
    }
}
