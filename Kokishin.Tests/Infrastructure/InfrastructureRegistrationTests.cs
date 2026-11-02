using Kokishin.Infrastructure;
using Kokishin.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Kokishin.Tests;

public class InfrastructureRegistrationTests
{
    [Fact]
    public void AddInfrastructure_RegistersSqliteDbContext()
    {
        var configuration = new ConfigurationManager
        {
            ["ConnectionStrings:Kokishin"] = "Data Source=:memory:"
        };
        var services = new ServiceCollection();

        services.AddInfrastructure(configuration);

        using var provider = services.BuildServiceProvider();
        using var dbContext = provider.GetRequiredService<KokishinDbContext>();

        Assert.Equal("Data Source=:memory:", dbContext.Database.GetConnectionString());
    }

    [Fact]
    public void AddInfrastructure_WithoutConnectionString_ThrowsInvalidOperationException()
    {
        var configuration = new ConfigurationManager();
        var services = new ServiceCollection();

        var exception = Assert.Throws<InvalidOperationException>(
            () =>
            {
                services.AddInfrastructure(configuration);
            });

        Assert.Contains("Connection string 'Kokishin' is required", exception.Message);
    }
}
