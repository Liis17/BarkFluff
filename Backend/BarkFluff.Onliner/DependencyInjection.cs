using BarkFluff.Onliner.BackgroundServices;
using BarkFluff.Onliner.Services;

namespace BarkFluff.Onliner;

public static class DependencyInjection
{
    public static IServiceCollection AddOnlinerServices(this IServiceCollection services)
    {
        // Singleton сервисы для управления состоянием
        services.AddSingleton<OnlineStatusStorage>();
        services.AddSingleton<OnlineStatusSubscriptionsManager>();
        services.AddSingleton<OnlineStatusNotifier>();

        // Background Services
        services.AddHostedService<OfflineDetectionService>();
        services.AddHostedService<DatabasePersistenceService>();

        // MediatR handlers
        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(DependencyInjection).Assembly));

        return services;
    }
}
