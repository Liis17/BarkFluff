using BarkFluff.GrpcServer;
using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Navigator.Admin;
using BarkFluff.Navigator.Domain;
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
    EnsureServersColumn(ctx, "FilesMediaEndpoint", "TEXT NULL");
    EnsureServersColumn(ctx, "IsManual", "INTEGER NOT NULL DEFAULT 0");
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
app.MapPingEndpoint();
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
            server.Id,
            server.Name,
            server.ServerPublicName,
            server.Description,
            server.Location,
            beaconHost = server.BeaconHost,
            beaconPort = server.BeaconPort,
            webEndpoint = server.WebEndpoint ?? string.Empty,
            filesMediaEndpoint = server.FilesMediaEndpoint ?? string.Empty,
            server.LastSeenAt,
            isManual = server.IsManual,
            color = server.ColorMainHex
        }));
}).RequireAuthorization(adminPolicy);

// Публичный список серверов для главной страницы / (без авторизации).
app.MapGet("/api/servers", async (ServersStorage serversStorage, CancellationToken cancellationToken) =>
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
            webEndpoint = server.WebEndpoint ?? string.Empty,
            isManual = server.IsManual,
            color = server.ColorMainHex
        }));
});

app.MapPost("/admin/api/servers", async (
    ManualServerRequest request,
    ClaimsPrincipal user,
    ServersStorage serversStorage,
    CancellationToken cancellationToken) =>
{
    var error = ManualServerValidation.Validate(request);
    if (error != null)
        return Results.BadRequest(new { error });

    var color = request.Color?.Trim() ?? string.Empty;

    await serversStorage.AddManualServerAsync(new ServerInfo
    {
        BeaconHost = (request.BeaconHost ?? string.Empty).Trim(),
        BeaconPort = request.BeaconPort ?? 0,
        Name = request.Name!.Trim(),
        Description = request.Description!.Trim(),
        ServerPublicName = request.ServerPublicName!.Trim(),
        Location = (request.Location ?? string.Empty).Trim(),
        ColorLiteHex = color,
        ColorMainHex = color,
        ColorHardHex = color,
        WebEndpoint = string.IsNullOrWhiteSpace(request.WebEndpoint) ? null : request.WebEndpoint.Trim(),
        FilesMediaEndpoint = string.IsNullOrWhiteSpace(request.FilesMediaEndpoint) ? null : request.FilesMediaEndpoint.Trim(),
        AddedBy = user.Identity?.Name ?? "admin"
    }, cancellationToken);

    return Results.NoContent();
}).RequireAuthorization(adminPolicy);

app.MapDelete("/admin/api/servers/{id}", async (long id, ServersStorage serversStorage, CancellationToken cancellationToken) =>
{
    var deleted = await serversStorage.DeleteManualServerAsync(id, cancellationToken);
    return deleted ? Results.NoContent() : Results.NotFound();
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

file sealed record ManualServerRequest(
    string? Name,
    string? ServerPublicName,
    string? Description,
    string? Location,
    string? Color,
    string? BeaconHost,
    int? BeaconPort,
    string? WebEndpoint,
    string? FilesMediaEndpoint);

// Правила те же, что у gRPC-регистрации (RegisterServerCommandHandler), но ошибки — 400 с текстом.
file static class ManualServerValidation
{
    private const int MaxNameLength = 64;
    private const int MaxPublicNameLength = 64;
    private const int MaxDescriptionLength = 512;
    private const int MaxLocationLength = 128;
    private const int MaxBeaconHostLength = 2048;
    private const int MaxPublicEndpointLength = 2048;

    public static string? Validate(ManualServerRequest request)
    {
        var name = request.Name?.Trim() ?? string.Empty;
        if (name.Length == 0)
            return "Имя сервера не может быть пустым";
        if (name.Length > MaxNameLength)
            return $"Имя сервера не должно превышать {MaxNameLength} символов";

        var publicName = request.ServerPublicName?.Trim() ?? string.Empty;
        if (publicName.Length == 0)
            return "Публичное имя сервера не может быть пустым";
        if (publicName.Length > MaxPublicNameLength)
            return $"Публичное имя не должно превышать {MaxPublicNameLength} символов";

        var description = request.Description?.Trim() ?? string.Empty;
        if (description.Length == 0)
            return "Описание сервера не может быть пустым";
        if (description.Length > MaxDescriptionLength)
            return $"Описание не должно превышать {MaxDescriptionLength} символов";

        if ((request.Location?.Trim() ?? string.Empty).Length > MaxLocationLength)
            return $"Локация не должна превышать {MaxLocationLength} символов";

        var beaconHost = request.BeaconHost?.Trim() ?? string.Empty;
        if (beaconHost.Length > 0)
        {
            if (beaconHost.Length > MaxBeaconHostLength || !RegisterServerCommandHandler.IsValidBeaconHost(beaconHost))
                return "Некорректный адрес Beacon";

            if (request.BeaconPort is not (> 0 and <= 65535))
                return "Порт Beacon должен быть в диапазоне от 1 до 65535";
        }

        if (!string.IsNullOrWhiteSpace(request.WebEndpoint)
            && (request.WebEndpoint.Trim().Length > MaxPublicEndpointLength
                || !RegisterServerCommandHandler.IsValidPublicEndpoint(request.WebEndpoint.Trim())))
            return "Некорректный адрес веб-клиента (нужен абсолютный http/https-URI)";

        if (!string.IsNullOrWhiteSpace(request.FilesMediaEndpoint)
            && (request.FilesMediaEndpoint.Trim().Length > MaxPublicEndpointLength
                || !RegisterServerCommandHandler.IsValidPublicEndpoint(request.FilesMediaEndpoint.Trim())))
            return "Некорректный файловый адрес (нужен абсолютный http/https-URI)";

        if (!RegisterServerCommandHandler.IsValidHexColor(request.Color?.Trim() ?? string.Empty))
            return "Цвет должен быть HEX-значением (например, #8c351c)";

        return null;
    }
}
