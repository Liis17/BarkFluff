using BarkFluff.GrpcServer;
using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Navigator.Host;
using BarkFluff.Navigator.Persistence;

using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Порт можно задать через переменные окружения NAVIGATOR_PORT или RunSettings__Port
var envPort = Environment.GetEnvironmentVariable("NAVIGATOR_PORT")
              ?? Environment.GetEnvironmentVariable("RunSettings__Port");

if (int.TryParse(envPort, out var dynamicPort))
{
    builder.WebHost.ConfigureKestrel(options =>
    {
        options.ListenAnyIP(dynamicPort, o =>
        {
            o.Protocols = HttpProtocols.Http2;
        });
    });
}
else
{
    // fallback на конфигурацию (RunSettings секция)
    builder.SetRunningAddress(builder.Configuration);
}

builder.Services.AddBarkFluffGrpc();
builder.Services.AddGrpcReflection();

builder.Services.AddDbContext<NavigatorContext>(c
    => c.UseNpgsql(builder.Configuration["NavigatorDb"]));

builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<Program>());

builder.Services.AddXAuth(builder.Configuration);

builder.Services.AddTransient<ServersStorage>();

builder.Services.AddMemoryCache();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var ctx = scope.ServiceProvider.GetRequiredService<NavigatorContext>();
    ctx.Database.Migrate();
}

app.MapGrpcReflectionService();
app.UseRouting();
app.UseXAuth();

app.MapGrpcService<NavigatorApiService>();

app.Run();