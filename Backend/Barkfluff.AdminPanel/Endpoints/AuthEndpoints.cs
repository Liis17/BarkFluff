using Barkfluff.AdminPanel.Models;
using Barkfluff.AdminPanel.Models.Dtos;
using Barkfluff.AdminPanel.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Barkfluff.AdminPanel.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/auth")
            .WithName("Auth")
            .WithTags("Auth");

        group.MapPost("/request", async (
            [FromBody] AuthRequestDto dto,
            HttpContext context,
            AuthService authService,
            CancellationToken ct) =>
        {
            try
            {
                dto.IpAddress = GetIpAddress(context);

                var requestId = await authService.CreateAuthRequestAsync(dto);
                return Results.Ok(new { requestId });
            }
            catch (Exception ex)
            {
                return Results.Problem(
                    title: "Failed to create auth request",
                    detail: ex.Message,
                    statusCode: 500);
            }
        })
        .WithName("CreateAuthRequest")
        .WithOpenApi();

        group.MapGet("/status/{requestId}", (string requestId, AuthService authService) =>
        {
            var status = authService.GetStatus(requestId);
            return Results.Ok(status);
        })
        .WithName("GetAuthStatus")
        .WithOpenApi();

        group.MapGet("/me", (HttpContext context) =>
        {
            if (context.Items["AuthToken"] is not AuthToken token)
            {
                return Results.Unauthorized();
            }

            return Results.Ok(new
            {
                id = token.Id,
                name = token.Name,
                createdAt = token.CreatedAt,
                lastActivity = token.LastActivity
            });
        })
        .WithName("GetCurrentToken")
        .WithOpenApi();

        group.MapPost("/logout", (HttpContext context) =>
        {
            context.Response.Cookies.Delete("auth_token");
            return Results.Ok(new { message = "Logged out successfully" });
        })
        .WithName("Logout")
        .WithOpenApi();

        group.MapGet("/tokens", (TokenService tokenService, HttpContext context) =>
        {
            if (context.Items["AuthToken"] is not AuthToken token)
            {
                return Results.Unauthorized();
            }

            var tokens = tokenService.GetAllTokens();
            return Results.Ok(tokens.Select(t => new
            {
                id = t.Id,
                name = t.Name,
                createdAt = t.CreatedAt,
                lastActivity = t.LastActivity,
                ipAddress = t.IpAddress,
                isCurrent = t.Id == token.Id
            }));
        })
        .WithName("ListTokens")
        .WithOpenApi();

        group.MapPost("/tokens/{id:guid}/rename", async (
            Guid id,
            [FromBody] RenameRequestDto dto,
            TokenService tokenService,
            HttpContext context) =>
        {
            if (context.Items["AuthToken"] is not AuthToken)
            {
                return Results.Unauthorized();
            }

            if (string.IsNullOrWhiteSpace(dto.Name))
            {
                return Results.BadRequest(new { error = "Name cannot be empty" });
            }

            var success = tokenService.RenameToken(id, dto.Name);
            if (!success)
            {
                return Results.NotFound(new { error = "Token not found" });
            }

            return Results.Ok(new { message = "Token renamed successfully" });
        })
        .WithName("RenameToken")
        .WithOpenApi();

        group.MapDelete("/tokens/{id:guid}", async (
            Guid id,
            TokenService tokenService,
            HttpContext context) =>
        {
            if (context.Items["AuthToken"] is AuthToken currentToken)
            {
                if (id == currentToken.Id)
                {
                    return Results.BadRequest(new { error = "Cannot delete your own token through this endpoint. Use logout instead." });
                }
            }
            else
            {
                return Results.Unauthorized();
            }

            var success = tokenService.DeleteToken(id);
            if (!success)
            {
                return Results.NotFound(new { error = "Token not found" });
            }

            return Results.Ok(new { message = "Token deleted successfully" });
        })
        .WithName("DeleteToken")
        .WithOpenApi();
    }

    private static string? GetIpAddress(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue("X-Forwarded-For", out var forwardedFor))
        {
            return forwardedFor.FirstOrDefault()?.Split(',').FirstOrDefault()?.Trim();
        }

        if (context.Request.Headers.TryGetValue("X-Real-IP", out var realIp))
        {
            return realIp.FirstOrDefault();
        }

        return context.Connection.RemoteIpAddress?.ToString();
    }
}

public class RenameRequestDto
{
    public string Name { get; set; } = string.Empty;
}
