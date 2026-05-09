namespace BarkFluff.Updates;

using Microsoft.Extensions.DependencyInjection;

public static class DependencyInjection
{
    public static IServiceCollection AddUpdatesServices(this IServiceCollection services)
    {
        // Регистрируем менеджер подписок как Singleton,
        // так как он должен сохранять состояние между запросами
        services.AddSingleton<Features.SubscribeNewMessages.StreamSubscriptionsManager>();
        services.AddSingleton<Features.SubscribeMessagesRead.StreamSubscriptionsManager>();
        services.AddSingleton<Features.SubscribeMessagesEdited.StreamSubscriptionsManager>();
        services.AddSingleton<Features.SubscribeMessagesDeleted.StreamSubscriptionsManager>();
        services.AddSingleton<Features.SubscribeMessagesPinned.StreamSubscriptionsManager>();
        services.AddSingleton<Features.SubscribeMessagesUnpinned.StreamSubscriptionsManager>();
        services.AddSingleton<Features.SubscribeAllMessagesUnpinned.StreamSubscriptionsManager>();

        // Регистрируем трекер ожидающих push-уведомлений
        services.AddSingleton<Features.PushNotifications.PendingPushTracker>();

        // Добавляем MediatR с регистрацией обработчиков из сборки Updates
        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(DependencyInjection).Assembly));

        return services;
    }
}
