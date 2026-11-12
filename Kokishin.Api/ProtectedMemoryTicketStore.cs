using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Kokishin.Infrastructure.Persistence.Sessions;

namespace Kokishin.Api;

internal sealed class ProtectedSessionTicketStore(
    IDurableSessionTicketStore sessionTicketStore,
    IDataProtectionProvider dataProtectionProvider) : ITicketStore
{
    private const string ProtectorPurpose = "Kokishin.Authentication.SessionTicket.v1";

    private readonly IDataProtector _protector = dataProtectionProvider.CreateProtector(ProtectorPurpose);

    public Task<string> StoreAsync(AuthenticationTicket ticket) =>
        StoreAsync(ticket, CancellationToken.None);

    public async Task<string> StoreAsync(AuthenticationTicket ticket, CancellationToken cancellationToken)
    {
        var key = Guid.NewGuid().ToString("N");
        await StoreAsync(key, ticket, cancellationToken);
        return key;
    }

    public Task RenewAsync(string key, AuthenticationTicket ticket) =>
        RenewAsync(key, ticket, CancellationToken.None);

    public Task RenewAsync(string key, AuthenticationTicket ticket, CancellationToken cancellationToken) =>
        StoreAsync(key, ticket, cancellationToken);

    public Task<AuthenticationTicket?> RetrieveAsync(string key) =>
        RetrieveAsync(key, CancellationToken.None);

    public async Task<AuthenticationTicket?> RetrieveAsync(string key, CancellationToken cancellationToken)
    {
        var protectedTicket = await sessionTicketStore.RetrieveAsync(key, cancellationToken);
        if (protectedTicket is null)
        {
            return null;
        }

        try
        {
            return TicketSerializer.Default.Deserialize(_protector.Unprotect(protectedTicket));
        }
        catch (CryptographicException)
        {
            await sessionTicketStore.RemoveAsync(key, cancellationToken);
            return null;
        }
    }

    public Task RemoveAsync(string key) => RemoveAsync(key, CancellationToken.None);

    public Task RemoveAsync(string key, CancellationToken cancellationToken) =>
        sessionTicketStore.RemoveAsync(key, cancellationToken);

    private Task StoreAsync(string key, AuthenticationTicket ticket, CancellationToken cancellationToken)
    {
        var expiration = ticket.Properties.ExpiresUtc ?? DateTimeOffset.UtcNow.AddMinutes(30);
        return sessionTicketStore.StoreAsync(
            key,
            _protector.Protect(TicketSerializer.Default.Serialize(ticket)),
            expiration,
            cancellationToken);
    }
}
