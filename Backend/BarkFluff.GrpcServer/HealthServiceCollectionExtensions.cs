using Microsoft.Extensions.DependencyInjection;

namespace BarkFluff.GrpcServer;

public static class HealthServiceCollectionExtensions
{
    /// <summary>
    /// Регистрирует фоновый ReadinessMonitorService (проверка EF/MassTransit/Redis/S3 раз в 15 c).
    /// Пара к app.MapHealthEndpoints().
    /// </summary>
    public static IServiceCollection AddBarkFluffHealth(this IServiceCollection services)
    {
        services.AddSingleton<ReadinessMonitorService>();
        services.AddHostedService(sp => sp.GetRequiredService<ReadinessMonitorService>());
        return services;
    }
}
