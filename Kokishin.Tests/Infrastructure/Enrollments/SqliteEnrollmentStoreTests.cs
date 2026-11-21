using Kokishin.Application.Enrollments;
using Kokishin.Domain.Accounts;
using Kokishin.Domain.Courses;
using Kokishin.Domain.Enrollments;
using Kokishin.Infrastructure.Persistence;
using Kokishin.Infrastructure.Persistence.Enrollments;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kokishin.Tests;

public sealed class SqliteEnrollmentStoreTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly ServiceProvider _services;

    public SqliteEnrollmentStoreTests()
    {
        _connection.Open();
        var services = new ServiceCollection();
        services.AddDbContextFactory<KokishinDbContext>(options => options.UseSqlite(_connection));
        services.AddSingleton<IEnrollmentStore, SqliteEnrollmentStore>();
        _services = services.BuildServiceProvider();

        using var scope = _services.CreateScope();
        scope.ServiceProvider.GetRequiredService<KokishinDbContext>().Database.Migrate();
    }

    [Fact]
    public async Task AddIfAbsentAsync_DuplicateCourseAndLearner_ReturnsExistingEnrollment()
    {
        var instructor = CreateActiveAccount(AccountRole.Instructor);
        var learner = CreateActiveAccount(AccountRole.Learner);
        var course = new Course(instructor.Id, "Biology");
        await using (var dbContext = await CreateDbContextAsync())
        {
            dbContext.Accounts.AddRange(instructor, learner);
            dbContext.Courses.Add(course);
            await dbContext.SaveChangesAsync();
        }

        var store = _services.GetRequiredService<IEnrollmentStore>();
        var enrollment = new Enrollment(course.Id, learner.Id);
        var firstInsert = await store.AddIfAbsentAsync(enrollment);
        var duplicateInsert = await store.AddIfAbsentAsync(new Enrollment(course.Id, learner.Id));

        await using var verifyContext = await CreateDbContextAsync();
        Assert.Null(firstInsert);
        Assert.NotNull(duplicateInsert);
        Assert.Equal(EnrollmentStatus.Active, duplicateInsert.Status);
        Assert.Single(await verifyContext.Enrollments.ToListAsync());
    }

    [Fact]
    public async Task AddIfAbsentAsync_UnknownCourseOrLearner_RejectsEnrollment()
    {
        var store = _services.GetRequiredService<IEnrollmentStore>();

        var exception = await Assert.ThrowsAsync<DbUpdateException>(
            () => store.AddIfAbsentAsync(new Enrollment(Guid.NewGuid(), Guid.NewGuid())));

        Assert.IsType<SqliteException>(exception.InnerException);
    }

    private Task<KokishinDbContext> CreateDbContextAsync() =>
        _services.GetRequiredService<IDbContextFactory<KokishinDbContext>>().CreateDbContextAsync();

    private static Account CreateActiveAccount(AccountRole role)
    {
        var account = new Account(
            "https://identity.example.test/",
            Guid.NewGuid().ToString("N"),
            role);
        account.Activate();
        return account;
    }

    public async ValueTask DisposeAsync()
    {
        await _services.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
