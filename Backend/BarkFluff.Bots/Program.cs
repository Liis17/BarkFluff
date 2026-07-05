using BarkFluff.Bots.Host;
using BarkFluff.Bots.Infrastructure;
using BarkFluff.Bots.Persistence;
using BarkFluff.Bots.Persistence.Services;
using BarkFluff.Bots.Services;
using BarkFluff.GrpcServer;
using BarkFluff.GrpcServer.Metrics;
using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Proto.Users;
using BarkFluff.Shared.Auth;
using BarkFluff.Shared.Exceptions.Interceptors;
using BarkFluff.Shared.Identity;

using Microsoft.EntityFrameworkCore;

using Serilog;

namespace BarkFluff.Bots;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.LoadConfiguration(ServiceId.Bots);
        builder.AddBarkFluffSerilog("BarkFluff.Bots");
        builder.SetRunningAddress(builder.Configuration);

        builder.Services.AddGrpc(options =>
        {
            options.Interceptors.Add<ServerExceptionInterceptor>();
        });
        builder.Services.AddBarkFluffMetrics("BarkFluff.Bots");
        builder.Services.AddGrpcReflection();

        builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<Program>());

        builder.Services.AddDbContext<BotsContext>(c
            => c.UseNpgsql(builder.Configuration["BotsDb"], npgsql =>
            {
                npgsql.EnableRetryOnFailure(3);
                npgsql.CommandTimeout(30);
            }));

        builder.Services.AddScoped<BotsStorage>();
        builder.Services.AddScoped<SystemBotsSeeder>();
        builder.Services.AddSingleton<BotRegistryCache>();
        builder.Services.AddSingleton<BotTokenService>();

        builder.Services.AddXAuth(builder.Configuration);

        builder.Services.AddGrpcClient<UsersServerApi.UsersServerApiClient>(o =>
            {
                o.Address = new Uri(builder.Configuration["UsersService:Host"]);
            }).AddInterceptor(() => new JwtClientInterceptor(builder.Configuration["UsersService:Token"]))
            .AddInterceptor(() => new ExceptionClientInterceptor());

        var app = builder.Build();

        using (var scope = app.Services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<BotsContext>();
            ctx.Database.Migrate();

            var seeder = scope.ServiceProvider.GetRequiredService<SystemBotsSeeder>();
            seeder.SeedAsync().GetAwaiter().GetResult();
        }

        app.MapGrpcReflectionService();
        app.UseRouting();

        app.UseXAuth();

        app.MapGrpcService<BotsServerApiService>();

        var startupMetrics = app.Services.GetRequiredService<MetricsCollector>();
        startupMetrics.Set("service_started_unix", DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        app.Lifetime.ApplicationStopped.Register(Log.CloseAndFlush);
        app.Run();
    }
}
