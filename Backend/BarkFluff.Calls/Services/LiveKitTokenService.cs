using BarkFluff.Calls.Settings;

using Livekit.Server.Sdk.Dotnet;

namespace BarkFluff.Calls.Services;

/// <summary>
/// Выдача LiveKit access-токенов. SDK подписывает JWT секретом LiveKit (HS256)
/// и кладёт grants на конкретную комнату. Backend не участвует в SDP/ICE.
/// </summary>
public class LiveKitTokenService
{
    private readonly LiveKitSettings _settings;

    public LiveKitTokenService(LiveKitSettings settings)
    {
        _settings = settings;
    }

    /// <summary>WS/WSS-адрес LiveKit для клиента (дублируется в Beacon).</summary>
    public string Url => _settings.Url;

    /// <summary>
    /// Токен на вход в комнату звонка с правом публиковать аудио/видео.
    /// Identity — стабильный идентификатор участника (userId).
    /// </summary>
    public string CreateRoomToken(string room, string identity, string? displayName)
    {
        return new AccessToken(_settings.ApiKey, _settings.ApiSecret)
            .WithIdentity(identity)
            .WithName(string.IsNullOrEmpty(displayName) ? identity : displayName)
            .WithGrants(new VideoGrants
            {
                RoomJoin = true,
                Room = room,
                CanPublish = true,
                CanSubscribe = true,
                CanPublishData = true,
            })
            .WithTtl(TimeSpan.FromHours(2))
            .ToJwt();
    }
}
