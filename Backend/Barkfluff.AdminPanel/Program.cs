using Barkfluff.AdminPanel.Data;
using Barkfluff.AdminPanel.Endpoints;
using Barkfluff.AdminPanel.Middleware;
using Barkfluff.AdminPanel.Models;
using Barkfluff.AdminPanel.Services;


using BarkFluff.GrpcServer;
using BarkFluff.Proto.Files;
using BarkFluff.Proto.Identity;
using BarkFluff.Proto.Users;
using BarkFluff.Shared.Auth;
using BarkFluff.Shared.Identity;

using MassTransit;

namespace Barkfluff.AdminPanel;

public class Program
{
    public static readonly DateTime StartedAtUtc = DateTime.UtcNow;

    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.WebHost.UseUrls("http://0.0.0.0:51888");

        // Загружаем конфигурацию из сервиса конфигурации
        builder.LoadConfiguration(ServiceId.Unknown);

        // Env-переменные контейнера должны иметь приоритет над Configuration service
        builder.Configuration.AddEnvironmentVariables();

        // Configure Settings
        builder.Services.Configure<TelegramSettings>(builder.Configuration.GetSection("Telegram"));
        builder.Services.Configure<AuthSettings>(builder.Configuration.GetSection("Auth"));
        builder.Services.Configure<LiteDbSettings>(builder.Configuration.GetSection(LiteDbSettings.SectionName));
        builder.Services.Configure<SeqSettings>(builder.Configuration.GetSection(SeqSettings.SectionName));

        // Register LiteDB DbContexts as Singletons
        builder.Services.AddSingleton<TokenDbContext>();

        var metricsCacheSettings = builder.Configuration.GetSection(MetricsCacheSettings.SectionName).Get<MetricsCacheSettings>() ?? new MetricsCacheSettings();
        builder.Services.AddSingleton(metricsCacheSettings);
        builder.Services.AddSingleton<MetricsCacheDbContext>();

        // Register Services
        builder.Services.AddSingleton<PendingAuthService>();
        builder.Services.AddSingleton<TokenService>();
        builder.Services.AddSingleton<AuthService>();

        // Register HttpClient for SeqService
        builder.Services.AddHttpClient<SeqService>();

        // Register LogsExportService as Singleton (in-memory job dictionary)
        builder.Services.AddSingleton<LogsExportService>();

        // Register LogsClearService as Singleton (in-memory job dictionary)
        builder.Services.AddSingleton<LogsClearService>();

        // Register S3BrowserService as Singleton
        builder.Services.AddSingleton<S3BrowserService>();

        // Register DockerService as Singleton
        builder.Services.AddSingleton<DockerService>();

        // Register RemoteDockerService as Singleton
        builder.Services.Configure<RemoteServersSettings>(builder.Configuration.GetSection(RemoteServersSettings.SectionName));
        builder.Services.AddSingleton<RemoteDockerService>();

        // Register MetricsCollectorService as background service
        builder.Services.AddHostedService<MetricsCollectorService>();

        // Register TelegramBotService as Singleton
        builder.Services.AddSingleton<TelegramBotService>();

        // Also register it as Hosted Service
        builder.Services.AddHostedService(provider => provider.GetRequiredService<TelegramBotService>());

        // Register gRPC clients for Users and Files services
        builder.Services.AddGrpcClient<UsersServerApi.UsersServerApiClient>(o =>
        {
            o.Address = new Uri(builder.Configuration["UsersService:Host"] ?? "http://users:7001");
        }).AddInterceptor(() => new JwtClientInterceptor(builder.Configuration["UsersService:Token"] ?? string.Empty));

        builder.Services.AddGrpcClient<FilesServerApi.FilesServerApiClient>(o =>
        {
            o.Address = new Uri(builder.Configuration["FilesService:Host"] ?? "http://files:7005");
        })
        .AddInterceptor(() => new JwtClientInterceptor(builder.Configuration["FilesService:Token"] ?? string.Empty))
        .ConfigureChannel(o =>
        {
            // Оригинальные файлы изображений могут быть больше дефолтных 4 МБ
            o.MaxSendMessageSize = 20 * 1024 * 1024;    // 20 МБ
            o.MaxReceiveMessageSize = 20 * 1024 * 1024;  // 20 МБ
        });

        builder.Services.AddGrpcClient<IdentityServerApi.IdentityServerApiClient>(o =>
        {
            o.Address = new Uri(builder.Configuration["IdentityService:Host"] ?? "http://identity:7000");
        }).AddInterceptor(() => new JwtClientInterceptor(builder.Configuration["IdentityService:Token"] ?? string.Empty));

        builder.Services.AddGrpcClient<BarkFluff.Proto.Configuration.ConfigurationApi.ConfigurationApiClient>(o =>
        {
            o.Address = new Uri(builder.Configuration["ConfigurationService:Host"] ?? "http://configuration:7010");
        }).AddInterceptor(() => new JwtClientInterceptor(builder.Configuration["ConfigurationService:Token"] ?? string.Empty));

        // MassTransit RabbitMQ — для публикации админских событий (push-рассылки и т.п.)
        builder.Services.AddMassTransit(x =>
        {
            x.UsingRabbitMq((ctx, cfg) =>
            {
                cfg.Host(builder.Configuration["RabbitMQ:Host"] ?? throw new InvalidOperationException("RabbitMQ:Host not configured"), "/", h =>
                {
                    h.Username(builder.Configuration["RabbitMQ:Username"] ?? throw new InvalidOperationException("RabbitMQ:Username not configured"));
                    h.Password(builder.Configuration["RabbitMQ:Password"] ?? throw new InvalidOperationException("RabbitMQ:Password not configured"));
                });
            });
        });

        // Configure and validate TelegramSettings
        builder.Services.AddOptions<TelegramSettings>()
            .PostConfigure(settings =>
            {
                // Parse admins from the string
                settings.ParsedAdmins = AdminUser.Parse(settings.Admins);

                // Validate that we have at least one admin
                if (settings.ParsedAdmins.Count == 0)
                {
                    throw new InvalidOperationException(
                        "No admins configured. Please set 'Telegram:Admins' in appsettings.json or via environment variable 'Telegram__Admins'. " +
                        "Format: 'userId1:username1,userId2:username2'");
                }

                // Check for duplicate usernames
                var duplicateUsernames = settings.ParsedAdmins
                    .GroupBy(a => a.Username, StringComparer.OrdinalIgnoreCase)
                    .Where(g => g.Count() > 1)
                    .Select(g => g.Key)
                    .ToList();

                if (duplicateUsernames.Count > 0)
                {
                    throw new InvalidOperationException(
                        $"Duplicate admin usernames found: {string.Join(", ", duplicateUsernames)}");
                }

                // Check for duplicate user IDs
                var duplicateUserIds = settings.ParsedAdmins
                    .GroupBy(a => a.TelegramUserId)
                    .Where(g => g.Count() > 1)
                    .Select(g => g.Key.ToString())
                    .ToList();

                if (duplicateUserIds.Count > 0)
                {
                    throw new InvalidOperationException(
                        $"Duplicate admin Telegram user IDs found: {string.Join(", ", duplicateUserIds)}");
                }
            });

        var app = builder.Build();

        // Configure the pipeline
        app.UseHttpsRedirection();

        // Serve favicon.ico before auth middleware (always accessible)
        app.MapGet("/favicon.ico", async context =>
        {
            var filePath = Path.Combine(AppContext.BaseDirectory, "Pages", "favicon.ico");
            if (File.Exists(filePath))
            {
                context.Response.ContentType = "image/x-icon";
                await context.Response.SendFileAsync(filePath);
            }
            else
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
            }
        });

        // Редирект /v2 → /v2/ (без trailing slash относительные пути styles.css/app.js
        // резолвятся как /styles.css вместо /v2/styles.css). Делаем через middleware,
        // т.к. MapGet("/v2") и MapGet("/v2/") дают AmbiguousMatchException — роутер
        // рассматривает их как одну каноническую точку.
        app.Use(async (ctx, next) =>
        {
            if (ctx.Request.Path.Value == "/v2")
            {
                ctx.Response.Redirect("/v2/");
                return;
            }
            await next();
        });

        // Add Token Authentication Middleware
        app.UseTokenAuth();

        // Map Auth Endpoints
        app.MapAuthEndpoints();

        // Map Seq Endpoints
        app.MapSeqEndpoints();

        // Map Logs Export Endpoints
        app.MapLogsExportEndpoints();

        // Map Logs Clear Endpoints
        app.MapLogsClearEndpoints();

        // Map Docker Endpoints
        app.MapDockerEndpoints();

        // Map Badges Endpoints
        app.MapBadgesEndpoints();

        // Map Stickers Endpoints
        app.MapStickersEndpoints();

        // Map Users Endpoints
        app.MapUsersEndpoints();

        // Map Configuration Endpoints
        app.MapConfigurationEndpoints();

        // Map Reserved Names Endpoints
        app.MapReservedNamesEndpoints();

        // Map S3 Browser Endpoints
        app.MapS3BrowserEndpoints();

        // Map Remote Docker Endpoints
        app.MapRemoteDockerEndpoints();

        // Map Notifications Endpoints (push-рассылки)
        app.MapNotificationsEndpoints();

        // Static files for Pages directory
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(
                Path.Combine(AppContext.BaseDirectory, "Pages")),
            RequestPath = ""
        });

        // Static files for redesigned UI under /v2
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(
                Path.Combine(AppContext.BaseDirectory, "Pages", "Redesigned")),
            RequestPath = "/v2"
        });

        // Root path routing based on auth and ui_version cookie
        app.MapGet("/", async context =>
        {
            var token = context.Items["AuthToken"] as Barkfluff.AdminPanel.Models.AuthToken;
            if (token != null)
            {
                if (context.Request.Cookies.TryGetValue("ui_version", out var ver) &&
                    string.Equals(ver, "v2", StringComparison.OrdinalIgnoreCase))
                {
                    context.Response.Redirect("/v2/");
                    return;
                }
                await ServeHtmlFile(context, "dashboard.html");
            }
            else
            {
                await ServeHtmlFile(context, "Login.html");
            }
        });

        // Page routes
        app.MapGet("/v2/", async context => await ServeHtmlFile(context, Path.Combine("Redesigned", "index.html")));
        app.MapGet("/services", async context => await ServeHtmlFile(context, "services.html"));
        app.MapGet("/logs", async context => await ServeHtmlFile(context, "logs.html"));
        app.MapGet("/badges", async context => await ServeHtmlFile(context, "badges.html"));
        app.MapGet("/stickers", async context => await ServeHtmlFile(context, "stickers.html"));
        app.MapGet("/users", async context => await ServeHtmlFile(context, "users.html"));
        app.MapGet("/s3-storage", async context => await ServeHtmlFile(context, "s3-storage.html"));
        app.MapGet("/s3-browser", async context => await ServeHtmlFile(context, "s3-browser.html"));
        app.MapGet("/restarting", async context => await ServeHtmlFile(context, "restarting.html"));
        app.MapGet("/updating", async context => await ServeHtmlFile(context, "updating.html"));

        app.Run();
    }

    // Helper function to serve HTML files
    private static async Task ServeHtmlFile(HttpContext context, string fileName)
    {
        var pagesPath = Path.Combine(AppContext.BaseDirectory, "Pages");
        var filePath = Path.Combine(pagesPath, fileName);

        if (!File.Exists(filePath))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            await context.Response.WriteAsync($"Page not found: {fileName}");
            return;
        }

        context.Response.ContentType = "text/html; charset=utf-8";
        var content = await File.ReadAllTextAsync(filePath);
        content = content.Replace("{{SERVER_STARTED_AT_UTC}}", StartedAtUtc.ToString("o"));
        await context.Response.WriteAsync(content);
    }
}

// Settings classes
public class TelegramSettings
{
    public const string SectionName = "Telegram";
    public string BotToken { get; set; } = string.Empty;
    public TelegramProxySettings Proxy { get; set; } = new();

    /// <summary>
    /// String containing admins in format "userId1:username1,userId2:username2"
    /// </summary>
    public string Admins { get; set; } = string.Empty;

    /// <summary>
    /// Parsed list of admins from the Admins string
    /// </summary>
    public List<AdminUser> ParsedAdmins { get; set; } = new();

    /// <summary>
    /// Legacy property for backward compatibility during migration
    /// </summary>
    [Obsolete("Use ParsedAdmins instead")]
    public List<long> AdminUserIds => ParsedAdmins.Select(a => a.TelegramUserId).ToList();
}

public class TelegramProxySettings
{
    public string Url { get; set; } = string.Empty;
    public string? Username { get; set; }
    public string? Password { get; set; }
}

public class AdminUser
{
    public long TelegramUserId { get; set; }
    public string Username { get; set; } = string.Empty;

    public AdminUser(long telegramUserId, string username)
    {
        TelegramUserId = telegramUserId;
        Username = username;
    }

    /// <summary>
    /// Parse a string in format "userId1:username1,userId2:username2"
    /// </summary>
    public static List<AdminUser> Parse(string adminsString)
    {
        var result = new List<AdminUser>();

        if (string.IsNullOrWhiteSpace(adminsString))
            return result;

        var parts = adminsString.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var part in parts)
        {
            var keyValue = part.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (keyValue.Length != 2)
                throw new InvalidOperationException($"Invalid admin format: '{part}'. Expected format: 'userId:username'");

            if (!long.TryParse(keyValue[0], out var userId))
                throw new InvalidOperationException($"Invalid Telegram user ID: '{keyValue[0]}' in '{part}'");

            result.Add(new AdminUser(userId, keyValue[1]));
        }

        return result;
    }
}

public static class TelegramSettingsExtensions
{
    /// <summary>
    /// Checks if the given Telegram user ID is an admin
    /// </summary>
    public static bool IsAdmin(this TelegramSettings settings, long telegramUserId)
    {
        return settings.ParsedAdmins.Any(a => a.TelegramUserId == telegramUserId);
    }

    /// <summary>
    /// Gets an admin by their Telegram user ID
    /// </summary>
    public static AdminUser? GetAdminByTelegramId(this TelegramSettings settings, long telegramUserId)
    {
        return settings.ParsedAdmins.FirstOrDefault(a => a.TelegramUserId == telegramUserId);
    }

    /// <summary>
    /// Gets an admin by their username
    /// </summary>
    public static AdminUser? GetAdminByUsername(this TelegramSettings settings, string username)
    {
        return settings.ParsedAdmins.FirstOrDefault(a => a.Username.Equals(username, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Gets the username for a Telegram user ID (if they are an admin)
    /// </summary>
    public static string? GetUsername(this TelegramSettings settings, long telegramUserId)
    {
        return settings.ParsedAdmins.FirstOrDefault(a => a.TelegramUserId == telegramUserId)?.Username;
    }
}

public class AuthSettings
{
    public const string SectionName = "Auth";
    public int TokenExpirationDays { get; set; } = 3;
    public int PendingRequestTimeoutMinutes { get; set; } = 10;
}

public class RemoteServerSettings
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 22;
    public string Username { get; set; } = "root";
    public string Password { get; set; } = string.Empty;
}

public class RemoteServersSettings
{
    public const string SectionName = "RemoteServers";
    public RemoteServerSettings Navigator { get; set; } = new();
    public RemoteServerSettings Msk { get; set; } = new();
}
