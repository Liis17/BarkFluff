using BarkFluff.Files.Infrastructure;
using BarkFluff.Files.Services;

namespace BarkFluff.Files.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMinioS3(this IServiceCollection services, IConfiguration configuration)
    {
        // Регистрируем реестр бакетов — управляет конфигурацией и S3 клиентами для каждого бакета
        services.AddSingleton<S3BucketRegistry>();

        // Регистрируем uploader
        services.AddTransient<S3Uploader>();

        // Регистрируем инициализатор бакетов
        services.AddSingleton<S3BucketInitializer>();

        return services;
    }

    public static IServiceCollection AddFileTypeDetection(this IServiceCollection services)
    {
        services.AddSingleton<FileTypeDetector>();
        return services;
    }
}
