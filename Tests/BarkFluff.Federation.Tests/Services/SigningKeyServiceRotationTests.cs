using BarkFluff.Federation.Persistence.Contexts;
using BarkFluff.Federation.Services;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace BarkFluff.Federation.Tests.Services;

// P1-01 (docs/rearch/problem/phase-1-open-problems.md): ротация атомарна — после неё в БД ровно
// один ключ с ExpiredAt IS NULL AND RevokedAt IS NULL; идентификаторы уникальны и монотонны.
public class SigningKeyServiceRotationTests
{
    private static FederationContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<FederationContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new FederationContext(options);
    }

    private static SigningKeyService Create(FederationContext context, int overlapDays = 30)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Federation:ServerName"] = "node-a.test",
                ["Federation:KeyRotationOverlapDays"] = overlapDays.ToString(),
            })
            .Build();
        return new SigningKeyService(context, config, NullLogger<SigningKeyService>.Instance);
    }

    [Fact]
    public async Task RotateAsync_LeavesExactlyOneActiveKey_OldExpiredWithOverlap()
    {
        await using var context = CreateContext();
        var service = Create(context);
        await service.EnsureActiveKeyAsync();

        var before = DateTime.UtcNow;
        var (newKey, oldKey) = await service.RotateAsync();

        var active = await context.SigningKeys
            .Where(k => k.ExpiredAt == null && k.RevokedAt == null)
            .ToListAsync();

        active.Should().ContainSingle();
        active[0].KeyId.Should().Be(newKey.KeyId);

        oldKey.KeyId.Should().Be("ed25519:1");
        newKey.KeyId.Should().Be("ed25519:2");
        oldKey.ExpiredAt.Should().NotBeNull();
        oldKey.ExpiredAt!.Value.Should().BeCloseTo(before.AddDays(30), TimeSpan.FromMinutes(5));
    }

    [Fact]
    public async Task RotateAsync_Twice_MonotonicIdsSingleActive()
    {
        await using var context = CreateContext();
        var service = Create(context);
        await service.EnsureActiveKeyAsync();

        await service.RotateAsync();
        var (newKey, _) = await service.RotateAsync();

        newKey.KeyId.Should().Be("ed25519:3");

        var active = await context.SigningKeys
            .Where(k => k.ExpiredAt == null && k.RevokedAt == null)
            .ToListAsync();
        active.Should().ContainSingle().Which.KeyId.Should().Be("ed25519:3");

        var allIds = await context.SigningKeys.Select(k => k.KeyId).OrderBy(x => x).ToListAsync();
        allIds.Should().Equal("ed25519:1", "ed25519:2", "ed25519:3");
    }
}
