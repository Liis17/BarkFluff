using BarkFluff.Federation.Consumers;
using BarkFluff.Federation.Services;
using BarkFluff.Federation.Tests.Infrastructure;
using BarkFluff.Shared.Queue.Federation;

using MassTransit;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;

namespace BarkFluff.Federation.Tests.Consumers;

public class SigningKeyRotatedConsumerTests
{
    private static async Task<(ActiveSigningKeyCache Cache, WellKnownDocumentService WellKnown, SigningKeyRotatedConsumer Consumer)> CreateConsumerAsync(
        bool rotateOnOtherInstance)
    {
        var db = TestHelpers.CreateDatabase();
        await using (var seedContext = TestHelpers.CreateContext(db))
        {
            await TestHelpers.CreateSigningKeyService(seedContext).EnsureActiveKeyAsync();
        }

        var provider = TestHelpers.CreateProvider(db);
        var cache = new ActiveSigningKeyCache(provider.GetRequiredService<IServiceScopeFactory>());
        await cache.RefreshAsync(); // стартовый прогрев старым ключом

        if (rotateOnOtherInstance)
        {
            // «Другой инстанс» выполнил ротацию — этот инстанс своего кэша ещё не трогал.
            await using (var otherContext = TestHelpers.CreateContext(db))
            {
                await TestHelpers.CreateSigningKeyService(otherContext).RotateAsync();
            }
        }

        var wellKnown = new WellKnownDocumentService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>());

        var consumer = new SigningKeyRotatedConsumer(
            cache, wellKnown, new BarkFluff.GrpcServer.Metrics.MetricsCollector(), NullLogger<SigningKeyRotatedConsumer>.Instance);
        return (cache, wellKnown, consumer);
    }

    private static Mock<ConsumeContext<SigningKeyRotatedEvent>> CreateContext(string newKeyId)
    {
        var context = new Mock<ConsumeContext<SigningKeyRotatedEvent>>();
        context.Setup(c => c.Message).Returns(new SigningKeyRotatedEvent { NewKeyId = newKeyId });
        return context;
    }

    [Fact]
    public async Task Consume_AfterRotationOnOtherInstance_RefreshesActiveKey()
    {
        var (cache, _, consumer) = await CreateConsumerAsync(rotateOnOtherInstance: true);
        cache.Current!.KeyId.Should().Be("ed25519:1", "стартовый прогрев — старым ключом");

        await consumer.Consume(CreateContext("ed25519:2").Object);

        cache.Current!.KeyId.Should().Be("ed25519:2", "fan-out должен переключить инстанс на новый ключ");
    }

    [Fact]
    public async Task Consume_NoRotation_DoesNotChangeKey()
    {
        var (cache, _, consumer) = await CreateConsumerAsync(rotateOnOtherInstance: false);

        await consumer.Consume(CreateContext("ed25519:1").Object);

        cache.Current!.KeyId.Should().Be("ed25519:1");
    }
}
