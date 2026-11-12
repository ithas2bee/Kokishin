using Kokishin.Domain.Accounts;

namespace Kokishin.Application.Accounts;

public enum AccountProvisioningOutcome
{
    Created,
    AlreadyExists
}

public sealed record AccountProvisioningResult(
    Account Account,
    AccountProvisioningOutcome Outcome);
