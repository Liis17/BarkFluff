using BarkFluff.GrpcServer;
using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Proto.Identity;
using BarkFluff.Shared.Identity;

using Grpc.Core;

using Microsoft.AspNetCore.Server.Kestrel.Core;

using Serilog;

using System.Text;

var builder = WebApplication.CreateBuilder(args);

builder.LoadConfiguration(ServiceId.Web);
builder.AddBarkFluffSerilog("BarkFluff.Web");

var port = int.Parse(builder.Configuration["RunSettings:Port"]!);
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(port, listenOptions =>
    {
        listenOptions.Protocols = HttpProtocols.Http1AndHttp2;
    });
});

builder.Services.AddBarkFluffMetrics("BarkFluff.Web");

builder.Services.AddGrpcClient<IdentityApi.IdentityApiClient>(o =>
{
    o.Address = new Uri(builder.Configuration["IdentityService:Host"] ?? "http://identity:7000");
});

var app = builder.Build();

// Middleware для логирования и метрик HTTP-запросов
app.Use(async (ctx, next) =>
{
    var metrics = ctx.RequestServices.GetRequiredService<MetricsCollector>();
    metrics.Increment("http_requests_total");

    var logger = ctx.RequestServices.GetRequiredService<ILogger<Program>>();
    var ip = ctx.Request.Headers["X-Forwarded-For"].FirstOrDefault()
             ?? ctx.Request.Headers["X-Real-IP"].FirstOrDefault()
             ?? ctx.Connection.RemoteIpAddress?.ToString()
             ?? "unknown";

    logger.LogDebug("HTTP {Method} {Path} от {IP}", ctx.Request.Method, ctx.Request.Path, ip);

    try
    {
        await next();
    }
    catch (Exception ex)
    {
        metrics.Increment("http_requests_errors");
        logger.LogError(ex, "Ошибка при обработке {Method} {Path}", ctx.Request.Method, ctx.Request.Path);
        throw;
    }
});

app.UseStaticFiles();

// POST /api/auth/login — проксирует авторизацию в Identity gRPC
app.MapPost("/api/auth/login", async (HttpContext httpCtx, IdentityApi.IdentityApiClient identityClient, MetricsCollector metrics) =>
{
    metrics.Increment("auth_login_attempts");

    var body = await httpCtx.Request.ReadFromJsonAsync<LoginRequest>();
    if (body == null || string.IsNullOrWhiteSpace(body.Login) || string.IsNullOrWhiteSpace(body.Password))
        return Results.BadRequest(new { error = "missing_fields" });

    var authRequest = new AuthRequest { Password = body.Password };

    if (body.Login.Contains('@'))
        authRequest.Email = body.Login;
    else
        authRequest.Username = body.Login;

    if (!string.IsNullOrEmpty(body.OtpCode))
        authRequest.OtpCode = body.OtpCode;

    var metadata = BuildMetadata(httpCtx, body.DeviceId);

    try
    {
        var response = await identityClient.AuthAsync(authRequest, new CallOptions(headers: metadata));

        metrics.Increment("auth_login_success");

        return Results.Ok(new TokenResponse
        {
            AccessToken = response.AccessToken.Value,
            AccessTokenExpiration = response.AccessToken.ExpirationDate.ToDateTimeOffset().ToUnixTimeMilliseconds(),
            RefreshToken = response.RefreshToken.Value,
            RefreshTokenExpiration = response.RefreshToken.ExpirationDate.ToDateTimeOffset().ToUnixTimeMilliseconds()
        });
    }
    catch (RpcException ex)
    {
        var errorCode = ex.Trailers.Get("x-error-code")?.Value;

        if (errorCode == "C1576884-12D8-4722-A7EE-9F9789AD1265")
        {
            metrics.Increment("auth_otp_required");
            return Results.Json(new { error = "otp_required" }, statusCode: 200);
        }

        metrics.Increment("auth_login_failed");

        return errorCode switch
        {
            "803B632C-4457-4B05-9435-9C3DD0F41E00" =>
                Results.Json(new { error = "invalid_otp" }, statusCode: 401),
            "21BFB9B5-C377-45D1-9B15-6B7F3432B397" =>
                Results.Json(new { error = "invalid_credentials" }, statusCode: 401),
            _ =>
                Results.Json(new { error = "server_error" }, statusCode: 500)
        };
    }
});

// POST /api/auth/refresh — обновление access токена по refresh токену
app.MapPost("/api/auth/refresh", async (HttpContext httpCtx, IdentityApi.IdentityApiClient identityClient, MetricsCollector metrics) =>
{
    metrics.Increment("auth_refresh_attempts");

    var body = await httpCtx.Request.ReadFromJsonAsync<RefreshRequest>();
    if (body == null || string.IsNullOrWhiteSpace(body.RefreshToken))
        return Results.BadRequest(new { error = "missing_refresh_token" });

    try
    {
        var response = await identityClient.CreateTokenAsync(
            new CreateTokenRequest { RefreshToken = body.RefreshToken });

        metrics.Increment("auth_refresh_success");

        return Results.Ok(new
        {
            accessToken = response.AccessToken.Value,
            expiration = response.AccessToken.ExpirationDate.ToDateTimeOffset().ToUnixTimeMilliseconds()
        });
    }
    catch (RpcException)
    {
        metrics.Increment("auth_refresh_failed");
        return Results.Json(new { error = "invalid_refresh_token" }, statusCode: 401);
    }
});

app.MapFallbackToFile("index.html");

app.Lifetime.ApplicationStopped.Register(Log.CloseAndFlush);
app.Run();

// --- Утилиты ---

static Metadata BuildMetadata(HttpContext ctx, string? deviceId)
{
    var ip = ctx.Request.Headers["X-Forwarded-For"].FirstOrDefault()
             ?? ctx.Request.Headers["X-Real-IP"].FirstOrDefault()
             ?? ctx.Connection.RemoteIpAddress?.ToString()
             ?? "unknown";

    return new Metadata
    {
        { "x-device-id", ToBase64(deviceId ?? Guid.NewGuid().ToString()) },
        { "x-device-name", ToBase64("Web Browser") },
        { "x-os", ToBase64("Web") },
        { "x-app-name", ToBase64("BarkFluff Web") },
        { "x-app-version", ToBase64("1.0.0") },
        { "x-ip-address", ToBase64(ip) },
    };
}

static string ToBase64(string value) =>
    Convert.ToBase64String(Encoding.UTF8.GetBytes(value));

// --- DTO ---

record LoginRequest(string? Login, string? Password, string? OtpCode, string? DeviceId);
record RefreshRequest(string? RefreshToken);

class TokenResponse
{
    public string AccessToken { get; init; } = "";
    public long AccessTokenExpiration { get; init; }
    public string RefreshToken { get; init; } = "";
    public long RefreshTokenExpiration { get; init; }
}
