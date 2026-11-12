using Microsoft.Extensions.DependencyInjection;
using Kokishin.Application.Accounts;

namespace Kokishin.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton<IAccountLifecycleService, AccountLifecycleService>();
        return services;
    }
}
