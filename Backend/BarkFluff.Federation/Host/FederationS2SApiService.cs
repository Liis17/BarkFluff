using BarkFluff.Federation.Services;
using BarkFluff.Proto.Federation;
using BarkFluff.Proto.Users;

using Google.Protobuf.WellKnownTypes;

using Grpc.Core;

namespace BarkFluff.Federation.Host;

// Авторизация — НЕ XAuth: Ed25519-подпись S2S-запросов (XFed, этап 1.3).
// В 1.1/1.2 Ping и GetServerKeys временно доступны без подписи (GetServerKeys — bootstrap-канал,
// останется неподписанным и после 1.3, см. docs/rearch/phase-1/step-1.2-keys-wellknown.md).
public class FederationS2SApiService : FederationS2SApi.FederationS2SApiBase
{
    private readonly IConfiguration _configuration;
    private readonly SigningKeyService _signingKeyService;
    private readonly UsersServerApi.UsersServerApiClient _usersClient;

    public FederationS2SApiService(
        IConfiguration configuration,
        SigningKeyService signingKeyService,
        UsersServerApi.UsersServerApiClient usersClient)
    {
        _configuration = configuration;
        _signingKeyService = signingKeyService;
        _usersClient = usersClient;
    }

    public override Task<PingResponse> Ping(PingRequest request, ServerCallContext context)
    {
        var response = new PingResponse
        {
            ServerName = _configuration["Federation:ServerName"] ?? string.Empty,
            ServerTime = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow),
        };
        response.ProtocolVersions.Add(1);

        return Task.FromResult(response);
    }

    public override async Task<GetServerKeysResponse> GetServerKeys(GetServerKeysRequest request, ServerCallContext context)
    {
        var keys = await _signingKeyService.GetNonRevokedKeysAsync(context.CancellationToken);

        var response = new GetServerKeysResponse
        {
            ServerName = _configuration["Federation:ServerName"] ?? string.Empty,
        };

        response.Keys.AddRange(keys.Select(k => new SigningKey
        {
            KeyId = k.KeyId,
            PublicKey = Google.Protobuf.ByteString.CopyFrom(k.PublicKey),
            ExpiredAt = k.ExpiredAt.HasValue
                ? Timestamp.FromDateTime(DateTime.SpecifyKind(k.ExpiredAt.Value, DateTimeKind.Utc))
                : null,
        }));

        return response;
    }

    // S2S профиль пользователя этой ноды (этап 2.1). XFed уже проверил подпись в per-service интерсепторе —
    // здесь только privacy-фильтрованная отдача через Users.GetFederatedProfile.
    public override async Task<GetUserProfileResponse> GetUserProfile(GetUserProfileRequest request, ServerCallContext context)
    {
        var usersRequest = new GetFederatedProfileRequest();
        switch (request.UserCase)
        {
            case GetUserProfileRequest.UserOneofCase.Username:
                usersRequest.Username = request.Username;
                break;
            case GetUserProfileRequest.UserOneofCase.Uuid:
                usersRequest.Uuid = request.Uuid;
                break;
            default:
                return new GetUserProfileResponse { Found = false };
        }

        var profile = await _usersClient.GetFederatedProfileAsync(usersRequest, cancellationToken: context.CancellationToken);

        if (!profile.Found)
            return new GetUserProfileResponse { Found = false };

        var response = new GetUserProfileResponse
        {
            Found = true,
            Uuid = profile.Uuid,
            Username = profile.Username,
            FirstName = profile.FirstName,
            LastName = profile.LastName,
            Bio = profile.Bio,
        };

        if (!string.IsNullOrEmpty(profile.AvatarFileId))
        {
            response.Avatar = new FederatedFileRef
            {
                OriginServer = _configuration["Federation:ServerName"] ?? string.Empty,
                FileId = profile.AvatarFileId,
            };
        }

        return response;
    }
}
