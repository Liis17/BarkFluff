
using BarkFluff.ClientStorage.Infrastructure;
using BarkFluff.ClientStorage.Middleware;
using BarkFluff.ClientStorage.Persistence;
using BarkFluff.ClientStorage.Services;

using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;

using Serilog;
using Serilog.Events;

namespace BarkFluff.ClientStorage;

public class Program
{
    private const string ServiceName = "BarkFluff.ClientStorage";
    private const long MaxUploadBytes = 512L * 1024 * 1024;

    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Host.UseSerilog((context, loggerConfig) =>
        {
            loggerConfig
                .MinimumLevel.Information()
                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
                .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
                .MinimumLevel.Override("System.Net.Http.HttpClient", LogEventLevel.Warning)
                .Enrich.FromLogContext()
                .Enrich.WithMachineName()
                .Enrich.WithEnvironmentName()
                .Enrich.WithThreadId()
                .Enrich.WithProperty("Application", ServiceName)
                .WriteTo.Console(
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}");
        });

        builder.Services.AddControllers();

        builder.Services.AddSingleton<S3StorageService>();
        builder.Services.AddSingleton<LocalFileCache>();
        builder.Services.AddHostedService<CacheWarmupService>();

        builder.Services.AddDbContext<ClientStorageContext>(options =>
            options.UseSqlite("Data Source=/app/data/clientstorage.db"));

        builder.Services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
            options.KnownNetworks.Clear();
            options.KnownProxies.Clear();
        });

        builder.Services.Configure<FormOptions>(options =>
        {
            options.MultipartBodyLengthLimit = MaxUploadBytes;
            options.ValueLengthLimit         = int.MaxValue;
            options.MultipartHeadersLengthLimit = int.MaxValue;
        });

        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Limits.MaxRequestBodySize = MaxUploadBytes;
            options.Limits.KeepAliveTimeout   = TimeSpan.FromMinutes(30);
            options.Limits.RequestHeadersTimeout = TimeSpan.FromMinutes(2);
            // Не выставляем MinRequestBodyDataRate — медленные клиенты не должны получать 408.
            options.Limits.MinRequestBodyDataRate = null;
            options.Limits.MinResponseDataRate    = null;
        });

        var app = builder.Build();

        Directory.CreateDirectory("/app/data");
        Directory.CreateDirectory(app.Configuration["CACHE_DIR"] ?? "/app/cache");

        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ClientStorageContext>();
            db.Database.Migrate();

            var s3 = scope.ServiceProvider.GetRequiredService<S3StorageService>();
            s3.InitializeBucketAsync().GetAwaiter().GetResult();
        }

        app.UseSerilogRequestLogging();
        app.UseForwardedHeaders();
        app.UseMiddleware<TokenAuthMiddleware>();

        app.MapControllers();

        app.Lifetime.ApplicationStopped.Register(Log.CloseAndFlush);
        app.Run();
    }
}
