using Kokishin.Infrastructure.Persistence;
using Kokishin.Infrastructure.Persistence.Sessions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kokishin.Tests;

public sealed class SqliteSessionTicketStoreTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly ServiceProvider _services;

    public SqliteSessionTicketStoreTests()
    {
        _connection.Open();
        var services = new ServiceCollection();
        services.AddDbContextFactory<KokishinDbContext>(options => options.UseSqlite(_connection));
        services.AddSingleton<IDurableSessionTicketStore, SqliteSessionTicketStore>();
        _services = services.BuildServiceProvider();

        using var scope = _services.CreateScope();
        scope.ServiceProvider.GetRequiredService<KokishinDbContext>().Database.EnsureCreated();
    }

    [Fact]
    public async Task StoreAsync_RetrieveAsyncAndRemoveAsync_PersistTicketData()
    {
        var store = _services.GetRequiredService<IDurableSessionTicketStore>();
        byte[] protectedTicket = [1, 2, 3, 4];

        await store.StoreAsync("session-key", protectedTicket, DateTimeOffset.UtcNow.AddMinutes(10));
        var retrieved = await store.RetrieveAsync("session-key");
        await store.RemoveAsync("session-key");
        var removed = await store.RetrieveAsync("session-key");

        Assert.Equal(protectedTicket, retrieved);
        Assert.Null(removed);
    }

    [Fact]
    public async Task RetrieveAsync_ExpiredTicket_ReturnsNullAndDeletesTicket()
    {
        var store = _services.GetRequiredService<IDurableSessionTicketStore>();
        await store.StoreAsync("expired-key", [1], DateTimeOffset.UtcNow.AddMinutes(-1));

        var retrieved = await store.RetrieveAsync("expired-key");

        Assert.Null(retrieved);
        Assert.Null(await store.RetrieveAsync("expired-key"));
    }

    [Fact]
    public async Task StoreAsync_ExistingKey_ReplacesTicketAndExpiration()
    {
        var store = _services.GetRequiredService<IDurableSessionTicketStore>();
        await store.StoreAsync("session-key", [1], DateTimeOffset.UtcNow.AddMinutes(10));
        await store.StoreAsync("session-key", [2, 3], DateTimeOffset.UtcNow.AddMinutes(20));

        var retrieved = await store.RetrieveAsync("session-key");

        Assert.Equal(new byte[] { 2, 3 }, retrieved);
    }

    public async ValueTask DisposeAsync()
    {
        await _services.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
