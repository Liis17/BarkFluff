using BarkFluff.Files.Extensions;
using BarkFluff.Files.Host;
using BarkFluff.Files.Infrastructure;
using BarkFluff.Files.Persistence;
using BarkFluff.Files.Services;
using BarkFluff.GrpcServer;
using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Shared.Identity;

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
        builder.Services.AddScoped<ImageCompressor>();
        
        builder.Services.AddMinioS3(builder.Configuration);
        builder.Services.AddFileTypeDetection();

        builder.Services.AddDbContext<FilesContext>(options =>
            options.UseNpgsql(builder.Configuration["FilesDb"]));

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