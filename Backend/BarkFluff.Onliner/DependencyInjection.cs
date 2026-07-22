using BarkFluff.Onliner.BackgroundServices;
using BarkFluff.Onliner.Services;

namespace BarkFluff.Onliner;

public static class DependencyInjection
{
    public static IServiceCollection AddOnlinerServices(this IServiceCollection services)
    {
        // Presence — общий стор в Redis (разделяется всеми инстансами) + распределённый single-runner.
        services.AddSingleton<IPresenceStore, RedisPresenceStore>();
        services.AddSingleton<RedisSingleRunner>();
        services.AddSingleton<OnlineStatusSubscriptionsManager>();
        services.AddSingleton<OnlineStatusNotifier>();

        // Индикаторы набора текста — чистый ретранслятор поверх in-memory подписок
        services.AddSingleton<TypingSubscriptionsManager>();
        services.AddSingleton<TypingNotifier>();

        // Фильтр видимости онлайн-статуса (использует gRPC-клиент UsersServerApi)
        services.AddScoped<OnlineVisibilityFilter>();

        // Фильтр членства в чате для typing (использует gRPC-клиент MessagesServerApi)
        services.AddScoped<ChatMembershipFilter>();

        // Background Services
        services.AddHostedService<OfflineDetectionService>();
        services.AddHostedService<DatabasePersistenceService>();
        services.AddHostedService<MetricsSnapshotService>();

        // MediatR handlers
        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(DependencyInjection).Assembly));

        return services;
    }
}
