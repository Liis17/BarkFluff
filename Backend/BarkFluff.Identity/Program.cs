using BarkFluff.GrpcServer;
using BarkFluff.Identity.Host;
using BarkFluff.Identity.Settings;
using BarkFluff.Proto.Users;

namespace BarkFluff.Identity;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddGrpc();
        builder.Services.AddGrpcReflection();
        
        builder.Services.AddGrpcClient<UsersServerApi.UsersServerApiClient>(o =>
        {
            o.Address = new Uri(builder.Configuration["UsersService"]);
        });
        
        builder.Services.AddSettings<JwtSettings>(builder.Configuration, "JwtSettings");


        var app = builder.Build();

        app.UseRouting();
        app.UseEndpoints(endpoints =>
        {
            endpoints.MapGrpcService<IdentityApiService>(); 
            endpoints.MapGrpcReflectionService();
        });

        app.Run();
    }
}