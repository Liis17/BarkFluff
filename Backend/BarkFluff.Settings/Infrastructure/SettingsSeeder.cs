using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Settings.Catalog;
using BarkFluff.Settings.Domain;
using BarkFluff.Settings.Persistence.Contexts;
using BarkFluff.Settings.Settings;
using BarkFluff.Shared.Identity;

using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace BarkFluff.Settings.Infrastructure;

public sealed record SettingsSeedOptions(
    string PostgresHost,
    string PostgresUsername,
    string PostgresPassword,
    string RabbitUsername,
    string RabbitPassword)
{
    public static SettingsSeedOptions FromConfiguration(
        SettingsDatabaseOptions database,
        IConfiguration configuration) => new(
            database.Host,
            database.Username,
            database.Password,
            configuration["RABBITMQ_DEFAULT_USER"] ?? "guest",
            configuration["RABBITMQ_DEFAULT_PASS"] ?? "guest");

    public static SettingsSeedOptions ForTests() => new("postgres", "postgres", "postgres", "guest", "guest");
}

public sealed class SettingsSeeder
{
    private static readonly string[] BuiltInReservedNames =
    [
        "admin", "support", "help", "system", "moderator", "mod", "root",
        "superuser", "administrator", "official", "barkfluff", "bark", "fluff"
    ];

    private readonly SettingsContext _context;
    private readonly SettingsSeedOptions _options;
    private readonly MetricsCollector? _metrics;

    public SettingsSeeder(SettingsContext context, SettingsSeedOptions options, MetricsCollector? metrics = null)
    {
        _context = context;
        _options = options;
        _metrics = metrics;
    }

    public async Task<int> SeedAsync(CancellationToken cancellationToken = default)
    {
        var existingByService = new Dictionary<ServiceId, HashSet<string>>();
        foreach (var scope in SettingsScopes.All)
        {
            existingByService[scope.ServiceId] = (await _context.Settings(scope)
                    .Select(row => row.Key)
                    .ToListAsync(cancellationToken))
                .ToHashSet(StringComparer.Ordinal);
        }

        var globalRows = await _context.Settings(SettingsScopes.Get(ServiceId.Unknown))
            .ToDictionaryAsync(row => row.Key, row => row.Value, StringComparer.Ordinal, cancellationToken);
        var jwtSecret = ExistingOr("JwtSettings:SecretKey", globalRows, () => GenerateRandomKey(64));
        var jwtIssuer = ExistingOr("JwtSettings:Issuer", globalRows, () => "BarkFluff");
        var jwtAudience = ExistingOr("JwtSettings:Audience", globalRows, () => "BarkFluffMicroservices");
        var values = new SettingsSeedValues(
            _options.PostgresHost,
            _options.PostgresUsername,
            _options.PostgresPassword,
            _options.RabbitUsername,
            _options.RabbitPassword,
            jwtSecret,
            jwtIssuer,
            jwtAudience,
            serviceName => GenerateServiceToken(jwtSecret, jwtIssuer, jwtAudience, serviceName));

        var now = DateTime.UtcNow;
        var inserted = 0;
        foreach (var entry in SettingsCatalog.All)
        {
            if (!existingByService[entry.ServiceId].Add(entry.StorageKey))
                continue;

            _context.Settings(SettingsScopes.Get(entry.ServiceId)).Add(new SettingRow
            {
                Key = entry.StorageKey,
                Value = entry.DefaultFactory(values),
                EditedBy = "system",
                EditedAt = now
            });
            inserted++;
        }

        var existingReserved = (await _context.ReservedNames.Select(item => item.Name).ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);
        foreach (var name in BuiltInReservedNames.Where(existingReserved.Add))
            _context.ReservedNames.Add(new ReservedName { Name = name });

        await _context.SaveChangesAsync(cancellationToken);
        _metrics?.Add("defaults_populated_total", inserted);
        _metrics?.Set("settings_rows_total", SettingsCatalog.All.Count);
        return inserted;
    }

    public async Task ValidateAsync(CancellationToken cancellationToken = default)
    {
        foreach (var scope in SettingsScopes.All)
        {
            var actual = await _context.Settings(scope).Select(row => row.Key).ToListAsync(cancellationToken);
            var unknown = actual.Where(key => !TryResolve(scope.ServiceId, key)).Order(StringComparer.Ordinal).ToArray();
            if (unknown.Length > 0)
                throw new InvalidOperationException($"Unknown keys found in {scope.TableName}: {string.Join(", ", unknown)}");

            var expected = SettingsCatalog.All.Where(entry => entry.ServiceId == scope.ServiceId).Select(entry => entry.StorageKey);
            var missing = expected.Except(actual, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            if (missing.Length > 0)
                throw new InvalidOperationException($"Catalog keys are missing in {scope.TableName}: {string.Join(", ", missing)}");
        }
    }

    private static bool TryResolve(ServiceId serviceId, string key)
    {
        try { SettingsCatalog.Resolve(serviceId, key); return true; }
        catch (UnknownSettingException) { return false; }
    }

    private static string ExistingOr(string key, IReadOnlyDictionary<string, string> rows, Func<string> factory) =>
        rows.TryGetValue(key, out var value) && !string.IsNullOrEmpty(value) ? value : factory();

    private static string GenerateServiceToken(string secretKey, string issuer, string audience, string serviceName)
    {
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey)),
            SecurityAlgorithms.HmacSha256);
        var claims = new[]
        {
            new Claim(IdentityClaims.TokenType, nameof(TokenType.Service)),
            new Claim(IdentityClaims.UserId, "0"),
            new Claim("service-name", serviceName)
        };
        return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            issuer, audience, claims, expires: DateTime.UtcNow.AddYears(10), signingCredentials: credentials));
    }

    private static string GenerateRandomKey(int length)
    {
        const string characters = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789!@#$%&*";
        return new string(RandomNumberGenerator.GetBytes(length).Select(value => characters[value % characters.Length]).ToArray());
    }
}
