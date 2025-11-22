using BarkFluff.Configuration.Host;
using BarkFluff.Configuration.Infrastructure;
using BarkFluff.GrpcServer;

using Microsoft.EntityFrameworkCore;

namespace BarkFluff.Configuration;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.SetRunningAddress(builder.Configuration);

        builder.Services.AddGrpc(options =>
        {
            options.Interceptors.Add<ServerExceptionInterceptor>();
        });

        builder.Services.AddGrpcReflection();

        var host = builder.Configuration["CONFIGURATION_HOST"];
        var database = builder.Configuration["CONFIGURATION_DATABASE"];
        var username = builder.Configuration["CONFIGURATION_USERNAME"];
        var password = builder.Configuration["CONFIGURATION_PASSWORD"];

        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(database)
            || string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException(
                "Database settings are not configured. Set CONFIGURATION_HOST, CONFIGURATION_DATABASE, CONFIGURATION_USERNAME, CONFIGURATION_PASSWORD.");
        }

        var configurationDb = $"Host={host};Database={database};Username={username};Password={password}";

        builder.Services.AddDbContext<ConfigurationContext>(c => c.UseNpgsql(configurationDb));

        builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<Program>());

        builder.Services.AddTransient<ConfigurationStorage>();

        var app = builder.Build();

        using (var scope = app.Services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<ConfigurationContext>();
            ctx.Database.Migrate();
        }

        app.MapGrpcReflectionService();
        app.UseRouting();

        app.MapGrpcService<ConfigurationApiService>();

        app.Run();
    }
}