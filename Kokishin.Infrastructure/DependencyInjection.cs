using Kokishin.Infrastructure.Persistence;
using Kokishin.Infrastructure.Persistence.Sessions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Kokishin.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Kokishin")
            ?? throw new InvalidOperationException(
                "Connection string 'Kokishin' is required. Set ConnectionStrings:Kokishin in configuration or the ConnectionStrings__Kokishin environment variable.");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Connection string 'Kokishin' cannot be empty. Set ConnectionStrings:Kokishin in configuration or the ConnectionStrings__Kokishin environment variable.");
        }

        SqliteConnectionStringBuilder sqliteConnectionString;
        try
        {
            sqliteConnectionString = new SqliteConnectionStringBuilder(connectionString);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidOperationException(
                "Connection string 'Kokishin' must be a valid SQLite connection string.",
                exception);
        }

        if (string.IsNullOrWhiteSpace(sqliteConnectionString.DataSource))
        {
            throw new InvalidOperationException(
                "Connection string 'Kokishin' must specify a SQLite data source.");
        }

        services.AddDbContextFactory<KokishinDbContext>(options => options.UseSqlite(connectionString));
        services.AddSingleton<IDurableSessionTicketStore, SqliteSessionTicketStore>();
        services.AddHealthChecks().AddDbContextCheck<KokishinDbContext>("database");

        return services;
    }
}
