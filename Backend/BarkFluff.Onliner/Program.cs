using BarkFluff.GrpcServer;
using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Onliner;
using BarkFluff.Onliner.Features.SubscribeToOnlineStatus;
using BarkFluff.Onliner.Host;
using BarkFluff.Onliner.Persistence.Contexts;
using BarkFluff.Shared.Exceptions.Interceptors;
using BarkFluff.Shared.Identity;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace BarkFluff.Onliner;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.LoadConfiguration(ServiceId.Onliner);
        builder.AddBarkFluffSerilog("BarkFluff.Onliner");
        builder.SetRunningAddress(builder.Configuration);

        // Регистрируем gRPC сервисы с интерцепторами
        builder.Services.AddGrpc(options =>
        {
            options.Interceptors.Add<ServerExceptionInterceptor>();
        });
        builder.Services.AddBarkFluffMetrics("BarkFluff.Onliner");

        builder.Services.AddGrpcReflection();

        builder.Services.AddDbContext<OnlineStatusContext>(c
            => c.UseNpgsql(builder.Configuration["OnlinerDb"]));

        // Регистрируем все Onliner сервисы (Storage, Notifier, Background Services, MediatR)
        builder.Services.AddOnlinerServices();

        // Регистрируем handler для streaming (не через MediatR)
        builder.Services.AddScoped<SubscribeToOnlineStatusQueryHandler>();

        // Регистрируем аутентификацию и авторизацию
        builder.Services.AddXAuth(builder.Configuration);

        var app = builder.Build();

        using (var scope = app.Services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<OnlineStatusContext>();
            ctx.Database.Migrate();
        }

        app.MapGrpcReflectionService();

        // Настраиваем middleware pipeline
        app.UseRouting();

        app.UseXAuth();

        // Регистрируем gRPC сервисы
        app.MapGrpcService<OnlinerApiService>();

        app.Lifetime.ApplicationStopped.Register(Log.CloseAndFlush);
        app.Run();
    }
}