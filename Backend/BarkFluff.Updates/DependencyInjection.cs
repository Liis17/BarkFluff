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
        services.AddSingleton<Features.SubscribePrivateMessages.StreamSubscriptionsManager>();
        services.AddSingleton<Features.SubscribePrivateMessageEdits.StreamSubscriptionsManager>();
        services.AddSingleton<Features.SubscribePrivateMessageDeletes.StreamSubscriptionsManager>();
        services.AddSingleton<Features.SubscribePrivateChatInvites.StreamSubscriptionsManager>();
        services.AddSingleton<Features.SubscribePrivateChatInviteResolutions.StreamSubscriptionsManager>();
        services.AddSingleton<Features.SubscribeSecretChatInvites.StreamSubscriptionsManager>();
        services.AddSingleton<Features.SubscribeSecretChatResolutions.StreamSubscriptionsManager>();
        services.AddSingleton<Features.SubscribeSecretMessages.StreamSubscriptionsManager>();

        // Регистрируем трекер ожидающих push-уведомлений
        services.AddSingleton<Features.PushNotifications.PendingPushTracker>();

        // Добавляем MediatR с регистрацией обработчиков из сборки Updates
        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(DependencyInjection).Assembly));

        return services;
    }
}
