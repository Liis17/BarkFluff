using Barkfluff.AdminPanel.Data;
using Barkfluff.AdminPanel.Endpoints;
using Barkfluff.AdminPanel.Middleware;
using Barkfluff.AdminPanel.Models;
using Barkfluff.AdminPanel.Services;
using Microsoft.Extensions.Options;

namespace Barkfluff.AdminPanel;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.WebHost.UseUrls("http://0.0.0.0:51888");

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

        // Register MetricsCollectorService as background service
        builder.Services.AddHostedService<MetricsCollectorService>();

        // Register TelegramBotService as Singleton
        builder.Services.AddSingleton<TelegramBotService>();

        // Also register it as Hosted Service
        builder.Services.AddHostedService(provider => provider.GetRequiredService<TelegramBotService>());

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

        // Add Token Authentication Middleware
        app.UseTokenAuth();

        // Map Auth Endpoints
        app.MapAuthEndpoints();

        // Map Seq Endpoints
        app.MapSeqEndpoints();

        // Static files for Pages directory
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(
                Path.Combine(AppContext.BaseDirectory, "Pages")),
            RequestPath = ""
        });

        // Root path routing based on auth
        app.MapGet("/", async context =>
        {
            var token = context.Items["AuthToken"] as Barkfluff.AdminPanel.Models.AuthToken;
            if (token != null)
            {
                await ServeHtmlFile(context, "dashboard.html");
            }
            else
            {
                await ServeHtmlFile(context, "Login.html");
            }
        });

        // Page routes
        app.MapGet("/services", async context => await ServeHtmlFile(context, "services.html"));
        app.MapGet("/logs", async context => await ServeHtmlFile(context, "logs.html"));

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
        await context.Response.WriteAsync(content);
    }
}

// Settings classes
public class TelegramSettings
{
    public const string SectionName = "Telegram";
    public string BotToken { get; set; } = string.Empty;

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
