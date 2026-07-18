using Barkfluff.AdminPanel.Models;

using BarkFluff.Proto.Federation;
using BarkFluff.Proto.FederationInternal;

using Google.Protobuf;

using Grpc.Core;

namespace Barkfluff.AdminPanel.Endpoints;

public static class FederationEndpoints
{
    public static void MapFederationEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/federation")
            .WithTags("Federation");

        // GET /api/federation/status
        group.MapGet("/status", async (
            FederationInternalApi.FederationInternalApiClient federationClient,
            HttpContext context) =>
        {
            if (context.Items["AuthToken"] is not AuthToken)
                return Results.Unauthorized();

            try
            {
                var response = await federationClient.GetFederationStatusAsync(new GetFederationStatusRequest());

                return Results.Ok(new
                {
                    serverName = response.ServerName,
                    enabled = response.Enabled,
                    knownServersActive = response.KnownServersActive,
                    outboxPending = response.OutboxPending,
                    outboxDeadletter = response.OutboxDeadletter,
                    ownKeys = response.OwnKeys.Select(MapSigningKey),
                });
            }
            catch (RpcException ex)
            {
                return Results.Problem($"Ошибка gRPC: {ex.Status.Detail}");
            }
        })
        .WithName("GetFederationStatus");

        // GET /api/federation/peers
        group.MapGet("/peers", async (
            FederationInternalApi.FederationInternalApiClient federationClient,
            HttpContext context) =>
        {
            if (context.Items["AuthToken"] is not AuthToken)
                return Results.Unauthorized();

            try
            {
                var response = await federationClient.GetKnownServersAsync(new GetKnownServersRequest());

                var peers = response.Servers.Select(s => new
                {
                    serverName = s.ServerName,
                    federationEndpoint = s.FederationEndpoint,
                    source = s.Source,
                    status = s.Status,
                    firstSeenAt = s.FirstSeenAt?.ToDateTime(),
                    lastSeenAt = s.LastSeenAt?.ToDateTime(),
                    keys = s.Keys.Select(MapSigningKey),
                });

                return Results.Ok(peers);
            }
            catch (RpcException ex)
            {
                return Results.Problem($"Ошибка gRPC: {ex.Status.Detail}");
            }
        })
        .WithName("GetKnownServers");

        // POST /api/federation/peers — добавить/обновить ручного пира
        group.MapPost("/peers", async (
            UpsertManualPeerRequestBody body,
            FederationInternalApi.FederationInternalApiClient federationClient,
            HttpContext context) =>
        {
            if (context.Items["AuthToken"] is not AuthToken)
                return Results.Unauthorized();

            if (string.IsNullOrWhiteSpace(body.ServerName) || string.IsNullOrWhiteSpace(body.Endpoint))
                return Results.BadRequest("server_name and endpoint are required");

            try
            {
                var request = new UpsertManualPeerRequest
                {
                    ServerName = body.ServerName.Trim(),
                    FederationEndpoint = body.Endpoint.Trim(),
                };

                foreach (var key in body.Keys ?? [])
                {
                    if (string.IsNullOrWhiteSpace(key.KeyId) || string.IsNullOrWhiteSpace(key.PublicKeyBase64))
                        continue;

                    request.Keys.Add(new SigningKey
                    {
                        KeyId = key.KeyId.Trim(),
                        PublicKey = ByteString.CopyFrom(Convert.FromBase64String(key.PublicKeyBase64)),
                    });
                }

                foreach (var spki in body.TlsSpkiSha256 ?? [])
                {
                    if (!string.IsNullOrWhiteSpace(spki))
                        request.TlsSpkiSha256.Add(spki.Trim());
                }

                await federationClient.UpsertManualPeerAsync(request);

                return Results.Ok();
            }
            catch (FormatException)
            {
                return Results.BadRequest("public_key_base64 должен быть валидным base64");
            }
            catch (RpcException ex)
            {
                return Results.Problem($"Ошибка gRPC: {ex.Status.Detail}");
            }
        })
        .WithName("UpsertManualPeer");

        // POST /api/federation/peers/{server}/block — { "blocked": true/false }
        group.MapPost("/peers/{server}/block", async (
            string server,
            SetServerBlockedRequestBody body,
            FederationInternalApi.FederationInternalApiClient federationClient,
            HttpContext context) =>
        {
            if (context.Items["AuthToken"] is not AuthToken)
                return Results.Unauthorized();

            try
            {
                await federationClient.SetServerBlockedAsync(new SetServerBlockedRequest
                {
                    ServerName = server,
                    Blocked = body.Blocked,
                });

                return Results.Ok();
            }
            catch (RpcException ex)
            {
                return Results.Problem($"Ошибка gRPC: {ex.Status.Detail}");
            }
        })
        .WithName("SetServerBlocked");

        // POST /api/federation/keys/rotate
        group.MapPost("/keys/rotate", async (
            FederationInternalApi.FederationInternalApiClient federationClient,
            HttpContext context) =>
        {
            if (context.Items["AuthToken"] is not AuthToken)
                return Results.Unauthorized();

            try
            {
                var response = await federationClient.RotateSigningKeyAsync(new RotateSigningKeyRequest());

                return Results.Ok(new
                {
                    newKeyId = response.NewKeyId,
                    oldKeyId = response.OldKeyId,
                    oldKeyExpiresAt = response.OldKeyExpiresAt?.ToDateTime(),
                });
            }
            catch (RpcException ex)
            {
                return Results.Problem($"Ошибка gRPC: {ex.Status.Detail}");
            }
        })
        .WithName("RotateSigningKey");
    }

    private static object MapSigningKey(SigningKey key) => new
    {
        keyId = key.KeyId,
        publicKeyBase64 = key.PublicKey.ToBase64(),
        expiredAt = key.ExpiredAt?.ToDateTime(),
    };

    public record UpsertManualPeerKeyBody(string? KeyId, string? PublicKeyBase64);
    public record UpsertManualPeerRequestBody(string? ServerName, string? Endpoint, List<UpsertManualPeerKeyBody>? Keys, List<string>? TlsSpkiSha256);
    public record SetServerBlockedRequestBody(bool Blocked);
}
