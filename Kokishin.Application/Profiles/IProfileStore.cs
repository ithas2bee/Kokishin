using Kokishin.Domain.Profiles;

namespace Kokishin.Application.Profiles;

public interface IProfileStore
{
    Task<Profile?> FindByAccountIdAsync(Guid accountId, CancellationToken cancellationToken = default);

    Task SaveAsync(Profile profile, CancellationToken cancellationToken = default);
}
