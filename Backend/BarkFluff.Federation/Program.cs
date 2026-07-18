using BarkFluff.Federation.Host;
using BarkFluff.Federation.Persistence.Contexts;
using BarkFluff.GrpcServer;
using BarkFluff.GrpcServer.Metrics;
using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Shared.Auth;
using BarkFluff.Shared.Identity;

using Microsoft.EntityFrameworkCore;

using Serilog;

namespace BarkFluff.Federation;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.LoadConfiguration(ServiceId.Federation);
        builder.AddBarkFluffSerilog("BarkFluff.Federation");
        builder.SetRunningAddress(builder.Configuration);

        builder.Services.AddGrpc(options =>
        {
            options.Interceptors.Add<ServerExceptionInterceptor>();
        });
        builder.Services.AddBarkFluffMetrics("BarkFluff.Federation");

        builder.Services.AddGrpcReflection();

        builder.Services.AddDbContext<FederationContext>(c
            => c.UseNpgsql(builder.Configuration["FederationDb"], npgsql =>
            {
                npgsql.EnableRetryOnFailure(3);
                npgsql.CommandTimeout(30);
            }));

        builder.Services.AddXAuth(builder.Configuration);

        var app = builder.Build();

        using (var scope = app.Services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<FederationContext>();
            ctx.Database.Migrate();
        }

        if (app.Environment.IsDevelopment())
            app.MapGrpcReflectionService();

        app.UseRouting();

        app.UseXAuth();

        app.MapGrpcService<FederationS2SApiService>();
        app.MapGrpcService<FederationInternalApiService>();

        var startupMetrics = app.Services.GetRequiredService<MetricsCollector>();
        startupMetrics.Set("service_started_unix", DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        app.Lifetime.ApplicationStopped.Register(Log.CloseAndFlush);
        app.Run();
    }
}
