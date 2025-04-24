using BarkFluff.GrpcServer;
using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Proto.Users;
using BarkFluff.Shared.Exceptions.Interceptors;
using BarkFluff.Users.Host;
using BarkFluff.Users.Persistence.Contexts;
using BarkFluff.Users.Persistence.Services;
using Microsoft.EntityFrameworkCore;

namespace BarkFluff.Users;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        
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

        builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<Program>());

        // Регистрируем аутентификацию и авторизацию
        builder.Services.AddXAuth(options =>
        {
            options.Secret = builder.Configuration["JwtSettings:SecretKey"];
            options.Issuer = builder.Configuration["JwtSettings:Issuer"];
            options.Audience = builder.Configuration["JwtSettings:Audience"];
        });
        

        var app = builder.Build();
        
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