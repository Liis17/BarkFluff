using BarkFluff.Onliner.BackgroundServices;
using BarkFluff.Onliner.Services;

namespace BarkFluff.Onliner;

public static class DependencyInjection
{
    public static IServiceCollection AddOnlinerServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Presence — общий стор в Redis (разделяется всеми инстансами) + распределённый single-runner.
        services.AddSingleton<IPresenceStore, RedisPresenceStore>();
        services.AddSingleton<RedisSingleRunner>();
        services.AddSingleton<OnlineStatusSubscriptionsManager>();
        services.AddSingleton<OnlineStatusNotifier>();

        // Статусы remote-пользователей (этап 4.2) — отдельные TTL-ключи, не sorted set:
        // их не должен трогать OfflineDetectionService и не должен персистить DatabasePersistenceService.
        services.AddSingleton<IRemotePresenceStore, RedisRemotePresenceStore>();

        // Индикаторы набора текста — чистый ретранслятор поверх in-memory подписок
        services.AddSingleton<TypingSubscriptionsManager>();
        services.AddSingleton<TypingNotifier>();

        // Фильтр видимости онлайн-статуса (использует gRPC-клиент UsersServerApi)
        services.AddScoped<OnlineVisibilityFilter>();

        // Фильтр членства в чате для typing (использует gRPC-клиент MessagesServerApi)
        services.AddScoped<ChatMembershipFilter>();

        // Исходящий federated typing (этап 4.4). Клиент Federation опционален: на ноде без
        // федерации его в контейнере нет, и вся ветка просто не активируется.
        services.AddScoped<FederatedTypingSender>();

        // Background Services
        services.AddHostedService<OfflineDetectionService>();
        services.AddHostedService<DatabasePersistenceService>();
        services.AddHostedService<MetricsSnapshotService>();

        // Интерес к remote-presence (этап 4.2). Гейт: нода без федерации не должна лить ошибки,
        // поэтому сервис стартует только при заданном FederationService:Host.
        if (!string.IsNullOrWhiteSpace(configuration["FederationService:Host"]))
        {
            services.AddHostedService<PresenceInterestReporter>();
        }

        // MediatR handlers
        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(DependencyInjection).Assembly));

        return services;
    }
}
