using BarkFluff.Beacon.Host;
using BarkFluff.GrpcServer;
using BarkFluff.GrpcServer.XAuth;

namespace BarkFluff.Beacon;

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
        
        builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<Program>());

        builder.Services.AddGrpcReflection();
        
        builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<Program>());
      
        var app = builder.Build();

        app.MapGrpcReflectionService();

        app.UseRouting();

        app.MapGrpcService<BeaconApiService>();

        app.Run();
    }
}