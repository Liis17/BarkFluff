using BarkFluff.GrpcServer;
using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Proto.Files;
using BarkFluff.Proto.Messages;
using BarkFluff.Shared.Auth;
using BarkFluff.Shared.Exceptions.Interceptors;
using BarkFluff.Shared.Identity;
using BarkFluff.Users.Host;
using BarkFluff.Users.Infrastructure;
using BarkFluff.Users.Persistence.Contexts;
using BarkFluff.Users.Persistence.Services;

using MassTransit;

using Microsoft.EntityFrameworkCore;

namespace BarkFluff.Users;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.LoadConfiguration(ServiceId.Users);
        builder.SetRunningAddress(builder.Configuration);

        // Регистрируем gRPC сервисы с интерцепторами
        builder.Services.AddGrpc(options =>
        {
            options.Interceptors.Add<ServerExceptionInterceptor>();
        });

        builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<Program>());

        builder.Services.AddGrpcReflection();

        builder.Services.AddDbContext<UsersContext>(c
            => c.UseNpgsql(builder.Configuration["UsersDb"]));

        builder.Services.AddTransient<UsersStorage>();
        builder.Services.AddTransient<DevicesStorage>();
        builder.Services.AddScoped<UserInfoQueueSender>();

        builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<Program>());

        // Регистрируем аутентификацию и авторизацию
        builder.Services.AddXAuth(builder.Configuration);

        builder.Services.AddGrpcClient<FilesServerApi.FilesServerApiClient>(o =>
            {
                o.Address = new Uri(builder.Configuration["FilesService:Host"]);
            }).AddInterceptor(() => new JwtClientInterceptor(builder.Configuration["FilesService:Token"]))
            .AddInterceptor(() => new ExceptionClientInterceptor());

        builder.Services.AddGrpcClient<MessagesServerApi.MessagesServerApiClient>(o =>
            {
                o.Address = new Uri(builder.Configuration["MessagesService:Host"]);
            }).AddInterceptor(() => new JwtClientInterceptor(builder.Configuration["MessagesService:Token"]))
            .AddInterceptor(() => new ExceptionClientInterceptor());

        builder.Services.AddMassTransit(x =>
        {
            x.UsingRabbitMq((context, cfg) =>
            {
                cfg.Host(builder.Configuration["RabbitMQ:Host"], "/", h =>
                {
                    h.Username(builder.Configuration["RabbitMQ:Username"]);
                    h.Password(builder.Configuration["RabbitMQ:Password"]);
                });
            });
        });

        var app = builder.Build();

        // Применение миграций базы данных
        using (var scope = app.Services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<UsersContext>();
            ctx.Database.Migrate();
        }

        app.MapGrpcReflectionService();

        // Настраиваем middleware pipeline
        app.UseRouting();

        app.UseXAuth();

        // Регистрируем gRPC сервисы
        app.MapGrpcService<UsersServerApiService>();
        app.MapGrpcService<UsersApiService>();

        app.Run();
    }
}