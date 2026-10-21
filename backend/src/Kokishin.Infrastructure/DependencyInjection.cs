using Kokishin.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Kokishin.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Kokishin")
            ?? throw new InvalidOperationException("Connection string 'Kokishin' is not configured.");

        services.AddDbContext<KokishinDbContext>(options => options.UseSqlite(connectionString));
        services.AddHealthChecks().AddDbContextCheck<KokishinDbContext>("database");

        return services;
    }
}
