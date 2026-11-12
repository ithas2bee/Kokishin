using Kokishin.Domain.Profiles;

namespace Kokishin.Application.Profiles;

public sealed class ProfileService(IProfileStore profileStore) : IProfileService
{
    public Task<Profile?> GetOwnProfileAsync(Guid accountId, CancellationToken cancellationToken = default) =>
        profileStore.FindByAccountIdAsync(accountId, cancellationToken);

    public async Task<Profile> UpdateOwnProfileAsync(
        Guid accountId,
        string displayName,
        string? description = null,
        CancellationToken cancellationToken = default)
    {
        var profile = new Profile(accountId, displayName, description);
        await profileStore.SaveAsync(profile, cancellationToken);
        return profile;
    }
}
