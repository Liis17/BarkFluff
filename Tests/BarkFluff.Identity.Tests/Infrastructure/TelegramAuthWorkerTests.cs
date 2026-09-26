using BarkFluff.GrpcServer;
using BarkFluff.GrpcServer.Tracker;
using BarkFluff.Identity.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BarkFluff.Identity.Tests.Infrastructure;

public sealed class TelegramAuthWorkerTests
{
    [Fact]
    public void Initializes_request_context_in_a_background_update_scope()
    {
        var services = new ServiceCollection();
        services.AddBarkFluffGrpc();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.Throws<InvalidOperationException>(
            () => scope.ServiceProvider.GetRequiredService<RequestContext>());

        TelegramAuthWorker.InitializeRequestContextForTelegramUpdate(scope.ServiceProvider);

        var context = scope.ServiceProvider.GetRequiredService<RequestContext>();
        Assert.Null(context.DeviceId);
        Assert.Null(context.TrustedIpAddress);
    }
}
