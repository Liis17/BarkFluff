using BarkFluff.FastAuth.Features.SubscribeFastAuthResult;
using BarkFluff.FastAuth.Infrastructure;

namespace BarkFluff.FastAuth;

public static class DependencyInjection
{
    public static IServiceCollection AddFastAuthServices(this IServiceCollection services)
    {
        services.AddSingleton<FastAuthSessionsManager>();
        services.AddSingleton<QrCodeGenerator>();
        services.AddHostedService<FastAuthExpirationService>();

        services.AddScoped<SubscribeFastAuthResultQueryHandler>();

        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(DependencyInjection).Assembly));

        return services;
    }
}
