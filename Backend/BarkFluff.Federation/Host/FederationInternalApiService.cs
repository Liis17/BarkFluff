using BarkFluff.Federation.Services;
using BarkFluff.Proto.FederationInternal;
using BarkFluff.Shared.Identity;

using Google.Protobuf.WellKnownTypes;

using Grpc.Core;

using Microsoft.AspNetCore.Authorization;

namespace BarkFluff.Federation.Host;

// Внутренний API Federation-сервиса. Авторизация — XAuth, TokenType.Service.
// Реализован только RotateSigningKey (1.2); управление пирами — 1.4, статус — 1.4/1.2.
[Authorize(Policy = nameof(TokenType.Service))]
public class FederationInternalApiService : FederationInternalApi.FederationInternalApiBase
{
    private readonly SigningKeyService _signingKeyService;
    private readonly WellKnownDocumentService _wellKnownDocumentService;

    public FederationInternalApiService(SigningKeyService signingKeyService, WellKnownDocumentService wellKnownDocumentService)
    {
        _signingKeyService = signingKeyService;
        _wellKnownDocumentService = wellKnownDocumentService;
    }

    public override async Task<RotateSigningKeyResponse> RotateSigningKey(RotateSigningKeyRequest request, ServerCallContext context)
    {
        var (newKey, oldKey) = await _signingKeyService.RotateAsync(context.CancellationToken);

        // Well-known после ротации публикует оба ключа и подписан новым.
        await _wellKnownDocumentService.RebuildAsync(context.CancellationToken);

        return new RotateSigningKeyResponse
        {
            NewKeyId = newKey.KeyId,
            OldKeyId = oldKey.KeyId,
            OldKeyExpiresAt = Timestamp.FromDateTime(DateTime.SpecifyKind(oldKey.ExpiredAt!.Value, DateTimeKind.Utc)),
        };
    }
}
