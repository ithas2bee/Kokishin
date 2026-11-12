using Kokishin.Application;
using Kokishin.Application.Profiles;
using Kokishin.Domain.Accounts;
using Kokishin.Infrastructure.Persistence;
using Kokishin.Infrastructure.Persistence.Profiles;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kokishin.Tests;

public sealed class SqliteProfileStoreTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly ServiceProvider _services;

    public SqliteProfileStoreTests()
    {
        _connection.Open();
        var services = new ServiceCollection();
        services.AddApplication();
        services.AddDbContextFactory<KokishinDbContext>(options => options.UseSqlite(_connection));
        services.AddSingleton<IProfileStore, SqliteProfileStore>();
        _services = services.BuildServiceProvider();

        using var scope = _services.CreateScope();
        scope.ServiceProvider.GetRequiredService<KokishinDbContext>().Database.Migrate();
    }

    [Fact]
    public async Task UpdateOwnProfileAsync_ProfileExists_PersistsAndRetrievesProfile()
    {
        var account = new Account("https://identity.example.test/", "profile-owner");
        await using (var dbContext = await CreateDbContextAsync())
        {
            dbContext.Accounts.Add(account);
            await dbContext.SaveChangesAsync();
        }

        var profileService = _services.GetRequiredService<IProfileService>();
        var saved = await profileService.UpdateOwnProfileAsync(account.Id, "Ada Lovelace", "Instructor");
        var retrieved = await profileService.GetOwnProfileAsync(account.Id);

        Assert.Equal(account.Id, saved.AccountId);
        Assert.Equal(account.Id, retrieved?.AccountId);
        Assert.Equal("Ada Lovelace", retrieved?.DisplayName);
        Assert.Equal("Instructor", retrieved?.Description);
    }

    [Fact]
    public async Task UpdateOwnProfileAsync_RepeatedUpdates_StoresOneProfilePerAccount()
    {
        var account = new Account("https://identity.example.test/", "profile-owner");
        await using (var dbContext = await CreateDbContextAsync())
        {
            dbContext.Accounts.Add(account);
            await dbContext.SaveChangesAsync();
        }

        var profileService = _services.GetRequiredService<IProfileService>();
        await profileService.UpdateOwnProfileAsync(account.Id, "Ada");
        await profileService.UpdateOwnProfileAsync(account.Id, "Grace", "Learner");

        await using var verifyContext = await CreateDbContextAsync();
        Assert.Equal(1, await verifyContext.Profiles.CountAsync(profile => profile.AccountId == account.Id));
        var profile = await verifyContext.Profiles.SingleAsync();
        Assert.Equal("Grace", profile.DisplayName);
        Assert.Equal("Learner", profile.Description);
    }

    [Fact]
    public async Task SaveAsync_AccountDoesNotExist_RejectsProfile()
    {
        var store = _services.GetRequiredService<IProfileStore>();
        var profile = new Kokishin.Domain.Profiles.Profile(Guid.NewGuid(), "Ada");

        await Assert.ThrowsAsync<SqliteException>(() => store.SaveAsync(profile));
    }

    private Task<KokishinDbContext> CreateDbContextAsync() =>
        _services.GetRequiredService<IDbContextFactory<KokishinDbContext>>().CreateDbContextAsync();

    public async ValueTask DisposeAsync()
    {
        await _services.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
