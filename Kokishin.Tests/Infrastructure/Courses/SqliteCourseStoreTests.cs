using Kokishin.Application;
using Kokishin.Application.Courses;
using Kokishin.Domain.Accounts;
using Kokishin.Domain.Courses;
using Kokishin.Infrastructure.Persistence;
using Kokishin.Infrastructure.Persistence.Courses;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kokishin.Tests;

public sealed class SqliteCourseStoreTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly ServiceProvider _services;

    public SqliteCourseStoreTests()
    {
        _connection.Open();
        var services = new ServiceCollection();
        services.AddApplication();
        services.AddDbContextFactory<KokishinDbContext>(options => options.UseSqlite(_connection));
        services.AddSingleton<ICourseStore, SqliteCourseStore>();
        _services = services.BuildServiceProvider();

        using var scope = _services.CreateScope();
        scope.ServiceProvider.GetRequiredService<KokishinDbContext>().Database.Migrate();
    }

    [Fact]
    public async Task CreateCourseAsync_ValidCourse_PersistsCourseAndOwner()
    {
        var account = new Account("https://identity.example.test/", "course-owner", AccountRole.Instructor);
        account.Activate();
        await using (var dbContext = await CreateDbContextAsync())
        {
            dbContext.Accounts.Add(account);
            await dbContext.SaveChangesAsync();
        }

        var service = _services.GetRequiredService<ICourseService>();
        var created = await service.CreateCourseAsync(account.Id, "Biology", "An introduction.");
        await using var verifyContext = await CreateDbContextAsync();
        var persisted = await verifyContext.Courses.SingleAsync(course => course.Id == created.Id);

        Assert.Equal(created.Id, persisted.Id);
        Assert.Equal(account.Id, persisted.OwnerId);
        Assert.Equal("Biology", persisted.Title);
        Assert.Equal("An introduction.", persisted.Description);
        Assert.Equal(CourseStatus.Draft, persisted.Status);
    }

    [Fact]
    public async Task AddAsync_OwnerDoesNotExist_RejectsCourse()
    {
        var store = _services.GetRequiredService<ICourseStore>();
        var course = new Course(Guid.NewGuid(), "Biology");

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => store.AddAsync(course));

        Assert.IsType<SqliteException>(exception.InnerException);
    }

    private Task<KokishinDbContext> CreateDbContextAsync() =>
        _services.GetRequiredService<IDbContextFactory<KokishinDbContext>>().CreateDbContextAsync();

    public async ValueTask DisposeAsync()
    {
        await _services.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
