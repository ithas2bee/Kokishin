namespace Kokishin.Domain.Profiles;

public sealed class Profile
{
    public const int MaxDisplayNameLength = 100;
    public const int MaxDescriptionLength = 500;

    private Profile()
    {
    }

    public Profile(Guid accountId, string displayName, string? description = null)
    {
        if (accountId == Guid.Empty)
        {
            throw new ArgumentException("Account ID is required.", nameof(accountId));
        }

        AccountId = accountId;
        Update(displayName, description);
    }

    public Guid AccountId { get; private set; }

    public string DisplayName { get; private set; } = null!;

    public string? Description { get; private set; }

    public void Update(string displayName, string? description)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new ArgumentException("Display name is required.", nameof(displayName));
        }

        var normalizedDisplayName = displayName.Trim();
        if (normalizedDisplayName.Length > MaxDisplayNameLength)
        {
            throw new ArgumentException(
                $"Display name must be {MaxDisplayNameLength} characters or fewer.",
                nameof(displayName));
        }

        var normalizedDescription = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        if (normalizedDescription?.Length > MaxDescriptionLength)
        {
            throw new ArgumentException(
                $"Description must be {MaxDescriptionLength} characters or fewer.",
                nameof(description));
        }

        DisplayName = normalizedDisplayName;
        Description = normalizedDescription;
    }
}
