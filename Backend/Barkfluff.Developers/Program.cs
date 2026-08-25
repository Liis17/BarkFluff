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
    public static void Main(string[] args)
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

        builder.Services.AddDbContext<DevelopersContext>(c
            => c.UseNpgsql(builder.Configuration["DevelopersDb"], npgsql =>
            {
                npgsql.EnableRetryOnFailure(3);
                npgsql.CommandTimeout(30);
            }));

        builder.Services.AddTransient<DocumentationStorage>();
        builder.Services.AddTransient<ProtoMetadataStorage>();
        builder.Services.AddSingleton<ProtoFileProvider>();
        builder.Services.AddSingleton<ErrorCodeSeeder>();

        builder.Services.AddCors(o => o.AddPolicy("DevelopersCors", p =>
        {
            p.AllowAnyOrigin()
             .AllowAnyMethod()
             .AllowAnyHeader()
             .WithExposedHeaders("grpc-status", "grpc-message", "grpc-status-details-bin", "x-error-code");
        }));

        builder.Services.AddBarkFluffHealth();

        var app = builder.Build();

        using (var scope = app.Services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<DevelopersContext>();
            ctx.Database.Migrate();

            var seeder = scope.ServiceProvider.GetRequiredService<ErrorCodeSeeder>();
            seeder.SeedIfNeeded(ctx).GetAwaiter().GetResult();

            var docStorage = scope.ServiceProvider.GetRequiredService<DocumentationStorage>();
            docStorage.SeedIfNeeded().GetAwaiter().GetResult();

            var protoStorage = scope.ServiceProvider.GetRequiredService<ProtoMetadataStorage>();
            protoStorage.SeedIfNeeded().GetAwaiter().GetResult();
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
        app.Run();
    }
}
