using BarkFluff.Files.Consumers;
using BarkFluff.Files.Extensions;
using BarkFluff.Files.Host;
using BarkFluff.Files.Infrastructure;
using BarkFluff.Files.Persistence;
using BarkFluff.Files.Services;
using BarkFluff.GrpcServer;
using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Shared.Identity;

using MassTransit;

using Microsoft.EntityFrameworkCore;

using Serilog;

namespace BarkFluff.Files;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.LoadConfiguration(ServiceId.Files);
        builder.AddBarkFluffSerilog("BarkFluff.Files");
        builder.SetRunningAddress(builder.Configuration);

        // Регистрируем gRPC сервисы с интерцепторами
        builder.Services.AddGrpc(options =>
        {
            options.Interceptors.Add<ServerExceptionInterceptor>();
            // Оригинальные файлы изображений от админ-панели могут быть больше дефолтных 4 МБ
            options.MaxReceiveMessageSize = 20 * 1024 * 1024; // 20 МБ
            options.MaxSendMessageSize = 20 * 1024 * 1024;    // 20 МБ
        });
        builder.Services.AddBarkFluffMetrics("BarkFluff.Files");

        builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<Program>());

        builder.Services.AddGrpcReflection();

        builder.Services.AddXAuth(builder.Configuration);

        // Регистрируем gRPC клиент для UsersServerApi
        builder.Services.AddGrpcClient<BarkFluff.Proto.Users.UsersServerApi.UsersServerApiClient>(o =>
            {
                o.Address = new Uri(builder.Configuration["UsersService:Host"]);
            }).AddInterceptor(() => new BarkFluff.Shared.Auth.JwtClientInterceptor(builder.Configuration["UsersService:Token"]))
            .AddInterceptor(() => new BarkFluff.Shared.Exceptions.Interceptors.ExceptionClientInterceptor());

        builder.Services.AddControllers();

        builder.Services.AddScoped<UploadedFilesStorage>();
        builder.Services.AddScoped<TempFilesStorage>();
        builder.Services.AddScoped<FileHashesStorage>();
        builder.Services.AddScoped<BadgeImagesStorage>();
        builder.Services.AddScoped<StickerPacksStorage>();
        builder.Services.AddScoped<StickersStorage>();

        // Стриминг файла ноде-партнёру (этап 3.2) — не через MediatR: результат поток, не сообщение.
        builder.Services.AddScoped<Features.FetchFileStream.FetchFileStreamQueryHandler>();

        // Скачивание federated-вложения через свою ноду (этап 3.3).
        builder.Services.AddScoped<Features.DownloadFile.FederatedDownloadService>();

        // gRPC-клиент к Federation: FetchRemoteFile — проксирование байтов с origin (этап 3.3).
        builder.Services.AddGrpcClient<BarkFluff.Proto.FederationInternal.FederationInternalApi.FederationInternalApiClient>(o =>
            {
                o.Address = new Uri(builder.Configuration["FederationService:Host"]!);
            }).AddInterceptor(() => new BarkFluff.Shared.Auth.JwtClientInterceptor(builder.Configuration["FederationService:Token"] ?? string.Empty))
            .AddInterceptor(() => new BarkFluff.Shared.Exceptions.Interceptors.ExceptionClientInterceptor());

        // gRPC-клиент к Messages: CheckFedFileUserAccess — проверка права пользователя на
        // federated-вложение при выдаче capability-ссылки (этап 3.3).
        builder.Services.AddGrpcClient<BarkFluff.Proto.Messages.MessagesServerApi.MessagesServerApiClient>(o =>
            {
                o.Address = new Uri(builder.Configuration["MessagesService:Host"]!);
            }).AddInterceptor(() => new BarkFluff.Shared.Auth.JwtClientInterceptor(builder.Configuration["MessagesService:Token"] ?? string.Empty))
            .AddInterceptor(() => new BarkFluff.Shared.Exceptions.Interceptors.ExceptionClientInterceptor());
        builder.Services.AddSingleton<ImageCompressor>();
        builder.Services.AddSingleton<VideoThumbnailExtractor>();
        builder.Services.AddHostedService<TempFileCleanupService>();

        // Путь к бинарям ffmpeg/ffprobe в образе (см. Dockerfile.slim). По умолчанию — /usr/local/bin.
        FFMpegCore.GlobalFFOptions.Configure(o =>
            o.BinaryFolder = builder.Configuration["Ffmpeg:BinaryFolder"] ?? "/usr/local/bin");

        builder.Services.AddMinioS3(builder.Configuration);
        builder.Services.AddFileTypeDetection();

        builder.Services.AddDbContext<FilesContext>(options =>
            options.UseNpgsql(builder.Configuration["FilesDb"], npgsql =>
            {
                npgsql.EnableRetryOnFailure(3);
                npgsql.CommandTimeout(30);
            }));

        builder.Services.AddMassTransit(x =>
        {
            x.AddConsumer<SessionRevokedConsumer>();

            x.UsingRabbitMq((context, cfg) =>
            {
                cfg.Host(builder.Configuration["RabbitMQ:Host"], "/", h =>
                {
                    h.Username(builder.Configuration["RabbitMQ:Username"]);
                    h.Password(builder.Configuration["RabbitMQ:Password"]);
                });

                cfg.ReceiveEndpoint($"session-revoked-files-{InstanceId.Current}", e =>
                {
                    e.AutoDelete = true;
                    e.Durable = false;
                    e.ConfigureConsumer<SessionRevokedConsumer>(context);
                });
            });
        });

        var app = builder.Build();

        using (var scope = app.Services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<FilesContext>();
            ctx.Database.Migrate();

            // Инициализируем S3 бакеты
            var bucketInitializer = scope.ServiceProvider.GetRequiredService<S3BucketInitializer>();
            bucketInitializer.InitializeBucketsAsync().GetAwaiter().GetResult();
        }

        app.MapGrpcReflectionService();

        app.UseXAuth();

        app.MapControllers();

        app.MapGrpcService<FilesApiService>();
        app.MapGrpcService<FilesServerApiService>();

        app.Lifetime.ApplicationStopped.Register(Log.CloseAndFlush);
        app.Run();
    }
}
