using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Memory;

namespace Kokishin.Api;

internal sealed class ProtectedMemoryTicketStore : ITicketStore
{
    private const string ProtectorPurpose = "Kokishin.Authentication.SessionTicket.v1";

    private readonly IMemoryCache _cache;
    private readonly IDataProtector _protector;

    public ProtectedMemoryTicketStore(IMemoryCache cache, IDataProtectionProvider dataProtectionProvider)
    {
        _cache = cache;
        _protector = dataProtectionProvider.CreateProtector(ProtectorPurpose);
    }

    public Task<string> StoreAsync(AuthenticationTicket ticket) =>
        StoreAsync(ticket, CancellationToken.None);

    public Task<string> StoreAsync(AuthenticationTicket ticket, CancellationToken cancellationToken)
    {
        var key = Guid.NewGuid().ToString("N");
        Store(key, ticket);
        return Task.FromResult(key);
    }

    public Task RenewAsync(string key, AuthenticationTicket ticket) =>
        RenewAsync(key, ticket, CancellationToken.None);

    public Task RenewAsync(string key, AuthenticationTicket ticket, CancellationToken cancellationToken)
    {
        Store(key, ticket);
        return Task.CompletedTask;
    }

    public Task<AuthenticationTicket?> RetrieveAsync(string key) =>
        RetrieveAsync(key, CancellationToken.None);

    public Task<AuthenticationTicket?> RetrieveAsync(string key, CancellationToken cancellationToken)
    {
        if (!_cache.TryGetValue(key, out byte[]? protectedTicket) || protectedTicket is null)
        {
            return Task.FromResult<AuthenticationTicket?>(null);
        }

        try
        {
            return Task.FromResult(TicketSerializer.Default.Deserialize(_protector.Unprotect(protectedTicket)));
        }
        catch (CryptographicException)
        {
            _cache.Remove(key);
            return Task.FromResult<AuthenticationTicket?>(null);
        }
    }

    public Task RemoveAsync(string key) => RemoveAsync(key, CancellationToken.None);

    public Task RemoveAsync(string key, CancellationToken cancellationToken)
    {
        _cache.Remove(key);
        return Task.CompletedTask;
    }

    private void Store(string key, AuthenticationTicket ticket)
    {
        var expiration = ticket.Properties.ExpiresUtc ?? DateTimeOffset.UtcNow.AddMinutes(30);
        _cache.Set(
            key,
            _protector.Protect(TicketSerializer.Default.Serialize(ticket)),
            expiration);
    }
}
