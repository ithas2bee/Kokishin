using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Hosting;

namespace Kokishin.Tests;

internal static class TestDataProtectionConfiguration
{
    private const string CertificatePassword = "test-only-data-protection-password";
    private static readonly string TestDirectory = Path.Combine(
        Path.GetTempPath(),
        $"kokishin-data-protection-tests-{Guid.NewGuid():N}");
    private static readonly string CertificatePath = Path.Combine(TestDirectory, "test-keys.pfx");
    private static readonly string KeysPath = Path.Combine(TestDirectory, "keys");

    static TestDataProtectionConfiguration()
    {
        Directory.CreateDirectory(TestDirectory);
        using var key = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=Kokishin Test Data Protection",
            key,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-5),
            DateTimeOffset.UtcNow.AddYears(1));
        File.WriteAllBytes(
            CertificatePath,
            certificate.Export(X509ContentType.Pfx, CertificatePassword));
    }

    public static void Configure(IWebHostBuilder builder)
    {
        builder.UseSetting("DataProtection:KeysPath", KeysPath);
        builder.UseSetting("DataProtection:CertificatePath", CertificatePath);
        builder.UseSetting("DataProtection:CertificatePassword", CertificatePassword);
    }
}
