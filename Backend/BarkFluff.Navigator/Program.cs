using BarkFluff.GrpcServer;
using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Navigator.Features.RegisterServer;
using BarkFluff.Navigator.Host;
using BarkFluff.Navigator.Persistence;

using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

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
    builder.SetRunningAddress(builder.Configuration);
}

builder.Services.AddBarkFluffGrpc();
builder.Services.AddGrpcReflection();

builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<Program>());

builder.Services.AddXAuth(builder.Configuration);

// Navigator вне платформенного шаблона (публичная инфраструктура вне ноды) — без LoadConfiguration,
// БД через переменную окружения NAVIGATOR_DB, фолбэк — ключ NavigatorDb в appsettings.
var navigatorConnectionString = Environment.GetEnvironmentVariable("NAVIGATOR_DB")
    ?? builder.Configuration["NavigatorDb"];

if (string.IsNullOrWhiteSpace(navigatorConnectionString))
    throw new InvalidOperationException("БД Navigator не сконфигурирована: задайте NAVIGATOR_DB (env) или NavigatorDb (appsettings).");

builder.Services.AddDbContext<NavigatorContext>(o => o.UseNpgsql(navigatorConnectionString, npgsql =>
{
    npgsql.EnableRetryOnFailure(3);
    npgsql.CommandTimeout(30);
}));

builder.Services.AddScoped<ServersStorage>();
builder.Services.AddSingleton<RegistrationThrottle>();
builder.Services.AddScoped<FederationWellKnownValidator>();

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
