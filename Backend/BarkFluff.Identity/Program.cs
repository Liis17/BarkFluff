using BarkFluff.GrpcServer;
using BarkFluff.Identity.Host;
using BarkFluff.Identity.Persistence.Contexts;
using BarkFluff.Identity.Settings;
using BarkFluff.Proto.Users;
using BarkFluff.Shared.Exceptions.Interceptors;
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
        
        builder.Services.AddGrpcClient<UsersServerApi.UsersServerApiClient>(o =>
        {
            o.Address = new Uri(builder.Configuration["UsersService"]);
        });

        builder.Services.AddDbContext<IdentityContext>(c 
            => c.UseNpgsql(builder.Configuration["IdentityDb"]));
        
        builder.Services.AddSettings<JwtSettings>(builder.Configuration, "JwtSettings");
        
        var app = builder.Build();

        app.MapGrpcReflectionService();
        app.UseRouting();
        app.MapGrpcService<IdentityApiService>();

        app.Run();
    }
}