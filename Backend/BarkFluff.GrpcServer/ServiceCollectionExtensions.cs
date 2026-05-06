using BarkFluff.GrpcServer.Tracker;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BarkFluff.GrpcServer;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSettings<TSettings>(this IServiceCollection services, IConfiguration configuration, string sectionName)
        where TSettings : class
    {
        services.Configure<TSettings>(configuration.GetSection(sectionName));

        services.AddSingleton(provider => provider.GetRequiredService<IOptions<TSettings>>().Value);

        return services;
    }

    public static IServiceCollection AddBarkFluffGrpc(this IServiceCollection services)
    {
        services.AddScoped<IRequestContextAccessor, RequestContextAccessor>();
        services.AddScoped<RequestContext>(sp => sp.GetRequiredService<IRequestContextAccessor>().Current);

        services.AddGrpc(options =>
        {
            options.Interceptors.Add<ServerExceptionInterceptor>();
            options.Interceptors.Add<RequestContextInterceptor>();
        });

        return services;
    }
}