using BarkFluff.GrpcServer;
using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Navigator.Admin;
using BarkFluff.Navigator.Features.RegisterServer;
using BarkFluff.Navigator.Host;
using BarkFluff.Navigator.Persistence;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;

using System.Security.Claims;

var builder = WebApplication.CreateBuilder(args);

var grpcPort = GetPort("NAVIGATOR_PORT", builder.Configuration["RunSettings:Port"], 7010);
var adminHttpPort = GetPort("NAVIGATOR_HTTP_PORT", builder.Configuration["RunSettings:Http1Port"], 7011);

if (grpcPort == adminHttpPort)
    throw new InvalidOperationException("NAVIGATOR_PORT and NAVIGATOR_HTTP_PORT must be different.");

builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(grpcPort, o => o.Protocols = HttpProtocols.Http2);
    options.ListenAnyIP(adminHttpPort, o => o.Protocols = HttpProtocols.Http1);
});

builder.AddBarkFluffSerilog("BarkFluff.Navigator");
builder.Services.AddBarkFluffGrpc();
builder.Services.AddBarkFluffMetrics("BarkFluff.Navigator");
builder.Services.AddGrpcReflection();

builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<Program>());

builder.Services.AddXAuth(builder.Configuration);
builder.Services
    .AddAuthentication()
    .AddCookie(NavigatorAdminAuthentication.Scheme, options =>
    {
        options.Cookie.Name = "__Host-navigator-admin";
        options.Cookie.Path = "/";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
    });
builder.Services.AddSingleton<AdminCredentials>();

// Navigator вне платформенного шаблона (публичная инфраструктура вне ноды) — без LoadConfiguration,
// локальная SQLite-БД: путь через переменную окружения NAVIGATOR_DB, фолбэк — ключ NavigatorDb в
// appsettings, дефолт — файл рядом с сервисом.
var navigatorConnectionString = Environment.GetEnvironmentVariable("NAVIGATOR_DB")
    ?? builder.Configuration["NavigatorDb"]
    ?? "Data Source=navigator.db";

builder.Services.AddDbContext<NavigatorContext>(o => o.UseSqlite(navigatorConnectionString));

builder.Services.AddScoped<ServersStorage>();
builder.Services.AddSingleton<RegistrationThrottle>();
builder.Services.AddScoped<FederationWellKnownValidator>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var ctx = scope.ServiceProvider.GetRequiredService<NavigatorContext>();
    ctx.Database.EnsureCreated();
    EnsureServersColumn(ctx, "WebEndpoint", "TEXT NULL");
}

// EnsureCreated() создаёт схему только для новой БД, миграций в проекте нет —
// поэтому поля, добавленные позже, дописываем сами. Без этого на существующем
// navigator.db любой запрос к новой колонке падает с "no such column".
static void EnsureServersColumn(NavigatorContext ctx, string column, string definition)
{
    var connection = ctx.Database.GetDbConnection();
    if (connection.State != System.Data.ConnectionState.Open)
        connection.Open();

    using (var check = connection.CreateCommand())
    {
        check.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('Servers') WHERE name = '{column}';";
        if (Convert.ToInt64(check.ExecuteScalar()) > 0)
            return;
    }

    using var alter = connection.CreateCommand();
    alter.CommandText = $"ALTER TABLE \"Servers\" ADD COLUMN \"{column}\" {definition};";
    alter.ExecuteNonQuery();
}

app.MapGrpcReflectionService();
app.UseRouting();
app.UseXAuth();
app.UseStaticFiles();

app.MapGrpcService<NavigatorApiService>();

var adminPolicy = new AuthorizeAttribute { AuthenticationSchemes = NavigatorAdminAuthentication.Scheme };

app.MapPost("/admin/api/login", async (AdminLoginRequest request, HttpContext context, AdminCredentials credentials) =>
{
    if (!credentials.IsValid(request.Username, request.Password))
        return Results.Unauthorized();

    var identity = new ClaimsIdentity(
        [new Claim(ClaimTypes.Name, request.Username!)],
        NavigatorAdminAuthentication.Scheme);

    await context.SignInAsync(
        NavigatorAdminAuthentication.Scheme,
        new ClaimsPrincipal(identity),
        new AuthenticationProperties { IsPersistent = true });

    return Results.NoContent();
});

app.MapPost("/admin/api/logout", async (HttpContext context) =>
{
    await context.SignOutAsync(NavigatorAdminAuthentication.Scheme);
    return Results.NoContent();
}).RequireAuthorization(adminPolicy);

app.MapGet("/admin/api/session", (ClaimsPrincipal user) =>
    Results.Ok(new { username = user.Identity!.Name }))
    .RequireAuthorization(adminPolicy);

app.MapGet("/admin/api/servers", async (ServersStorage serversStorage, CancellationToken cancellationToken) =>
{
    var servers = await serversStorage.GetServersAsync(cancellationToken);

    return Results.Ok(servers
        .OrderBy(server => server.Name)
        .Select(server => new
        {
            server.Name,
            server.ServerPublicName,
            server.Description,
            server.Location,
            beaconHost = server.BeaconHost,
            beaconPort = server.BeaconPort,
            webEndpoint = server.WebEndpoint ?? string.Empty,
            server.LastSeenAt,
            color = server.ColorMainHex
        }));
}).RequireAuthorization(adminPolicy);

app.MapFallbackToFile("/admin/{*path:nonfile}", "admin/index.html");

app.Run();

static int GetPort(string environmentVariable, string? configuredValue, int defaultValue)
{
    var value = Environment.GetEnvironmentVariable(environmentVariable) ?? configuredValue;
    return int.TryParse(value, out var port) && port is > 0 and <= 65535 ? port : defaultValue;
}

file static class NavigatorAdminAuthentication
{
    public const string Scheme = "NavigatorAdmin";
}

file sealed record AdminLoginRequest(string? Username, string? Password);
