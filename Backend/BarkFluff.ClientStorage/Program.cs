
using BarkFluff.ClientStorage.Infrastructure;
using BarkFluff.ClientStorage.Middleware;
using BarkFluff.ClientStorage.Persistence;
using BarkFluff.ClientStorage.Services;

using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;

namespace BarkFluff.ClientStorage;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

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

        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Limits.MaxRequestBodySize = 512 * 1024 * 1024; // 512 MB
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

        app.UseForwardedHeaders();
        app.UseMiddleware<TokenAuthMiddleware>();

        app.MapControllers();

        app.Run();
    }
}
