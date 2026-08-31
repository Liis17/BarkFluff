extern alias GrpcServer;

using BarkFluff.Settings.Infrastructure;
using BarkFluff.Settings.Host;
using BarkFluff.Settings.Persistence.Contexts;
using BarkFluff.Settings.Persistence.Services;
using BarkFluff.Settings.Settings;

using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;

using Serilog;

namespace BarkFluff.Settings;

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        var port = 7003;
        var configuredPort = builder.Configuration["SETTINGS_PORT"]
            ?? builder.Configuration["CONFIGURATION_PORT"]
            ?? builder.Configuration["RunSettings__Port"];
        if (int.TryParse(configuredPort, out var parsedPort))
            port = parsedPort;

        builder.WebHost.ConfigureKestrel(options =>
            options.ListenAnyIP(port, listen => listen.Protocols = HttpProtocols.Http2));

        GrpcServer::BarkFluff.GrpcServer.SerilogExtensions.AddBarkFluffSerilog(builder, "BarkFluff.Settings");
        GrpcServer::BarkFluff.GrpcServer.SerilogExtensions.AddBarkFluffMetrics(builder.Services, "BarkFluff.Settings");
        GrpcServer::BarkFluff.GrpcServer.ServiceCollectionExtensions.AddBarkFluffGrpc(builder.Services);
        if (builder.Environment.IsDevelopment())
            builder.Services.AddGrpcReflection();
        builder.Services.AddMediatR(configuration => configuration.RegisterServicesFromAssemblyContaining<Program>());

        var databaseOptions = SettingsDatabaseOptions.FromConfiguration(builder.Configuration);
        var setupOptions = SettingsSetupOptions.FromConfiguration(builder.Configuration);
        builder.Services.AddSingleton(databaseOptions);
        builder.Services.AddSingleton(setupOptions);
        builder.Services.AddSingleton(SettingsSeedOptions.FromConfiguration(databaseOptions, builder.Configuration));
        builder.Services.AddDbContext<SettingsContext>(options =>
            options.UseNpgsql(databaseOptions.ConnectionString, npgsql =>
            {
                npgsql.UseAdminDatabase(databaseOptions.AdminDatabase);
                npgsql.EnableRetryOnFailure(3);
                npgsql.CommandTimeout(30);
            }));

        builder.Services.AddSingleton<SettingsStartupInitializer>();
        builder.Services.AddScoped<SettingsSeeder>();
        builder.Services.AddScoped<SettingsStorage>();
        builder.Services.AddScoped<SettingsSetupCoordinator>();
        builder.Services.AddScoped<SettingsReadinessContributor>();
        builder.Services.AddScoped<GrpcServer::BarkFluff.GrpcServer.IBarkFluffReadinessContributor>(provider =>
            provider.GetRequiredService<SettingsReadinessContributor>());
        GrpcServer::BarkFluff.GrpcServer.HealthServiceCollectionExtensions.AddBarkFluffHealth(builder.Services);

        var app = builder.Build();

        try
        {
            await app.Services.GetRequiredService<SettingsStartupInitializer>().InitializeAsync();
        }
        catch (Exception exception)
        {
            app.Logger.LogCritical(exception, "Settings startup initialization failed; the application will not start");
            await Log.CloseAndFlushAsync();
            throw;
        }

        app.UseRouting();
        if (app.Environment.IsDevelopment())
            app.MapGrpcReflectionService();
        app.MapGrpcService<SettingsApiService>();
        app.MapGrpcService<SettingsSetupApiService>();
        GrpcServer::BarkFluff.GrpcServer.HealthEndpointExtensions.MapHealthEndpoints(app);

        app.Lifetime.ApplicationStopped.Register(Log.CloseAndFlush);
        await app.RunAsync();
    }
}
