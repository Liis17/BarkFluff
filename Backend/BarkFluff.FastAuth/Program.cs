using BarkFluff.FastAuth.Host;
using BarkFluff.GrpcServer;
using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Proto.Identity;
using BarkFluff.Shared.Auth;
using BarkFluff.Shared.Exceptions.Interceptors;
using BarkFluff.Shared.Identity;

using Serilog;

using StackExchange.Redis;

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
        if (builder.Environment.IsDevelopment())
            builder.Services.AddGrpcReflection();

        builder.Services.AddXAuth(builder.Configuration);

        // Redis — общий стор QR-сессий + pub/sub wake-up стримов (масштабирование, см. docs/scaling/fastauth.md).
        builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
            ConnectionMultiplexer.Connect(builder.Configuration["Redis"]
                ?? throw new InvalidOperationException("Redis configuration is missing")));

        builder.Services.AddGrpcClient<IdentityServerApi.IdentityServerApiClient>(o =>
            {
                o.Address = new Uri(builder.Configuration["IdentityService:Host"] ?? "http://identity:7000");
            })
            .AddInterceptor(() => new JwtClientInterceptor(builder.Configuration["IdentityService:Token"]
                ?? throw new InvalidOperationException("IdentityService:Token not configured")))
            .AddInterceptor(() => new ExceptionClientInterceptor());

        builder.Services.AddFastAuthServices();

        builder.Services.AddBarkFluffHealth();

        var app = builder.Build();

        if (app.Environment.IsDevelopment())
            app.MapGrpcReflectionService();

        app.UseRouting();

        app.UseXAuth();
        app.MapHealthEndpoints();

        app.MapGrpcService<FastAuthApiService>();
        app.MapGrpcService<FastAuthServerApiService>();

        app.Lifetime.ApplicationStopped.Register(Log.CloseAndFlush);
        app.Run();
    }
}
