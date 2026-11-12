using Kokishin.Application;
using Kokishin.Application.Accounts;
using Kokishin.Domain.Accounts;
using Kokishin.Infrastructure.Persistence;
using Kokishin.Infrastructure.Persistence.Accounts;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kokishin.Tests;

public sealed class SqliteAccountStoreTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly ServiceProvider _services;

    public SqliteAccountStoreTests()
    {
        _connection.Open();
        var services = new ServiceCollection();
        services.AddApplication();
        services.AddDbContextFactory<KokishinDbContext>(options => options.UseSqlite(_connection));
        services.AddSingleton<IAccountStore, SqliteAccountStore>();
        _services = services.BuildServiceProvider();

        using var scope = _services.CreateScope();
        scope.ServiceProvider.GetRequiredService<KokishinDbContext>().Database.Migrate();
    }

    [Fact]
    public async Task AddAsync_FindByIdentityAsync_PersistsAccountAndStableId()
    {
        var store = _services.GetRequiredService<IAccountStore>();
        var account = new Account("https://identity.example.test/", "subject-1", AccountRole.Instructor);

        await store.AddAsync(account);
        var retrieved = await store.FindByIdentityAsync(account.Issuer, account.Subject);

        Assert.NotNull(retrieved);
        Assert.Equal(account.Id, retrieved.Id);
        Assert.Equal(AccountRole.Instructor, retrieved.Role);
    }

    [Fact]
    public async Task AddAccountRoleMigration_ExistingAccounts_AreAssignedLearner()
    {
        const string migrationBeforeRole = "20261112070144_AddAccountProfiles";
        const string issuer = "https://identity.example.test/";
        const string subject = "legacy-account";
        var accountId = new Account(issuer, subject, AccountRole.Learner).Id;
        await using var dbContext = await _services
            .GetRequiredService<IDbContextFactory<KokishinDbContext>>()
            .CreateDbContextAsync();

        await dbContext.Database.MigrateAsync(migrationBeforeRole);
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO Accounts (Id, Issuer, Subject, Status) VALUES ({accountId}, {issuer}, {subject}, {(int)AccountStatus.Inactive})");
        await dbContext.Database.MigrateAsync();

        var retrieved = await _services.GetRequiredService<IAccountStore>().FindByIdAsync(accountId);

        Assert.NotNull(retrieved);
        Assert.Equal(AccountRole.Learner, retrieved.Role);
    }

    [Fact]
    public async Task AddAsync_DuplicateIdentity_RejectsSecondAccount()
    {
        var store = _services.GetRequiredService<IAccountStore>();
        await store.AddAsync(new Account("https://identity.example.test/", "subject-1", AccountRole.Learner));

        var duplicate = new Account("https://identity.example.test/", "subject-1", AccountRole.Learner);
        await using var dbContext = await _services
            .GetRequiredService<IDbContextFactory<KokishinDbContext>>()
            .CreateDbContextAsync();
        dbContext.Entry(duplicate).Property(account => account.Id).CurrentValue = Guid.NewGuid();
        dbContext.Accounts.Add(duplicate);

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync());
        Assert.NotNull(exception);
    }

    [Fact]
    public async Task FindByIdentityAsync_DifferentIdentityPairs_ReturnsOnlyMatchingAccounts()
    {
        var store = _services.GetRequiredService<IAccountStore>();
        var firstIssuer = new Account("https://identity-one.example.test/", "subject", AccountRole.Learner);
        var secondIssuer = new Account("https://identity-two.example.test/", "subject", AccountRole.Learner);
        var secondSubject = new Account("https://identity-one.example.test/", "other-subject", AccountRole.Learner);
        await store.AddAsync(firstIssuer);
        await store.AddAsync(secondIssuer);
        await store.AddAsync(secondSubject);

        var firstRetrieved = await store.FindByIdentityAsync(firstIssuer.Issuer, firstIssuer.Subject);
        var secondIssuerRetrieved = await store.FindByIdentityAsync(secondIssuer.Issuer, secondIssuer.Subject);
        var secondSubjectRetrieved = await store.FindByIdentityAsync(secondSubject.Issuer, secondSubject.Subject);

        Assert.Equal(firstIssuer.Id, firstRetrieved?.Id);
        Assert.Equal(secondIssuer.Id, secondIssuerRetrieved?.Id);
        Assert.Equal(secondSubject.Id, secondSubjectRetrieved?.Id);
        Assert.NotEqual(firstIssuer.Id, secondIssuerRetrieved?.Id);
        Assert.NotEqual(firstIssuer.Id, secondSubjectRetrieved?.Id);
    }

    [Fact]
    public async Task ProvisionVerifiedIdentityAsync_RepeatedProvisioningReturnsExistingInactiveAccount()
    {
        var lifecycle = _services.GetRequiredService<IAccountLifecycleService>();

        var first = await lifecycle.ProvisionVerifiedIdentityAsync(
            "https://identity.example.test/",
            "subject-1",
            AccountRole.Instructor);
        var second = await lifecycle.ProvisionVerifiedIdentityAsync(
            "https://identity.example.test/",
            "subject-1",
            AccountRole.Learner);

        Assert.Equal(AccountProvisioningOutcome.Created, first.Outcome);
        Assert.Equal(AccountProvisioningOutcome.AlreadyExists, second.Outcome);
        Assert.Equal(first.Account.Id, second.Account.Id);
        Assert.Equal(AccountRole.Instructor, second.Account.Role);
        Assert.Equal(AccountStatus.Inactive, second.Account.Status);
        Assert.False(await lifecycle.IsActiveAsync(first.Account.Id));
    }

    [Fact]
    public async Task Lifecycle_ActivationAndDeactivationPersistStatus()
    {
        var lifecycle = _services.GetRequiredService<IAccountLifecycleService>();
        var provisioning = await lifecycle.ProvisionVerifiedIdentityAsync(
            "https://identity.example.test/",
            "subject-1",
            AccountRole.Learner);

        await lifecycle.ActivateAsync(provisioning.Account.Id);
        var activeAccount = await _services.GetRequiredService<IAccountStore>()
            .FindByIdAsync(provisioning.Account.Id);

        await lifecycle.DeactivateAsync(provisioning.Account.Id);
        var inactiveAccount = await _services.GetRequiredService<IAccountStore>()
            .FindByIdAsync(provisioning.Account.Id);

        Assert.Equal(AccountStatus.Active, activeAccount?.Status);
        Assert.Equal(AccountStatus.Inactive, inactiveAccount?.Status);
    }

    [Fact]
    public async Task Lifecycle_InvalidStatusTransitionsAreRejected()
    {
        var lifecycle = _services.GetRequiredService<IAccountLifecycleService>();
        var provisioning = await lifecycle.ProvisionVerifiedIdentityAsync(
            "https://identity.example.test/",
            "subject-1",
            AccountRole.Learner);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => lifecycle.DeactivateAsync(provisioning.Account.Id));

        await lifecycle.ActivateAsync(provisioning.Account.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => lifecycle.ActivateAsync(provisioning.Account.Id));
    }

    [Theory]
    [InlineData("", "subject")]
    [InlineData("issuer", " ")]
    public void Account_RequiredIdentityPartIsMissing_ThrowsArgumentException(string issuer, string subject)
    {
        Assert.Throws<ArgumentException>(() => new Account(issuer, subject, AccountRole.Learner));
    }

    public async ValueTask DisposeAsync()
    {
        await _services.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
