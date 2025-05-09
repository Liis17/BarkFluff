using Amazon.Runtime;
using Amazon.S3;
using BarkFluff.Files.Configurations;
using BarkFluff.Files.Infrastructure;
using Microsoft.Extensions.Options;

namespace BarkFluff.Files.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMinioS3(this IServiceCollection services, IConfiguration configuration)
    {
        // Привязываем секцию Minio из appsettings.json
        services.Configure<MinioOptions>(configuration.GetSection("Minio"));

        // Регистрируем IAmazonS3 с настройками MinIO
        services.AddSingleton<IAmazonS3>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<MinioOptions>>().Value;
            var config = new AmazonS3Config
            {
                ServiceURL = opts.ServiceUrl,
                ForcePathStyle = opts.ForcePathStyle,
            };
            
            var creds = new BasicAWSCredentials(opts.AccessKey, opts.SecretKey);
            
            return new AmazonS3Client(creds, config);
        });

        // Регистрируем uploader
        services.AddTransient<S3Uploader>();

        return services;
    }
}