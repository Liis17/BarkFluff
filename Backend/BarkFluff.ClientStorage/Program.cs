
using BarkFluff.ClientStorage.Infrastructure;
using BarkFluff.ClientStorage.Middleware;
using BarkFluff.ClientStorage.Persistence;
using BarkFluff.ClientStorage.Services;
using BarkFluff.GrpcServer;

using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;

using Serilog;

namespace BarkFluff.ClientStorage;

public class Program
{
    private const string ServiceName = "BarkFluff.ClientStorage";
    private const long MaxUploadBytes = 512L * 1024 * 1024;

    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.AddBarkFluffSerilog(ServiceName);
        builder.Services.AddBarkFluffMetrics(ServiceName);

        builder.Services.AddControllers();

        builder.Services.AddSingleton<S3StorageService>();
        builder.Services.AddSingleton<LocalFileCache>();
        builder.Services.AddHostedService<CacheWarmupService>();
        builder.Services.AddHostedService<OldVersionsCleanupService>();

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
