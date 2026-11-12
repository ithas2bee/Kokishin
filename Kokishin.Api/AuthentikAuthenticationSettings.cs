using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Kokishin.Api;

internal sealed record AuthentikAuthenticationSettings(
    Uri Authority,
    string Audience,
    string ClientId,
    string ClientSecret,
    Uri CallbackUrl)
{
    public static AuthentikAuthenticationSettings? Read(
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var section = configuration.GetSection("Authentication");
        var values = new Dictionary<string, string?>
        {
            ["Authentication:Authority"] = section["Authority"],
            ["Authentication:Audience"] = section["Audience"],
            ["Authentication:ClientId"] = section["ClientId"],
            ["Authentication:ClientSecret"] = section["ClientSecret"],
            ["Authentication:CallbackUrl"] = section["CallbackUrl"]
        };
        var missingSettings = values
            .Where(setting => string.IsNullOrWhiteSpace(setting.Value))
            .Select(setting => setting.Key)
            .ToArray();
        if (missingSettings.Length > 0)
        {
            throw new InvalidOperationException(
                $"Missing required authentication configuration: {string.Join(", ", missingSettings)}.");
        }

        var authority = ParseHttpsUri(values["Authentication:Authority"]!, "Authentication:Authority");
        var callbackUrl = ParseHttpsUri(values["Authentication:CallbackUrl"]!, "Authentication:CallbackUrl");

        return new AuthentikAuthenticationSettings(
            authority,
            values["Authentication:Audience"]!,
            values["Authentication:ClientId"]!,
            values["Authentication:ClientSecret"]!,
            callbackUrl);
    }

    private static Uri ParseHttpsUri(string value, string settingName)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new InvalidOperationException(
                $"{settingName} must be an absolute HTTPS URL without user information, query, or fragment.");
        }

        return uri;
    }
}

internal sealed record PersistentDataProtectionSettings(
    DirectoryInfo KeyStorageDirectory,
    X509Certificate2 KeyEncryptionCertificate)
{
    public static PersistentDataProtectionSettings? Read(
        IConfiguration configuration,
        IHostEnvironment environment,
        bool authenticationEnabled)
    {
        var section = configuration.GetSection("DataProtection");
        var values = new Dictionary<string, string?>
        {
            ["DataProtection:KeysPath"] = section["KeysPath"],
            ["DataProtection:CertificatePath"] = section["CertificatePath"],
            ["DataProtection:CertificatePassword"] = section["CertificatePassword"]
        };
        if (environment.IsDevelopment() && !authenticationEnabled &&
            values.Values.All(string.IsNullOrWhiteSpace))
        {
            return null;
        }

        var missingSettings = values
            .Where(setting => string.IsNullOrWhiteSpace(setting.Value))
            .Select(setting => setting.Key)
            .ToArray();
        if (missingSettings.Length > 0)
        {
            throw new InvalidOperationException(
                $"Missing required data protection configuration: {string.Join(", ", missingSettings)}.");
        }

        var keyStoragePath = values["DataProtection:KeysPath"]!;
        var certificatePath = values["DataProtection:CertificatePath"]!;
        if (!Path.IsPathFullyQualified(keyStoragePath) ||
            !Path.IsPathFullyQualified(certificatePath))
        {
            throw new InvalidOperationException(
                "DataProtection:KeysPath and DataProtection:CertificatePath must be absolute paths.");
        }

        var keyStorageDirectory = Directory.CreateDirectory(keyStoragePath);
        X509Certificate2 certificate;
        try
        {
            certificate = X509CertificateLoader.LoadPkcs12FromFile(
                certificatePath,
                values["DataProtection:CertificatePassword"],
                X509KeyStorageFlags.EphemeralKeySet);
        }
        catch (CryptographicException exception)
        {
            throw new InvalidOperationException(
                "DataProtection:CertificatePath must identify a valid PKCS#12 certificate.",
                exception);
        }

        using var rsaPrivateKey = certificate.GetRSAPrivateKey();
        if (rsaPrivateKey is null ||
            certificate.NotBefore.ToUniversalTime() > DateTime.UtcNow ||
            certificate.NotAfter.ToUniversalTime() <= DateTime.UtcNow)
        {
            certificate.Dispose();
            throw new InvalidOperationException(
                "The data protection certificate must have a private key and be currently valid.");
        }

        return new PersistentDataProtectionSettings(keyStorageDirectory, certificate);
    }
}
