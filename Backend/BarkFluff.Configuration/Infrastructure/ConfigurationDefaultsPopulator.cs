using BarkFluff.Configuration.Domain;
using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Shared.Identity;

using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace BarkFluff.Configuration.Infrastructure;

/// <summary>
/// Автоматическое заполнение пустых конфигураций значениями по умолчанию.
/// Запускается после миграций при старте Configuration-сервиса.
/// </summary>
public class ConfigurationDefaultsPopulator
{
    private readonly ConfigurationContext _context;
    private readonly ILogger<ConfigurationDefaultsPopulator> _logger;
    private readonly MetricsCollector? _metrics;
    private readonly string _postgresHost;
    private readonly string _postgresUsername;
    private readonly string _postgresPassword;
    private readonly string _rabbitUsername;
    private readonly string _rabbitPassword;

    /// <summary>
    /// Маппинг ServiceId → имя контейнера в Docker
    /// </summary>
    private static readonly Dictionary<ServiceId, string> ContainerNames = new()
    {
        { ServiceId.Identity, "identity" },
        { ServiceId.Users, "users" },
        { ServiceId.Beacon, "beacon" },
        { ServiceId.Notifications, "notification" },
        { ServiceId.Files, "files" },
        { ServiceId.Messages, "messages" },
        { ServiceId.FastAuth, "fast-auth" },
        { ServiceId.Updates, "updates" },
        { ServiceId.Onliner, "onliner" },
        { ServiceId.CloudMessaging, "cloud-messaging" },
        { ServiceId.Web, "web" },
        { ServiceId.Calls, "calls" },
    };

    /// <summary>
    /// Маппинг ServiceId → порт по умолчанию
    /// </summary>
    private static readonly Dictionary<ServiceId, int> DefaultPorts = new()
    {
        { ServiceId.Identity, 7000 },
        { ServiceId.Users, 7001 },
        { ServiceId.Beacon, 7002 },
        { ServiceId.Notifications, 7004 },
        { ServiceId.Files, 7005 },
        { ServiceId.Messages, 7007 },
        { ServiceId.FastAuth, 7008 },
        { ServiceId.Updates, 7015 },
        { ServiceId.Onliner, 7009 },
        { ServiceId.CloudMessaging, 7011 },
        { ServiceId.Web, 7016 },
        { ServiceId.Calls, 7025 },
    };

    /// <summary>
    /// Маппинг ServiceId → субдомен для внешнего доступа
    /// </summary>
    private static readonly Dictionary<ServiceId, string> SubdomainNames = new()
    {
        { ServiceId.Identity, "identity" },
        { ServiceId.Users, "users" },
        { ServiceId.Beacon, "beacon" },
        { ServiceId.Notifications, "notification" },
        { ServiceId.Files, "files" },
        { ServiceId.Messages, "messages" },
        { ServiceId.FastAuth, "fast-auth" },
        { ServiceId.Updates, "updates" },
        { ServiceId.Onliner, "onliner" },
        { ServiceId.Web, "web" },
        { ServiceId.Calls, "calls" },
    };

    /// <summary>
    /// Маппинг ServiceId → имя базы данных
    /// </summary>
    private static readonly Dictionary<ServiceId, (string Section, string DbName)> DatabaseNames = new()
    {
        { ServiceId.Identity, ("IdentityDb", "identity") },
        { ServiceId.Users, ("UsersDb", "users") },
        { ServiceId.Files, ("FilesDb", "files") },
        { ServiceId.Messages, ("MessagesDb", "messages") },
        { ServiceId.Onliner, ("OnlinerDb", "onliner") },
        { ServiceId.Calls, ("CallsDb", "calls") },
    };

    public ConfigurationDefaultsPopulator(
        ConfigurationContext context,
        ILogger<ConfigurationDefaultsPopulator> logger,
        string postgresHost,
        string postgresUsername,
        string postgresPassword,
        string rabbitUsername,
        string rabbitPassword,
        MetricsCollector? metrics = null)
    {
        _context = context;
        _logger = logger;
        _metrics = metrics;
        _postgresHost = postgresHost;
        _postgresUsername = postgresUsername;
        _postgresPassword = postgresPassword;
        _rabbitUsername = rabbitUsername;
        _rabbitPassword = rabbitPassword;
    }

    public async Task PopulateDefaultsAsync()
    {
        var emptyConfigs = await _context.Configurations
            .Where(c => c.Value == "" || c.Value == null)
            .ToListAsync();

        // Стартовый gauge — общее число записей
        var totalConfigs = await _context.Configurations.CountAsync();
        _metrics?.Set("configurations_total", totalConfigs);

        if (emptyConfigs.Count == 0)
        {
            _logger.LogInformation("Все конфигурации уже заполнены, авто-заполнение не требуется");
            _metrics?.Set("configurations_empty_at_startup", 0);
            return;
        }

        _logger.LogInformation("Найдено {Count} пустых конфигураций, запуск авто-заполнения", emptyConfigs.Count);
        _metrics?.Set("configurations_empty_at_startup", emptyConfigs.Count);

        // Сначала заполняем JWT SecretKey, т.к. он нужен для генерации сервисных токенов
        var jwtSecret = await GetOrGenerateJwtSecret(emptyConfigs);
        var jwtIssuer = await GetOrGenerateValue(emptyConfigs, "JwtSettings", "Issuer", "BarkFluff");
        var jwtAudience = await GetOrGenerateValue(emptyConfigs, "JwtSettings", "Audience", "BarkFluffMicroservices");

        var populatedCount = 0;
        foreach (var config in emptyConfigs)
        {
            var defaultValue = ResolveDefault(config, jwtSecret, jwtIssuer, jwtAudience);
            if (defaultValue != null)
            {
                config.Value = defaultValue;
                config.EditedAt = DateTime.UtcNow;
                config.EditedBy = "system";
                config.EditedFrom = "auto-populate";
                populatedCount++;
                _logger.LogDebug("Авто-заполнение: [{ServiceId}] {Section}:{Key} = {Value}",
                    config.ServiceId, config.Section, config.Key,
                    IsSensitive(config) ? "***" : defaultValue);
            }
        }

        await _context.SaveChangesAsync();
        _metrics?.Add("defaults_populated_total", populatedCount);
        _metrics?.Set("configurations_total", await _context.Configurations.CountAsync());
        _logger.LogInformation("Авто-заполнение завершено. Заполнено: {Count}", populatedCount);
    }

    private async Task<string> GetOrGenerateJwtSecret(List<ConfigurationItem> emptyConfigs)
    {
        var secretConfig = emptyConfigs.FirstOrDefault(
            c => c.Section == "JwtSettings" && c.Key == "SecretKey");

        if (secretConfig != null)
        {
            var secret = GenerateRandomKey(64);
            secretConfig.Value = secret;
            secretConfig.EditedAt = DateTime.UtcNow;
            secretConfig.EditedBy = "system";
            secretConfig.EditedFrom = "auto-populate";
            return secret;
        }

        // Если SecretKey уже заполнен, читаем его для генерации токенов
        var existing = await _context.Configurations
            .Where(c => c.Section == "JwtSettings" && c.Key == "SecretKey" && c.Value != "" && c.Value != null)
            .FirstOrDefaultAsync();

        return existing?.Value ?? GenerateRandomKey(64);
    }

    private async Task<string> GetOrGenerateValue(
        List<ConfigurationItem> emptyConfigs, string section, string key, string defaultValue)
    {
        var config = emptyConfigs.FirstOrDefault(c => c.Section == section && c.Key == key);
        if (config != null)
        {
            config.Value = defaultValue;
            config.EditedAt = DateTime.UtcNow;
            config.EditedBy = "system";
            config.EditedFrom = "auto-populate";
            return defaultValue;
        }

        var existing = await _context.Configurations
            .Where(c => c.Section == section && c.Key == key && c.Value != "" && c.Value != null)
            .FirstOrDefaultAsync();

        return existing?.Value ?? defaultValue;
    }

    private string? ResolveDefault(ConfigurationItem config, string jwtSecret, string jwtIssuer, string jwtAudience)
    {
        // Пропускаем уже заполненные (могли быть заполнены в GetOrGenerate)
        if (!string.IsNullOrEmpty(config.Value))
            return null;

        var serviceId = config.ServiceId;

        // --- RunSettings:Port ---
        if (config.Section == "RunSettings" && config.Key == "Port")
        {
            if (DefaultPorts.TryGetValue(serviceId, out var port))
                return port.ToString();
        }

        // --- RunSettings:Http1Port (Files, Calls) ---
        if (config.Section == "RunSettings" && config.Key == "Http1Port")
        {
            if (serviceId == ServiceId.Files)
                return "7006";
            if (serviceId == ServiceId.Calls)
                return "7026"; // HTTP/1.1-листенер для приёма LiveKit-webhooks
        }

        // --- JwtSettings ---
        if (config.Section == "JwtSettings")
        {
            return config.Key switch
            {
                "SecretKey" => null, // Уже обработано выше
                "Issuer" => null,    // Уже обработано выше
                "Audience" => null,  // Уже обработано выше
                "ExpiryMinutes" => "60",
                _ => null
            };
        }

        // --- RabbitMQ (внутренний Docker-адрес) ---
        if (config.Section == "RabbitMQ")
        {
            return config.Key switch
            {
                "Host" => "rabbitmq",
                "Username" => _rabbitUsername,
                "Password" => _rabbitPassword,
                "VirtualHost" => "/",
                _ => null
            };
        }

        // --- Redis (внутренний Docker-адрес) ---
        if (config.Section == "Redis" && config.Key == "")
        {
            return "redis:6379";
        }

        // --- Seq (агрегатор логов) ---
        if (config.Section == "Seq" && config.Key == "ServerUrl")
        {
            return "http://seq:5341";
        }

        // --- Database connection strings ---
        foreach (var (sId, (section, dbName)) in DatabaseNames)
        {
            if (config.Section == section && config.ServiceId == sId)
            {
                return $"Host={_postgresHost};Database={dbName};Username={_postgresUsername};Password={_postgresPassword}";
            }
        }

        // --- ExternalEndpoint:Host (внешние субдомены) ---
        if (config.Section == "ExternalEndpoint" && config.Key == "Host")
        {
            if (SubdomainNames.TryGetValue(serviceId, out var subdomain))
                return $"https://{subdomain}.example.com";
        }

        // --- NavigatorUrl (внутренний Docker-адрес для Beacon) ---
        if (config.Section == "NavigatorUrl" && config.Key == "")
        {
            return "http://navigator:7010";
        }

        // --- UsersService (inter-service) ---
        if (config.Section == "UsersService")
        {
            return config.Key switch
            {
                "Host" => $"http://users:{DefaultPorts[ServiceId.Users]}",
                "Token" => GenerateServiceToken(jwtSecret, jwtIssuer, jwtAudience, "UsersServiceClient"),
                _ => null
            };
        }

        // --- FilesService (inter-service) ---
        if (config.Section == "FilesService")
        {
            return config.Key switch
            {
                "Host" => $"http://files:{DefaultPorts[ServiceId.Files]}",
                "Token" => GenerateServiceToken(jwtSecret, jwtIssuer, jwtAudience, "FilesServiceClient"),
                _ => null
            };
        }

        // --- MessagesService (inter-service) ---
        if (config.Section == "MessagesService")
        {
            return config.Key switch
            {
                "Host" => $"http://messages:{DefaultPorts[ServiceId.Messages]}",
                "Token" => GenerateServiceToken(jwtSecret, jwtIssuer, jwtAudience, "MessagesServiceClient"),
                _ => null
            };
        }

        // --- IdentityService (inter-service) ---
        if (config.Section == "IdentityService")
        {
            return config.Key switch
            {
                "Host" => $"http://identity:{DefaultPorts[ServiceId.Identity]}",
                "Token" => GenerateServiceToken(jwtSecret, jwtIssuer, jwtAudience, "IdentityServiceClient"),
                _ => null
            };
        }

        // --- LiveKit (звонки) — креды должны совпадать с keys в конфиге LiveKit-сервера ---
        if (config.Section == "LiveKit")
        {
            return config.Key switch
            {
                "Url" => "ws://livekit:7880",
                "ApiKey" => "devkey",
                "ApiSecret" => "devsecret_change_me_in_production_0123456789",
                _ => null
            };
        }

        // --- TempFiles ---
        if (config.Section == "TempFiles" && config.Key == "ExpiresAt")
        {
            return "60"; // минуты
        }

        // --- S3 Buckets (внутренний Docker-адрес MinIO) ---
        if (config.Section.StartsWith("S3Buckets:"))
        {
            return config.Key switch
            {
                "ServiceUrl" => "http://minio:9000",
                "AccessKey" => "minioadmin",
                "SecretKey" => "minioadmin",
                _ => null
            };
        }

        return null;
    }

    /// <summary>
    /// Генерация JWT-токена для межсервисного взаимодействия (TokenType = Service)
    /// </summary>
    private static string GenerateServiceToken(string secretKey, string issuer, string audience, string serviceName)
    {
        var key = new SymmetricSecurityKey(Encoding.ASCII.GetBytes(secretKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(IdentityClaims.TokenType, nameof(TokenType.Service)),
            new Claim(IdentityClaims.UserId, "0"),
            new Claim("service-name", serviceName),
        };

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: DateTime.UtcNow.AddYears(10),
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>
    /// Генерация криптографически стойкого случайного ключа
    /// </summary>
    private static string GenerateRandomKey(int length)
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789!@#$%&*";
        var data = RandomNumberGenerator.GetBytes(length);
        var result = new StringBuilder(length);
        foreach (var b in data)
        {
            result.Append(chars[b % chars.Length]);
        }
        return result.ToString();
    }

    private static bool IsSensitive(ConfigurationItem config)
    {
        return config.Key is "SecretKey" or "Password" or "Token" or "ApiSecret"
               || config.Section.Contains("Password")
               || config.Section.Contains("Secret")
               || config.Section.Contains("Token");
    }
}
