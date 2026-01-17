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

        // Сначала регистрируем TelegramBotService как Singleton
        builder.Services.AddSingleton<TelegramBotService>();

        // Затем регистрируем его же как Hosted Service
        builder.Services.AddHostedService(provider => provider.GetRequiredService<TelegramBotService>());

        var app = builder.Build();

        // Configure the pipeline
        app.UseHttpsRedirection();

        // Add Token Authentication Middleware
        app.UseTokenAuth();

        // Map Auth Endpoints
        app.MapAuthEndpoints();

        // Serve static files (HTML pages)
        app.UseDefaultFiles(new DefaultFilesOptions
        {
            DefaultFileNames = new[] { "Login.html" },
            FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(
                Path.Combine(AppContext.BaseDirectory, "Pages"))
        });

        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(
                Path.Combine(AppContext.BaseDirectory, "Pages"))
        });

        app.Run();
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
