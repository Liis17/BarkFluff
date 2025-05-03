using BarkFluff.GrpcServer;
using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Identity.Host;
using BarkFluff.Identity.Infrastructure;
using BarkFluff.Identity.Persistence.Contexts;
using BarkFluff.Identity.Persistence.Services;
using BarkFluff.Identity.Services;
using BarkFluff.Identity.Settings;
using BarkFluff.Proto.Users;
using BarkFluff.Shared.Exceptions.Interceptors;
using MassTransit;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;

namespace BarkFluff.Identity;

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

        builder.Services.AddDbContext<IdentityContext>(c 
            => c.UseNpgsql(builder.Configuration["IdentityDb"]));
        
        builder.Services.AddSettings<JwtSettings>(builder.Configuration, "JwtSettings");

        builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<Program>());
        
        builder.Services.AddXAuth(options =>
        {
            options.Secret = builder.Configuration["JwtSettings:SecretKey"];
            options.Issuer = builder.Configuration["JwtSettings:Issuer"];
            options.Audience = builder.Configuration["JwtSettings:Audience"];
        });
        
        
        builder.Services.AddGrpcClient<UsersServerApi.UsersServerApiClient>(o =>
        {
            o.Address = new Uri(builder.Configuration["UsersService"]);
        });

        builder.Services.AddTransient<RefreshTokensStorage>();
        builder.Services.AddTransient<JwtService>();
        builder.Services.AddTransient<ConfirmationCodesStorage>();
        builder.Services.AddScoped<NotificationQueueSender>();
        
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

        app.MapGrpcReflectionService();
        app.UseRouting();
        
        app.UseXAuth();
        
        app.MapGrpcService<IdentityApiService>();

        app.Run();
    }
}