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
        var account = new Account("https://identity.example.test/", "subject-1");

        await store.AddAsync(account);
        var retrieved = await store.FindByIdentityAsync(account.Issuer, account.Subject);

        Assert.NotNull(retrieved);
        Assert.Equal(account.Id, retrieved.Id);
    }

    [Fact]
    public async Task AddAsync_DuplicateIdentity_RejectsSecondAccount()
    {
        var store = _services.GetRequiredService<IAccountStore>();
        await store.AddAsync(new Account("https://identity.example.test/", "subject-1"));

        var duplicate = new Account("https://identity.example.test/", "subject-1");
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
        var firstIssuer = new Account("https://identity-one.example.test/", "subject");
        var secondIssuer = new Account("https://identity-two.example.test/", "subject");
        var secondSubject = new Account("https://identity-one.example.test/", "other-subject");
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

    [Theory]
    [InlineData("", "subject")]
    [InlineData("issuer", " ")]
    public void Account_RequiredIdentityPartIsMissing_ThrowsArgumentException(string issuer, string subject)
    {
        Assert.Throws<ArgumentException>(() => new Account(issuer, subject));
    }

    public async ValueTask DisposeAsync()
    {
        await _services.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
