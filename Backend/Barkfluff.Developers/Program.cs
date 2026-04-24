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

        builder.WebHost.ConfigureKestrel(options =>
        {
            var port = builder.Configuration.GetValue<int>("RunSettings:Port");
            if (port <= 0) port = 7020;

            options.ListenAnyIP(port, listenOptions =>
            {
                listenOptions.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http2;
            });
        });

        builder.Services.AddBarkFluffGrpc();
        builder.Services.AddGrpcReflection();
        builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<Program>());
        builder.Services.AddXAuth(builder.Configuration);

        builder.Services.AddDbContext<DevelopersContext>(c
            => c.UseNpgsql(builder.Configuration["DevelopersDb"]));

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

        app.UseRouting();
        app.UseCors("DevelopersCors");
        app.UseGrpcWeb();
        app.UseXAuth();

        app.MapGrpcReflectionService();
        app.MapGrpcService<DevelopersApiService>().EnableGrpcWeb();

        app.Lifetime.ApplicationStopped.Register(Log.CloseAndFlush);
        app.Run();
    }
}
