using BarkFluff.FastAuth.Host;
using BarkFluff.GrpcServer;
using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Proto.Identity;
using BarkFluff.Shared.Auth;
using BarkFluff.Shared.Exceptions.Interceptors;
using BarkFluff.Shared.Identity;

using Serilog;

namespace BarkFluff.FastAuth;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.LoadConfiguration(ServiceId.FastAuth);
        builder.AddBarkFluffSerilog("BarkFluff.FastAuth");
        builder.SetRunningAddress(builder.Configuration);

        builder.Services.AddBarkFluffGrpc();
        builder.Services.AddBarkFluffMetrics("BarkFluff.FastAuth");
        builder.Services.AddGrpcReflection();

        builder.Services.AddXAuth(builder.Configuration);

        builder.Services.AddGrpcClient<IdentityServerApi.IdentityServerApiClient>(o =>
            {
                o.Address = new Uri(builder.Configuration["IdentityService:Host"] ?? "http://identity:7000");
            })
            .AddInterceptor(() => new JwtClientInterceptor(builder.Configuration["IdentityService:Token"]))
            .AddInterceptor(() => new ExceptionClientInterceptor());

        builder.Services.AddFastAuthServices();

        var app = builder.Build();

        app.MapGrpcReflectionService();

        app.UseRouting();

        app.UseXAuth();

        app.MapGrpcService<FastAuthApiService>();
        app.MapGrpcService<FastAuthServerApiService>();

        app.Lifetime.ApplicationStopped.Register(Log.CloseAndFlush);
        app.Run();
    }
}
