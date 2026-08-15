using BarkFluff.FastAuth.Domain;
using BarkFluff.FastAuth.Features.SubscribeFastAuthResult;
using BarkFluff.FastAuth.Infrastructure;

namespace BarkFluff.FastAuth;

public static class DependencyInjection
{
    public static IServiceCollection AddFastAuthServices(this IServiceCollection services)
    {
        services.AddSingleton<IFastAuthSessionStore, RedisFastAuthSessionStore>();
        services.AddSingleton<FastAuthEventBus>();
        services.AddSingleton<IFastAuthEventBus>(sp => sp.GetRequiredService<FastAuthEventBus>());
        services.AddHostedService(sp => sp.GetRequiredService<FastAuthEventBus>());

        services.AddSingleton<QrCodeGenerator>();

        services.AddScoped<SubscribeFastAuthResultQueryHandler>();

        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(DependencyInjection).Assembly));

        return services;
    }
}
