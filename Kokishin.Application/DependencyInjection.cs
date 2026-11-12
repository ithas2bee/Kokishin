using Microsoft.Extensions.DependencyInjection;
using Kokishin.Application.Accounts;
using Kokishin.Application.Profiles;

namespace Kokishin.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton<IAccountLifecycleService, AccountLifecycleService>();
        services.AddSingleton<IProfileService, ProfileService>();
        return services;
    }
}
