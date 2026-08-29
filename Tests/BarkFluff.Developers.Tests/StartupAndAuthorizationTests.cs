using System.Security.Claims;
using BarkFluff.Shared.Identity;
using Barkfluff.Developers.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace Barkfluff.Developers.Tests;

public class StartupAndAuthorizationTests
{
    [Fact]
    public async Task Startup_fails_when_a_published_physical_proto_is_missing()
    {
        await using var services = TestInfrastructure.CreateInitializerProvider(
            Guid.NewGuid().ToString(),
            new TestInfrastructure.TestPublishedProtoCatalog(["shared.proto"]));
        var initializer = services.GetRequiredService<DevelopersStartupInitializer>();

        var action = () => initializer.InitializeAsync();

        var exception = await action.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Contain("shared.proto");
    }

    [Fact]
    public async Task User_jwt_is_accepted_but_service_and_anonymous_principals_are_rejected()
    {
        using var services = new ServiceCollection()
            .AddDevelopersAuthorization()
            .AddLogging()
            .BuildServiceProvider();
        var authorization = services.GetRequiredService<IAuthorizationService>();

        var user = Principal(TokenType.User);
        var service = Principal(TokenType.Service);
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity());

        (await authorization.AuthorizeAsync(user, null, DevelopersAuthorizationExtensions.ReaderPolicyName))
            .Succeeded.Should().BeTrue();
        (await authorization.AuthorizeAsync(service, null, DevelopersAuthorizationExtensions.ReaderPolicyName))
            .Succeeded.Should().BeFalse();
        (await authorization.AuthorizeAsync(anonymous, null, DevelopersAuthorizationExtensions.ReaderPolicyName))
            .Succeeded.Should().BeFalse();
    }

    private static ClaimsPrincipal Principal(TokenType tokenType)
    {
        return new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(IdentityClaims.TokenType, tokenType.ToString())],
            authenticationType: "test"));
    }
}
