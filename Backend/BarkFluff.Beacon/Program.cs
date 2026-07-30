using BarkFluff.Beacon.Configurations;
using BarkFluff.Beacon.BackgroundServices;
using BarkFluff.Beacon.Host;
using BarkFluff.GrpcServer;
using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Proto.Configuration;
using BarkFluff.Shared.Identity;

using Microsoft.AspNetCore.Server.Kestrel.Core;

using Serilog;

namespace BarkFluff.Beacon;
/// <summary>
/// ����� ����� � ����������
/// </summary>
public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.LoadConfiguration(ServiceId.Beacon);
        builder.AddBarkFluffSerilog("BarkFluff.Beacon");

        var envPort = Environment.GetEnvironmentVariable("BEACON_PORT")
                      ?? Environment.GetEnvironmentVariable("RunSettings__Port");

        if (int.TryParse(envPort, out var dynamicPort))
        {
            builder.WebHost.ConfigureKestrel(options =>
            {
                options.ListenAnyIP(dynamicPort, o =>
                {
                    o.Protocols = HttpProtocols.Http2;
                });
            });
        }
        else
        {
            builder.SetRunningAddress(builder.Configuration);
        }

        builder.Services.AddMemoryCache();
        builder.Services.AddBarkFluffGrpc();
        builder.Services.AddBarkFluffMetrics("BarkFluff.Beacon");
        builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<Program>());
        if (builder.Environment.IsDevelopment())
            builder.Services.AddGrpcReflection();
        builder.Services.AddSettings<ServerColorSettings>(builder.Configuration, "ServerColor");
        builder.Services.AddSettings<ServerPropsSettings>(builder.Configuration, "ServerProps");
        builder.Services.AddGrpcClient<BarkFluff.Proto.Navigator.NavigatorApi.NavigatorApiClient>(o =>
        {
            o.Address = new Uri(builder.Configuration["NavigatorUrl"]);
        });
        builder.Services.AddGrpcClient<ConfigurationApi.ConfigurationApiClient>(o =>
        {
            o.Address = new Uri(builder.Configuration["ConfigurationServiceAddr"]);
        });
        builder.Services.AddHostedService<ServerRegistrationService>();

        var app = builder.Build();

        // Гейдж со временем старта сервиса — позволяет админке вычислять uptime.
        var startupMetrics = app.Services.GetRequiredService<MetricsCollector>();
        startupMetrics.Set("service_started_unix", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        startupMetrics.Set("navigator_registration_healthy", 0);

        if (app.Environment.IsDevelopment())
            app.MapGrpcReflectionService();
        app.UseRouting();
        app.MapGrpcService<BeaconApiService>();
        app.Lifetime.ApplicationStopped.Register(Log.CloseAndFlush);
        app.Run();
    }
}
