using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

if (args.Length != 4 ||
    args[0] != "--output-dir" ||
    args[2] != "--server-name")
{
    Console.Error.WriteLine(
        "Usage: LocalCopilot.OcrProvisioner --output-dir <path> --server-name <dns-or-ip>");
    return 2;
}

string outputDirectory =
    Path.GetFullPath(args[1]);

string serverName =
    args[3].Trim();

if (string.IsNullOrWhiteSpace(serverName) ||
    serverName.Length > 253)
{
    Console.Error.WriteLine("Server name is invalid.");
    return 2;
}

Directory.CreateDirectory(outputDirectory);

string certPath =
    Path.Combine(outputDirectory, "server-cert.pem");
string privateKeyPath =
    Path.Combine(outputDirectory, "server-key.pem");
string serverAuthKeyPath =
    Path.Combine(outputDirectory, "server-auth-key.hex");
string clientBundleDirectory =
    Path.Combine(outputDirectory, "client-bundle");
string clientAuthKeyPath =
    Path.Combine(clientBundleDirectory, "ocr-auth-key.hex");
string clientConfigPath =
    Path.Combine(clientBundleDirectory, "ocr-client-config.json");

foreach (string path in new[]
{
    certPath,
    privateKeyPath,
    serverAuthKeyPath,
    clientAuthKeyPath,
    clientConfigPath
})
{
    if (File.Exists(path))
    {
        Console.Error.WriteLine(
            $"Refusing to overwrite existing provisioning material: {Path.GetFileName(path)}");
        return 3;
    }
}

Directory.CreateDirectory(clientBundleDirectory);

using RSA rsa =
    RSA.Create(3072);

CertificateRequest request =
    new(
        "CN=LocalCopilot OCR Server",
        rsa,
        HashAlgorithmName.SHA256,
        RSASignaturePadding.Pkcs1);

request.CertificateExtensions.Add(
    new X509BasicConstraintsExtension(
        certificateAuthority: false,
        hasPathLengthConstraint: false,
        pathLengthConstraint: 0,
        critical: true));

request.CertificateExtensions.Add(
    new X509KeyUsageExtension(
        X509KeyUsageFlags.DigitalSignature |
        X509KeyUsageFlags.KeyEncipherment,
        critical: true));

request.CertificateExtensions.Add(
    new X509SubjectKeyIdentifierExtension(
        request.PublicKey,
        critical: false));

SubjectAlternativeNameBuilder san =
    new();

if (IPAddress.TryParse(serverName, out IPAddress? address))
{
    san.AddIpAddress(address);
}
else
{
    san.AddDnsName(serverName);
}

request.CertificateExtensions.Add(
    san.Build());

DateTimeOffset now =
    DateTimeOffset.UtcNow;

using X509Certificate2 certificate =
    request.CreateSelfSigned(
        now.AddDays(-1),
        now.AddYears(2));

File.WriteAllText(
    certPath,
    certificate.ExportCertificatePem(),
    new System.Text.UTF8Encoding(false));

File.WriteAllText(
    privateKeyPath,
    rsa.ExportPkcs8PrivateKeyPem(),
    new System.Text.UTF8Encoding(false));

byte[] authenticationKey =
    RandomNumberGenerator.GetBytes(32);

try
{
    string authenticationKeyHex =
        Convert.ToHexString(authenticationKey)
            .ToLowerInvariant();

    File.WriteAllText(
        serverAuthKeyPath,
        authenticationKeyHex + Environment.NewLine,
        System.Text.Encoding.ASCII);

    File.WriteAllText(
        clientAuthKeyPath,
        authenticationKeyHex + Environment.NewLine,
        System.Text.Encoding.ASCII);

    string certificateSha256 =
        Convert.ToHexString(
                SHA256.HashData(certificate.RawData))
            .ToLowerInvariant();

    var clientConfiguration =
        new
        {
            schema = 1,
            server_name = serverName,
            port = 49321,
            server_certificate_sha256 = certificateSha256,
            authentication_key_file = "ocr-auth-key.hex"
        };

    File.WriteAllText(
        clientConfigPath,
        JsonSerializer.Serialize(
            clientConfiguration,
            new JsonSerializerOptions
            {
                WriteIndented = true
            }) + Environment.NewLine,
        new System.Text.UTF8Encoding(false));

    Console.WriteLine("M4.2.3 OCR PROVISIONING: PASS");
    Console.WriteLine($"server_name={serverName}");
    Console.WriteLine($"certificate_sha256={certificateSha256}");
    Console.WriteLine($"client_bundle={clientBundleDirectory}");
    Console.WriteLine("authentication_key_printed=False");
    Console.WriteLine("private_key_printed=False");
}
finally
{
    CryptographicOperations.ZeroMemory(authenticationKey);
}

return 0;
