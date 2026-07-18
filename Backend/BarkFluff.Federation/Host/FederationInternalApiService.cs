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
    private readonly ActiveSigningKeyCache _activeSigningKeyCache;

    public FederationInternalApiService(
        SigningKeyService signingKeyService,
        WellKnownDocumentService wellKnownDocumentService,
        ActiveSigningKeyCache activeSigningKeyCache)
    {
        _signingKeyService = signingKeyService;
        _wellKnownDocumentService = wellKnownDocumentService;
        _activeSigningKeyCache = activeSigningKeyCache;
    }

    public override async Task<RotateSigningKeyResponse> RotateSigningKey(RotateSigningKeyRequest request, ServerCallContext context)
    {
        var (newKey, oldKey) = await _signingKeyService.RotateAsync(context.CancellationToken);

        // Well-known после ротации публикует оба ключа и подписан новым; исходящие XFed-подписи —
        // новым активным ключом немедленно.
        await _wellKnownDocumentService.RebuildAsync(context.CancellationToken);
        await _activeSigningKeyCache.RefreshAsync(context.CancellationToken);

        return new RotateSigningKeyResponse
        {
            NewKeyId = newKey.KeyId,
            OldKeyId = oldKey.KeyId,
            OldKeyExpiresAt = Timestamp.FromDateTime(DateTime.SpecifyKind(oldKey.ExpiredAt!.Value, DateTimeKind.Utc)),
        };
    }
}
