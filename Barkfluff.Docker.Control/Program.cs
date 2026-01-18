using Barkfluff.Docker.Control.Data;
using Barkfluff.Docker.Control.Endpoints;
using Barkfluff.Docker.Control.Middleware;
using Barkfluff.Docker.Control.Services;

namespace Barkfluff.Docker.Control;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Configure Settings
        builder.Services.Configure<TelegramSettings>(builder.Configuration.GetSection("Telegram"));
        builder.Services.Configure<AuthSettings>(builder.Configuration.GetSection("Auth"));
        builder.Services.Configure<LiteDbSettings>(builder.Configuration.GetSection(LiteDbSettings.SectionName));

        // Register LiteDB DbContext as Singleton
        builder.Services.AddSingleton<TokenDbContext>();

        // Register Services
        builder.Services.AddSingleton<PendingAuthService>();
        builder.Services.AddSingleton<TokenService>();
        builder.Services.AddSingleton<AuthService>();

        // ������� ������������ TelegramBotService ��� Singleton
        builder.Services.AddSingleton<TelegramBotService>();

        // ����� ������������ ��� �� ��� Hosted Service
        builder.Services.AddHostedService(provider => provider.GetRequiredService<TelegramBotService>());

        var app = builder.Build();

        // Configure the pipeline
        app.UseHttpsRedirection();

        // Add Token Authentication Middleware
        app.UseTokenAuth();

        // Map Auth Endpoints
        app.MapAuthEndpoints();

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
            var token = context.Items["AuthToken"] as Barkfluff.Docker.Control.Models.AuthToken;
            if (token != null)
            {
                // Valid token - serve dashboard
                await ServeHtmlFile(context, "dashboard.html");
            }
            else
            {
                // No token - serve login
                await ServeHtmlFile(context, "Login.html");
            }
        });

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
