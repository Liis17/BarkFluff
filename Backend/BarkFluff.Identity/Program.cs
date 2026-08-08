using BarkFluff.GrpcServer;
using BarkFluff.GrpcServer.Metrics;
using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Identity.Consumers;
using BarkFluff.Identity.Host;
using BarkFluff.Identity.Infrastructure;
using BarkFluff.Identity.Persistence.Contexts;
using BarkFluff.Identity.Persistence.Services;
using BarkFluff.Identity.Services;
using BarkFluff.Identity.Settings;
using BarkFluff.Proto.Users;
using BarkFluff.Shared.Auth;
using BarkFluff.Shared.Exceptions.Interceptors;
using BarkFluff.Shared.Identity;

using MassTransit;

using Microsoft.EntityFrameworkCore;

using Serilog;

namespace BarkFluff.Identity;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.LoadConfiguration(ServiceId.Identity);
        builder.AddBarkFluffSerilog("BarkFluff.Identity");
        builder.SetRunningAddress(builder.Configuration);

        builder.Services.AddBarkFluffGrpc();
        builder.Services.AddBarkFluffMetrics("BarkFluff.Identity");
        builder.Services.AddGrpcReflection();

        builder.Services.AddDbContext<IdentityContext>(c
            => c.UseNpgsql(builder.Configuration["IdentityDb"], npgsql =>
            {
                npgsql.EnableRetryOnFailure(3);
                npgsql.CommandTimeout(30);
            }));

        builder.Services.AddSettings<JwtSettings>(builder.Configuration, "JwtSettings");

        builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<Program>());

        builder.Services.AddXAuth(builder.Configuration);

        builder.Services.AddCors(o => o.AddPolicy("IdentityCors", p =>
        {
            p.AllowAnyOrigin()
             .AllowAnyMethod()
             .AllowAnyHeader()
             .WithExposedHeaders("grpc-status", "grpc-message", "grpc-status-details-bin", "x-error-code");
        }));

        builder.Services.AddGrpcClient<UsersServerApi.UsersServerApiClient>(o =>
            {
                o.Address = new Uri(builder.Configuration["UsersService:Host"]);
            }).AddInterceptor(() => new JwtClientInterceptor(builder.Configuration["UsersService:Token"]))
            .AddInterceptor(() => new ExceptionClientInterceptor());

        builder.Services.AddTransient<RefreshTokensStorage>();
        builder.Services.AddTransient<JwtService>();
        builder.Services.AddTransient<ConfirmationCodesStorage>();
        builder.Services.AddScoped<NotificationQueueSender>();
        builder.Services.AddHttpClient<LocationClient>();
        builder.Services.AddScoped<LocationClient>();
        builder.Services.AddTransient<AuthPropertiesStorage>();
        builder.Services.AddTransient<PasswordsStorage>();
        builder.Services.AddTransient<ResetPasswordsStorage>();

        builder.Services.AddMassTransit(x =>
        {
            x.AddConsumer<SessionRevokedConsumer>();

            x.UsingRabbitMq((context, cfg) =>
            {
                cfg.Host(builder.Configuration["RabbitMQ:Host"], "/", h =>
                {
                    h.Username(builder.Configuration["RabbitMQ:Username"]);
                    h.Password(builder.Configuration["RabbitMQ:Password"]);
                });

                cfg.ReceiveEndpoint($"session-revoked-identity-{InstanceId.Current}", e =>
                {
                    e.AutoDelete = true;
                    e.Durable = false;
                    e.ConfigureConsumer<SessionRevokedConsumer>(context);
                });
            });
        });

        var app = builder.Build();

        using (var scope = app.Services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<IdentityContext>();
            ctx.Database.Migrate();
        }

        var metrics = app.Services.GetRequiredService<MetricsCollector>();
        metrics.Set("service_started_unix", DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        app.UseRouting();
        app.UseCors("IdentityCors");
        app.UseGrpcWeb();
        app.MapGrpcReflectionService();

        app.UseXAuth();
        app.MapPingEndpoint();

        app.MapGrpcService<IdentityApiService>().EnableGrpcWeb();
        app.MapGrpcService<IdentityServerApiService>();

        app.Lifetime.ApplicationStopped.Register(Log.CloseAndFlush);
        app.Run();

    }
}
