namespace BarkFluff.Updates;

using Features.SubscribeNewMessages;
using Features.SubscribeReadReceipts;
using Microsoft.Extensions.DependencyInjection;

public static class DependencyInjection
{
    public static IServiceCollection AddUpdatesServices(this IServiceCollection services)
    {
        // Регистрируем менеджер подписок как Singleton,
        // так как он должен сохранять состояние между запросами
        services.AddSingleton<StreamSubscriptionsManager>();
        services.AddSingleton<ReadReceiptSubscriptionsManager>();
        
        // Добавляем MediatR с регистрацией обработчиков из сборки Updates
        services.AddMediatR(cfg => 
            cfg.RegisterServicesFromAssembly(typeof(DependencyInjection).Assembly));
        
        return services;
    }
}
