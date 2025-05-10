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

        builder.Services.AddDbContext<ConfigurationContext>(c 
            => c.UseNpgsql(builder.Configuration["ConfigurationDb"]));
        
        builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<Program>());

        builder.Services.AddTransient<ConfigurationStorage>();
        
        var app = builder.Build();
        
        app.MapGrpcReflectionService();
        app.UseRouting();

        app.MapGrpcService<ConfigurationApiService>();

        app.Run();
        
    }
}