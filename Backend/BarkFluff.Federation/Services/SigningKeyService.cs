using BarkFluff.Federation.Domain.Entities;
using BarkFluff.Federation.Persistence.Contexts;

using Microsoft.EntityFrameworkCore;

using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Security;

namespace BarkFluff.Federation.Services;

// Ed25519 (BouncyCastle.Cryptography, см. docs/rearch/phase-0/step-0.5-report.md).
public class SigningKeyService
{
    private readonly FederationContext _context;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SigningKeyService> _logger;

    public SigningKeyService(FederationContext context, IConfiguration configuration, ILogger<SigningKeyService> logger)
    {
        _context = context;
        _configuration = configuration;
        _logger = logger;
    }

    // Идемпотентно: рестарт сервиса не плодит новые ключи.
    public async Task EnsureActiveKeyAsync(CancellationToken ct = default)
    {
        var hasActive = await _context.SigningKeys.AnyAsync(k => k.ExpiredAt == null && k.RevokedAt == null, ct);
        if (hasActive)
            return;

        if (string.IsNullOrWhiteSpace(_configuration["Federation:ServerName"]))
            _logger.LogWarning("Federation:ServerName не задан — signing-ключ сгенерирован, но федерация не сконфигурирована");

        var nextN = await GetNextKeyNumberAsync(ct);
        await GenerateAndStoreKeyAsync($"ed25519:{nextN}", ct);
    }

    public async Task<FederationSigningKey> GetActiveKeyAsync(CancellationToken ct = default)
    {
        var key = await _context.SigningKeys
            .Where(k => k.ExpiredAt == null && k.RevokedAt == null)
            .OrderByDescending(k => k.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (key == null)
            throw new InvalidOperationException("Нет активного signing-ключа Federation");

        return key;
    }

    public Task<List<FederationSigningKey>> GetNonRevokedKeysAsync(CancellationToken ct = default)
        => _context.SigningKeys.Where(k => k.RevokedAt == null).ToListAsync(ct);

    public byte[] Sign(FederationSigningKey key, byte[] data)
    {
        var privateKey = new Ed25519PrivateKeyParameters(key.PrivateKeySeed, 0);

        ISigner signer = new Ed25519Signer();
        signer.Init(true, privateKey);
        signer.BlockUpdate(data, 0, data.Length);
        return signer.GenerateSignature();
    }

    public static bool Verify(byte[] publicKey, byte[] data, byte[] signature)
    {
        var pub = new Ed25519PublicKeyParameters(publicKey, 0);

        ISigner signer = new Ed25519Signer();
        signer.Init(false, pub);
        signer.BlockUpdate(data, 0, data.Length);
        return signer.VerifySignature(signature);
    }

    // Плановая ротация: новый ключ становится активным, старому проставляется ExpiredAt = now + overlap.
    public async Task<(FederationSigningKey NewKey, FederationSigningKey OldKey)> RotateAsync(CancellationToken ct = default)
    {
        var oldKey = await GetActiveKeyAsync(ct);

        var overlapDays = 30;
        var overlapConfig = _configuration["Federation:KeyRotationOverlapDays"];
        if (!string.IsNullOrWhiteSpace(overlapConfig) && int.TryParse(overlapConfig, out var parsedOverlap))
            overlapDays = parsedOverlap;

        var nextN = await GetNextKeyNumberAsync(ct);
        var newKey = await GenerateAndStoreKeyAsync($"ed25519:{nextN}", ct);

        oldKey.ExpiredAt = DateTime.UtcNow.AddDays(overlapDays);
        await _context.SaveChangesAsync(ct);

        return (newKey, oldKey);
    }

    private async Task<int> GetNextKeyNumberAsync(CancellationToken ct)
    {
        var keyIds = await _context.SigningKeys.Select(k => k.KeyId).ToListAsync(ct);

        var maxN = 0;
        foreach (var keyId in keyIds)
        {
            var parts = keyId.Split(':');
            if (parts.Length == 2 && int.TryParse(parts[1], out var n) && n > maxN)
                maxN = n;
        }

        return maxN + 1;
    }

    private async Task<FederationSigningKey> GenerateAndStoreKeyAsync(string keyId, CancellationToken ct)
    {
        var random = new SecureRandom();
        var kpGen = new Ed25519KeyPairGenerator();
        kpGen.Init(new KeyGenerationParameters(random, 256));
        var kp = kpGen.GenerateKeyPair();
        var privateKey = (Ed25519PrivateKeyParameters)kp.Private;
        var publicKey = (Ed25519PublicKeyParameters)kp.Public;

        var entity = new FederationSigningKey
        {
            KeyId = keyId,
            PublicKey = publicKey.GetEncoded(),
            PrivateKeySeed = privateKey.GetEncoded(),
            CreatedAt = DateTime.UtcNow,
        };

        _context.SigningKeys.Add(entity);
        await _context.SaveChangesAsync(ct);

        return entity;
    }
}
