using Barkfluff.AdminPanel.Middleware;
using Barkfluff.AdminPanel.Models;
using Barkfluff.AdminPanel.Models.Dtos;
using Barkfluff.AdminPanel.Services;

using Microsoft.AspNetCore.Mvc;

namespace Barkfluff.AdminPanel.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/auth")
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

                var result = await authService.CreateAuthRequestAsync(dto);

                if (!result.Success)
                {
                    return Results.BadRequest(new
                    {
                        error = result.ErrorCode,
                        message = result.ErrorMessage
                    });
                }

                return Results.Ok(new { requestId = result.RequestId });
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

        group.MapGet("/me", (HttpContext context, AdminService adminService) =>
        {
            var token = context.GetAuthToken()!;

            var roles = token.ApprovedByTelegramUserId.HasValue
                ? adminService.GetRoles(token.ApprovedByTelegramUserId.Value)
                : new HashSet<AdminRole>();

            return Results.Ok(new
            {
                id = token.Id,
                name = token.Name,
                adminUsername = token.AdminUsername,
                hasTelegramAvatar = token.ApprovedByTelegramUserId.HasValue,
                roles = roles.Select(AdminRoles.DisplayName).ToList(),
                createdAt = token.CreatedAt,
                lastActivity = token.LastActivity
            });
        })
        .WithName("GetCurrentToken")
        .WithOpenApi();

        group.MapGet("/me/avatar", async (
            HttpContext context,
            TelegramBotService telegramBotService,
            CancellationToken cancellationToken) =>
        {
            if (context.Items["AuthToken"] is not AuthToken token || !token.ApprovedByTelegramUserId.HasValue)
                return Results.NotFound();

            var photo = await telegramBotService.GetProfilePhotoAsync(token.ApprovedByTelegramUserId.Value, cancellationToken);
            if (photo == null)
                return Results.NotFound();

            return Results.File(photo, "image/jpeg");
        })
        .WithName("GetCurrentAdminTelegramAvatar")
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
            var currentToken = context.GetAuthToken()!;

            // Only show tokens that belong to the current admin (or all tokens if current token has no admin association for backward compatibility)
            List<AuthToken> tokens;
            if (currentToken.ApprovedByTelegramUserId.HasValue)
            {
                tokens = tokenService.GetTokensByAdmin(currentToken.ApprovedByTelegramUserId.Value);
            }
            else
            {
                // Legacy: show all tokens for tokens created before multi-admin support
                tokens = tokenService.GetAllTokens().Where(t => t.ApprovedByTelegramUserId.HasValue).ToList();
            }

            return Results.Ok(tokens.Select(t => new
            {
                id = t.Id,
                name = t.Name,
                createdAt = t.CreatedAt,
                lastActivity = t.LastActivity,
                ipAddress = t.IpAddress,
                adminUsername = t.AdminUsername,
                isCurrent = t.Id == currentToken.Id
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
            var currentToken = context.GetAuthToken()!;

            if (string.IsNullOrWhiteSpace(dto.Name))
            {
                return Results.BadRequest(new { error = "Name cannot be empty" });
            }

            // Check if the user can rename this token
            bool success;
            if (currentToken.ApprovedByTelegramUserId.HasValue)
            {
                success = tokenService.RenameTokenByAdmin(id, dto.Name, currentToken.ApprovedByTelegramUserId.Value);
            }
            else
            {
                // Legacy: allow renaming any token for tokens created before multi-admin support
                var token = tokenService.GetToken(id);
                if (token == null)
                {
                    return Results.NotFound(new { error = "Token not found" });
                }
                success = tokenService.RenameToken(id, dto.Name);
            }

            if (!success)
            {
                return Results.NotFound(new { error = "Token not found or you don't have permission" });
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
            var currentToken = context.GetAuthToken()!;

            if (id == currentToken.Id)
            {
                return Results.BadRequest(new { error = "Cannot delete your own token through this endpoint. Use logout instead." });
            }

            // Check if the user can delete this token
            bool success;
            if (currentToken.ApprovedByTelegramUserId.HasValue)
            {
                success = tokenService.DeleteTokenByAdmin(id, currentToken.ApprovedByTelegramUserId.Value);
            }
            else
            {
                // Legacy: allow deleting any token for tokens created before multi-admin support
                var token = tokenService.GetToken(id);
                if (token == null)
                {
                    return Results.NotFound(new { error = "Token not found" });
                }
                success = tokenService.DeleteToken(id);
            }

            if (!success)
            {
                return Results.NotFound(new { error = "Token not found or you don't have permission" });
            }

            return Results.Ok(new { message = "Token deleted successfully" });
        })
        .WithName("DeleteToken")
        .WithOpenApi();
    }

    private static string? GetIpAddress(HttpContext context)
    {
        return context.Connection.RemoteIpAddress?.ToString();
    }
}

public class RenameRequestDto
{
    public string Name { get; set; } = string.Empty;
}
