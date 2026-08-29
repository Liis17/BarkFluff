using BarkFluff.Shared.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace Barkfluff.Developers.Infrastructure;

internal static class DevelopersAuthorizationExtensions
{
    public const string ReaderPolicyName = "DevelopersReader";

    public static IServiceCollection AddDevelopersAuthorization(this IServiceCollection services)
    {
        services.AddAuthorization(options =>
        {
            options.AddPolicy(ReaderPolicyName, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.RequireClaim(IdentityClaims.TokenType, TokenType.User.ToString());
            });
        });

        return services;
    }
}
