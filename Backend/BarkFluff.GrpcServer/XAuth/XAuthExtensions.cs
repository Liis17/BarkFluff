using BarkFluff.Shared.Identity;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

using System.Text;

namespace BarkFluff.GrpcServer.XAuth;

public static class XAuthExtensions
{
    public static IServiceCollection AddXAuth(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<TokenRevocationCache>();
        services.AddHostedService<TokenRevocationCleanupService>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(cfg =>
            {
                cfg.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(configuration["JwtSettings:SecretKey"]!)),
                    ValidateIssuer = true,
                    ValidIssuer = configuration["JwtSettings:Issuer"],
                    ValidateAudience = true,
                    ValidAudience = configuration["JwtSettings:Audience"],
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero
                };

                cfg.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        if (context.Request.Headers.TryGetValue("x-auth-token", out var token))
                        {
                            context.Token = token;
                        }

                        return Task.CompletedTask;
                    },
                    OnTokenValidated = context =>
                    {
                        var principal = context.Principal;
                        var tokenType = principal?.FindFirst(IdentityClaims.TokenType)?.Value;

                        if (tokenType == TokenType.User.ToString())
                        {
                            var userIdStr = principal?.FindFirst(IdentityClaims.UserId)?.Value;
                            var deviceId = principal?.FindFirst(IdentityClaims.DeviceId)?.Value;

                            if (long.TryParse(userIdStr, out var userId)
                                && !string.IsNullOrEmpty(deviceId))
                            {
                                var cache = context.HttpContext.RequestServices
                                    .GetRequiredService<TokenRevocationCache>();

                                if (cache.IsRevoked(userId, deviceId))
                                {
                                    context.Fail("Session has been revoked");
                                }
                            }
                        }

                        return Task.CompletedTask;
                    }
                };
            });

        services.AddAuthorization(options =>
        {
            options.AddPolicy(nameof(TokenType.Service),
                p => p.RequireClaim(IdentityClaims.TokenType, "Service"));

            options.AddPolicy(nameof(TokenType.User),
                p => p.RequireClaim(IdentityClaims.TokenType, "User", "Service"));
        });

        services.AddHttpContextAccessor();
        services.AddScoped<UserContext>();

        return services;
    }

    public static IApplicationBuilder UseXAuth(this IApplicationBuilder app)
    {
        app.UseAuthentication();
        app.UseAuthorization();
        return app;
    }
}