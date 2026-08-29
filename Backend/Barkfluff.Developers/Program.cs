using BarkFluff.GrpcServer;
using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Shared.Identity;
using Barkfluff.Developers.Host;
using Barkfluff.Developers.Infrastructure;
using Barkfluff.Developers.Persistence.Contexts;
using Barkfluff.Developers.Persistence.Services;

using Microsoft.EntityFrameworkCore;

using Serilog;

namespace Barkfluff.Developers;

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.LoadConfiguration(ServiceId.Developers);
        builder.AddBarkFluffSerilog("BarkFluff.Developers");

        var port = builder.Configuration.GetValue<int>("RunSettings:Port");
        if (int.TryParse(Environment.GetEnvironmentVariable("DEVELOPERS_PORT"), out var configuredPort))
            port = configuredPort;
        if (port <= 0) port = 7020;

        var staticPort = builder.Configuration.GetValue<int>("RunSettings:Http1Port");
        if (int.TryParse(Environment.GetEnvironmentVariable("DEVELOPERS_HTTP1PORT"), out var configuredStaticPort))
            staticPort = configuredStaticPort;
        if (staticPort <= 0) staticPort = 7021;
        if (staticPort == port)
            throw new InvalidOperationException("Developers API and static ports must be different.");

        builder.WebHost.ConfigureKestrel(options =>
        {
            options.ListenAnyIP(port, listenOptions =>
            {
                listenOptions.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http1AndHttp2;
            });

            options.ListenAnyIP(staticPort, listenOptions =>
            {
                listenOptions.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http1;
            });
        });

        builder.Services.AddBarkFluffGrpc();
        builder.Services.AddBarkFluffMetrics("BarkFluff.Developers");
        if (builder.Environment.IsDevelopment())
            builder.Services.AddGrpcReflection();
        builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<Program>());
        builder.Services.AddXAuth(builder.Configuration);
        builder.Services.AddDevelopersAuthorization();

        builder.Services.AddDbContext<DevelopersContext>(c
            => c.UseNpgsql(builder.Configuration["DevelopersDb"], npgsql =>
            {
                npgsql.EnableRetryOnFailure(3);
                npgsql.CommandTimeout(30);
            }));

        builder.Services.AddTransient<DocumentationStorage>();
        builder.Services.AddTransient<ProtoMetadataStorage>();
        builder.Services.AddSingleton<ProtoFileProvider>();
        builder.Services.AddSingleton<IProtoFileSource>(services =>
            services.GetRequiredService<ProtoFileProvider>());
        builder.Services.AddSingleton<IPublishedProtoCatalog, PublishedProtoCatalog>();
        builder.Services.AddSingleton<ErrorCodeSeeder>();
        builder.Services.AddSingleton<DevelopersStartupInitializer>();

        var allowedOrigins = GetAllowedOrigins(builder.Configuration, builder.Environment);
        builder.Services.AddCors(o => o.AddPolicy("DevelopersCors", p =>
        {
            p.WithOrigins(allowedOrigins)
             .WithMethods("POST", "OPTIONS")
             .AllowAnyHeader()
             .WithExposedHeaders("grpc-status", "grpc-message", "grpc-status-details-bin", "x-error-code");
        }));

        builder.Services.AddBarkFluffHealth();

        var app = builder.Build();

        try
        {
            await app.Services
                .GetRequiredService<DevelopersStartupInitializer>()
                .InitializeAsync();
        }
        catch (Exception exception)
        {
            app.Logger.LogCritical(
                exception,
                "Developers startup initialization failed; the application will not start");
            Log.CloseAndFlush();
            throw;
        }

        var staticRoot = app.Environment.WebRootPath ?? Path.Combine(app.Environment.ContentRootPath, "wwwroot");
        var staticIndex = Path.Combine(staticRoot, "index.html");

        app.MapWhen(context => context.Connection.LocalPort == staticPort, staticApplication =>
        {
            staticApplication.UseDefaultFiles();
            staticApplication.UseStaticFiles();
            staticApplication.Run(async context =>
            {
                if ((context.Request.Method != HttpMethods.Get && context.Request.Method != HttpMethods.Head)
                    || !File.Exists(staticIndex))
                {
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    return;
                }

                await context.Response.SendFileAsync(staticIndex);
            });
        });

        app.UseRouting();
        app.UseCors("DevelopersCors");
        app.UseGrpcWeb();
        app.UseXAuth();

        if (app.Environment.IsDevelopment())
            app.MapGrpcReflectionService();
        app.MapGrpcService<DevelopersApiService>().EnableGrpcWeb();
        app.MapHealthEndpoints();

        app.Lifetime.ApplicationStopped.Register(Log.CloseAndFlush);
        await app.RunAsync();
    }

    private static string[] GetAllowedOrigins(IConfiguration configuration, IHostEnvironment environment)
    {
        var configuredOrigins = configuration
            .GetSection("Developers:AllowedOrigins")
            .GetChildren()
            .Select(section => section.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .ToArray();

        if (configuredOrigins.Length > 0)
        {
            return configuredOrigins
                .Where(origin => environment.IsDevelopment() || !IsLocalhostOrigin(origin))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        return environment.IsDevelopment()
            ? ["https://developers.barkfluff.com", "http://localhost:5173"]
            : ["https://developers.barkfluff.com"];
    }

    private static bool IsLocalhostOrigin(string origin)
    {
        return Uri.TryCreate(origin, UriKind.Absolute, out var uri)
            && (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                || uri.Host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase)
                || uri.Host.Equals("[::1]", StringComparison.OrdinalIgnoreCase));
    }
}
