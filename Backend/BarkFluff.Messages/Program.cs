using BarkFluff.GrpcServer;
using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Messages.Host;
using BarkFluff.Messages.Infrastructure;
using BarkFluff.Messages.Persistence;
using BarkFluff.Messages.Persistence.Services;
using BarkFluff.Proto.Files;
using BarkFluff.Proto.Users;
using BarkFluff.Shared.Auth;
using BarkFluff.Shared.Exceptions.Interceptors;
using BarkFluff.Shared.Identity;

using MassTransit;

using Microsoft.EntityFrameworkCore;

namespace BarkFluff.Messages;

using Consumers;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.LoadConfiguration(ServiceId.Messages);
        builder.SetRunningAddress(builder.Configuration);

        builder.Services.AddGrpc(options =>
        {
            options.Interceptors.Add<ServerExceptionInterceptor>();
        });
        builder.Services.AddGrpcReflection();

        builder.Services.AddDbContext<MessagesContext>(c
            => c.UseNpgsql(builder.Configuration["MessagesDb"]));

        builder.Services.AddStackExchangeRedisCache(options =>
        {
            options.Configuration = builder.Configuration["Redis"];
            options.InstanceName = "Messages_";
        });

        builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<Program>());

        builder.Services.AddXAuth(builder.Configuration);

        builder.Services.AddGrpcClient<UsersServerApi.UsersServerApiClient>(o =>
            {
                o.Address = new Uri(builder.Configuration["UsersService:Host"]);
            }).AddInterceptor(() => new JwtClientInterceptor(builder.Configuration["UsersService:Token"]))
            .AddInterceptor(() => new ExceptionClientInterceptor());

        builder.Services.AddGrpcClient<FilesServerApi.FilesServerApiClient>(o =>
            {
                o.Address = new Uri(builder.Configuration["FilesService:Host"]);
            }).AddInterceptor(() => new JwtClientInterceptor(builder.Configuration["FilesService:Token"]))
            .AddInterceptor(() => new ExceptionClientInterceptor());

        builder.Services.AddTransient<ChatsStorage>();
        builder.Services.AddScoped<ChatCache>();
        builder.Services.AddTransient<MessagesStorage>();
        builder.Services.AddTransient<MessageQueueSender>();
        builder.Services.AddTransient<ReadByQueueSender>();

        builder.Services.AddMassTransit(x =>
        {
            x.AddConsumer<UserChangedAvatarConsumer>();
            x.AddConsumer<UserChangedNameConsumer>();

            x.UsingRabbitMq((context, cfg) =>
            {
                cfg.Host(builder.Configuration["RabbitMQ:Host"], "/", h =>
                {
                    h.Username(builder.Configuration["RabbitMQ:Username"]);
                    h.Password(builder.Configuration["RabbitMQ:Password"]);
                });

                cfg.ReceiveEndpoint("user-changed-name-messages", e =>
                {
                    e.ConfigureConsumer<UserChangedNameConsumer>(context);
                });

                cfg.ReceiveEndpoint("user-changed-avatar-messages", e =>
                {
                    e.ConfigureConsumer<UserChangedAvatarConsumer>(context);
                });
            });
        });

        var app = builder.Build();

        using (var scope = app.Services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<MessagesContext>();
            ctx.Database.Migrate();
        }

        app.MapGrpcReflectionService();
        app.UseRouting();

        app.UseXAuth();

        app.MapGrpcService<MessagesApiService>();
        app.MapGrpcService<MessagesServerApiService>();

        app.Run();
    }
}