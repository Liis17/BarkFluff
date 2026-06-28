using BarkFluff.Shared.Exceptions.Identity;
using BarkFluff.Shared.Exceptions.Users;
using BarkFluff.Users.Features.AddDraftUser;
using BarkFluff.Users.Features.ConfirmUser;
using BarkFluff.Users.Features.OverrideDraftUser;
using FluentAssertions;
using Microsoft.Extensions.Configuration;

namespace BarkFluff.Users.Tests.Features.UserLifecycle;

internal static class TestHelperExtensions
{
    public static BarkFluff.Users.Services.ReservedUsernamesService CreateReservedService(this TestHelper _, string configValue)
    {
        var config = new Mock<IConfiguration>();
        config.Setup(c => c["ReservedNames:Usernames"]).Returns(configValue);
        return new BarkFluff.Users.Services.ReservedUsernamesService(config.Object);
    }
}
