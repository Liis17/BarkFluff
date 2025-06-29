using BarkFluff.Beacon.Configurations;
using BarkFluff.Beacon.Host;
using BarkFluff.GrpcServer;
using BarkFluff.Proto.Configuration;
using BarkFluff.Shared.Identity;

namespace BarkFluff.Beacon;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.LoadConfiguration(ServiceId.Beacon);
        builder.SetRunningAddress(builder.Configuration);

        builder.Services.AddBarkFluffGrpc();
        
        builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<Program>());

        builder.Services.AddGrpcReflection();
        
        builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<Program>());
        
        builder.Services.AddSettings<ServerColorSettings>(builder.Configuration, "ServerColor");
        builder.Services.AddSettings<ServerPropsSettings>(builder.Configuration, "ServerProps");
        
        builder.Services.AddGrpcClient<BarkFluff.Proto.Navigator.NavigatorApi.NavigatorApiClient>(o =>
        {
            o.Address = new Uri(builder.Configuration["NavigatorUrl"]); 
        });
      
        builder.Services.AddHostedService<BarkFluff.Beacon.Features.RegisterServer.ServerRegistrationService>();

        var app = builder.Build();

        app.MapGrpcReflectionService();

        app.UseRouting();

        app.MapGrpcService<BeaconApiService>();

        app.Run();
        
    }
}