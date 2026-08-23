using Barkfluff.AdminPanel.Middleware;
using Barkfluff.AdminPanel.Models;

using BarkFluff.Proto.Configuration;

using Grpc.Core;

namespace Barkfluff.AdminPanel.Endpoints;

public static class ReservedNamesEndpoints
{
    public static void MapReservedNamesEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/reserved-names")
            .WithTags("ReservedNames")
            .RequirePermission(AdminPermissions.ReservedNamesManage);

        group.MapGet("/", async (
            ConfigurationApi.ConfigurationApiClient configClient) =>
        {
            try
            {
                var response = await configClient.GetReservedNamesAsync(new GetReservedNamesRequest());
                return Results.Ok(response.Names.ToList());
            }
            catch (RpcException ex)
            {
                return Results.Problem($"Ошибка gRPC: {ex.Status.Detail}");
            }
        });

        group.MapPost("/", async (
            ConfigurationApi.ConfigurationApiClient configClient,
            AddReservedNameDto dto) =>
        {
            try
            {
                var response = await configClient.AddReservedNameAsync(new AddReservedNameRequest { Name = dto.Name });
                if (!response.Success)
                    return Results.BadRequest(new { message = response.Message });

                return Results.Ok(new { message = response.Message });
            }
            catch (RpcException ex)
            {
                return Results.Problem($"Ошибка gRPC: {ex.Status.Detail}");
            }
        });

        group.MapPut("/", async (
            ConfigurationApi.ConfigurationApiClient configClient,
            UpdateReservedNameDto dto) =>
        {
            try
            {
                var response = await configClient.UpdateReservedNameAsync(new UpdateReservedNameRequest
                {
                    OldName = dto.OldName,
                    NewName = dto.NewName
                });
                if (!response.Success)
                    return Results.BadRequest(new { message = response.Message });

                return Results.Ok(new { message = response.Message });
            }
            catch (RpcException ex)
            {
                return Results.Problem($"Ошибка gRPC: {ex.Status.Detail}");
            }
        });

        group.MapDelete("/{name}", async (
            string name,
            ConfigurationApi.ConfigurationApiClient configClient) =>
        {
            try
            {
                var decodedName = Uri.UnescapeDataString(name);
                var response = await configClient.DeleteReservedNameAsync(new DeleteReservedNameRequest { Name = decodedName });
                if (!response.Success)
                    return Results.BadRequest(new { message = response.Message });

                return Results.Ok(new { message = response.Message });
            }
            catch (RpcException ex)
            {
                return Results.Problem($"Ошибка gRPC: {ex.Status.Detail}");
            }
        });
    }
}

public class AddReservedNameDto
{
    public string Name { get; set; } = string.Empty;
}

public class UpdateReservedNameDto
{
    public string OldName { get; set; } = string.Empty;
    public string NewName { get; set; } = string.Empty;
}
