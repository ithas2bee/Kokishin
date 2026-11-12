using System.Security.Cryptography;
using System.Text;

namespace Kokishin.Domain.Accounts;

public sealed class Account
{
    private static readonly Guid UserIdNamespace = Guid.Parse("47fbe87b-685f-49d4-a78b-4533d1bf164d");

    private Account()
    {
    }

    public Account(string issuer, string subject)
    {
        if (string.IsNullOrWhiteSpace(issuer))
        {
            throw new ArgumentException("Issuer is required.", nameof(issuer));
        }

        if (string.IsNullOrWhiteSpace(subject))
        {
            throw new ArgumentException("Subject is required.", nameof(subject));
        }

        Id = CreateId(issuer, subject);
        Issuer = issuer;
        Subject = subject;
    }

    public Guid Id { get; private set; }

    public string Issuer { get; private set; } = null!;

    public string Subject { get; private set; } = null!;

    private static Guid CreateId(string issuer, string subject)
    {
        var namespaceBytes = UserIdNamespace.ToByteArray(bigEndian: true);
        var identityBytes = Encoding.UTF8.GetBytes($"{issuer}\0{subject}");
        var input = new byte[namespaceBytes.Length + identityBytes.Length];
        namespaceBytes.CopyTo(input, 0);
        identityBytes.CopyTo(input, namespaceBytes.Length);
        var hash = SHA256.HashData(input);
        hash[6] = (byte)((hash[6] & 0x0F) | 0x80);
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);
        return new Guid(hash.AsSpan(0, 16), bigEndian: true);
    }
}
