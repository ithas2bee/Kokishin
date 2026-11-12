using Kokishin.Domain.Profiles;

namespace Kokishin.Application.Profiles;

public interface IProfileService
{
    Task<Profile?> GetOwnProfileAsync(Guid accountId, CancellationToken cancellationToken = default);

    Task<Profile> UpdateOwnProfileAsync(
        Guid accountId,
        string displayName,
        string? description = null,
        CancellationToken cancellationToken = default);
}
