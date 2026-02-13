using Barkfluff.AdminPanel.Data;
using Barkfluff.AdminPanel.Endpoints;
using Barkfluff.AdminPanel.Middleware;
using Barkfluff.AdminPanel.Models;
using Barkfluff.AdminPanel.Services;

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

        // Register LiteDB DbContext as Singleton
        builder.Services.AddSingleton<TokenDbContext>();

        // Register Services
        builder.Services.AddSingleton<PendingAuthService>();
        builder.Services.AddSingleton<TokenService>();
        builder.Services.AddSingleton<AuthService>();

        // Register HttpClient for SeqService
        builder.Services.AddHttpClient<SeqService>();

        // Register TelegramBotService as Singleton
        builder.Services.AddSingleton<TelegramBotService>();

        // Also register it as Hosted Service
        builder.Services.AddHostedService(provider => provider.GetRequiredService<TelegramBotService>());

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
    public List<long> AdminUserIds { get; set; } = new();
}

public class AuthSettings
{
    public const string SectionName = "Auth";
    public int TokenExpirationDays { get; set; } = 3;
    public int PendingRequestTimeoutMinutes { get; set; } = 10;
}
