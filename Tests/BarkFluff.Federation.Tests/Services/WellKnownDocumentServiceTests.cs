using System.Text.Json;

using BarkFluff.Federation.Domain.Entities;
using BarkFluff.Federation.Services;
using BarkFluff.Federation.Tests.Infrastructure;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Org.Webpki.JsonCanonicalizer;

namespace BarkFluff.Federation.Tests.Services;

public class WellKnownDocumentServiceTests
{
    private static readonly Dictionary<string, string?> Configured = new()
    {
        ["Federation:ExternalEndpoint"] = "https://federation.node-a.test",
        ["Federation:TlsSpkiSha256"] = "spki-1, spki-2",
    };

    private static WellKnownDocumentService CreateService(ServiceProvider provider)
        => new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<IConfiguration>());

    [Fact]
    public void IsConfigured_RequiresServerNameAndEndpoint()
    {
        var db = TestHelpers.CreateDatabase();

        using var configured = TestHelpers.CreateProvider(db, TestHelpers.CreateConfiguration(Configured));
        CreateService(configured).IsConfigured.Should().BeTrue();

        using var noEndpoint = TestHelpers.CreateProvider(db);
        CreateService(noEndpoint).IsConfigured.Should().BeFalse();
    }

    [Fact]
    public async Task RebuildAsync_NotConfigured_ClearsCache()
    {
        using var provider = TestHelpers.CreateProvider(TestHelpers.CreateDatabase());
        var service = CreateService(provider);

        await service.RebuildAsync();

        service.GetCachedDocument().Should().BeNull();
    }

    [Fact]
    public async Task RebuildAsync_Configured_PublishesSignedDocument()
    {
        var db = TestHelpers.CreateDatabase();
        FederationSigningKey activeKey;
        await using (var seedContext = TestHelpers.CreateContext(db))
        {
            activeKey = await TestHelpers.EnsureActiveKeyAsync(seedContext);
        }

        using var provider = TestHelpers.CreateProvider(db, TestHelpers.CreateConfiguration(Configured));
        var service = CreateService(provider);

        await service.RebuildAsync();

        var json = service.GetCachedDocument();
        json.Should().NotBeNull();

        using var document = JsonDocument.Parse(json!);
        var root = document.RootElement;

        root.GetProperty("server_name").GetString().Should().Be(TestHelpers.OwnServerName);
        root.GetProperty("public_name").GetString().Should().BeEmpty();

        var federation = root.GetProperty("federation");
        federation.GetProperty("endpoint").GetString().Should().Be("https://federation.node-a.test");
        federation.GetProperty("tls_spki_sha256").EnumerateArray().Select(e => e.GetString()).Should().Equal("spki-1", "spki-2");
        federation.GetProperty("protocol_versions").EnumerateArray().Select(e => e.GetInt32()).Should().Equal(1);

        var signingKeys = root.GetProperty("signing_keys");
        signingKeys.TryGetProperty("ed25519:1", out var keyEntry).Should().BeTrue();
        Convert.FromBase64String(keyEntry.GetProperty("key").GetString()!).Should().Equal(activeKey.PublicKey);

        // Подпись должна сходиться независимой проверкой: JCS-канонизация документа без
        // поля signature + Ed25519-verify публичным ключом активного ключа.
        var signature = root.GetProperty("signature");
        signature.GetProperty("key_id").GetString().Should().Be(activeKey.KeyId);
        var signatureBytes = Convert.FromBase64String(signature.GetProperty("value").GetString()!);

        var withoutSignature = new Dictionary<string, JsonElement>();
        foreach (var property in root.EnumerateObject())
        {
            if (property.Name != "signature")
                withoutSignature[property.Name] = property.Value;
        }
        var canonicalBytes = new JsonCanonicalizer(JsonSerializer.Serialize(withoutSignature)).GetEncodedUTF8();

        SigningKeyService.Verify(activeKey.PublicKey, canonicalBytes, signatureBytes).Should().BeTrue();
    }

    [Fact]
    public async Task RebuildAsync_ExpiredKeyListedWithExpiredAt()
    {
        var db = TestHelpers.CreateDatabase();
        await using (var seedContext = TestHelpers.CreateContext(db))
        {
            var keyService = TestHelpers.CreateSigningKeyService(seedContext);
            await keyService.EnsureActiveKeyAsync();
            await keyService.RotateAsync(); // ed25519:1 получает ExpiredAt, активным становится ed25519:2
        }

        using var provider = TestHelpers.CreateProvider(db, TestHelpers.CreateConfiguration(Configured));
        var service = CreateService(provider);

        await service.RebuildAsync();

        using var document = JsonDocument.Parse(service.GetCachedDocument()!);
        var signingKeys = document.RootElement.GetProperty("signing_keys");

        signingKeys.TryGetProperty("ed25519:1", out var oldKey).Should().BeTrue();
        oldKey.GetProperty("expired_at").GetString().Should().NotBeNullOrEmpty();

        signingKeys.TryGetProperty("ed25519:2", out var newKey).Should().BeTrue();
        newKey.GetProperty("expired_at").ValueKind.Should().Be(JsonValueKind.Null);

        document.RootElement.GetProperty("signature").GetProperty("key_id").GetString().Should().Be("ed25519:2");
    }

    [Fact]
    public async Task RebuildAsync_EmptySpki_PublishesEmptyArray()
    {
        var db = TestHelpers.CreateDatabase();
        await using (var seedContext = TestHelpers.CreateContext(db))
        {
            await TestHelpers.EnsureActiveKeyAsync(seedContext);
        }

        using var provider = TestHelpers.CreateProvider(db, TestHelpers.CreateConfiguration(new Dictionary<string, string?>
        {
            ["Federation:ExternalEndpoint"] = "https://federation.node-a.test",
        }));
        var service = CreateService(provider);

        await service.RebuildAsync();

        using var document = JsonDocument.Parse(service.GetCachedDocument()!);
        document.RootElement.GetProperty("federation").GetProperty("tls_spki_sha256")
            .EnumerateArray().Should().BeEmpty();
    }
}
