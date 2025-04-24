using System.Text;
using BarkFluff.Shared.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace BarkFluff.GrpcServer.XAuth;

public static class XAuthExtensions
{
    public static IServiceCollection AddXAuth(this IServiceCollection services, 
        Action<XAuthOptions> configureOptions)
    {
        var options = new XAuthOptions();
        configureOptions(options);
        
        services.AddSingleton(options);
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(cfg =>
            {
                cfg.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.ASCII.GetBytes(options.Secret)),
                    ValidateIssuer = true,
                    ValidIssuer = options.Issuer,
                    ValidateAudience = true,
                    ValidAudience = options.Audience,
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