using BarkFluff.Messages.Persistence.Services;
using BarkFluff.Proto.Messages;

using MediatR;

namespace BarkFluff.Messages.Features.CheckFederatedPresenceAccess;

/// <summary>
/// Origin-сторона проверки отношений (риск №42): нода-подписчик видит presence нашего
/// пользователя только при наличии активного федеративного чата между ними.
/// Отсутствующий uuid и uuid без общего чата неразличимы — существование аккаунтов не светим.
/// </summary>
public class CheckFederatedPresenceAccessQueryHandler
    : IRequestHandler<CheckFederatedPresenceAccessQuery, CheckFederatedPresenceAccessResponse>
{
    private readonly ChatsStorage _chatsStorage;

    public CheckFederatedPresenceAccessQueryHandler(ChatsStorage chatsStorage)
    {
        _chatsStorage = chatsStorage;
    }

    public async Task<CheckFederatedPresenceAccessResponse> Handle(
        CheckFederatedPresenceAccessQuery request,
        CancellationToken cancellationToken)
    {
        var response = new CheckFederatedPresenceAccessResponse();

        // ChatMember.ServerName хранится канонизированным (2.3) — приводим вход к той же форме.
        var requestingServer = request.RequestingServer.Trim().ToLowerInvariant();

        if (requestingServer.Length == 0)
        {
            return response;
        }

        var parsed = new List<Guid>();
        foreach (var raw in request.UserUuids.Distinct())
        {
            if (Guid.TryParse(raw, out var uuid))
            {
                parsed.Add(uuid);
            }
        }

        if (parsed.Count == 0)
        {
            return response;
        }

        var allowed = await _chatsStorage.GetUuidsSharingFederatedChatWithServer(
            requestingServer, parsed);

        response.AllowedUserUuids.AddRange(allowed.Select(uuid => uuid.ToString()));

        return response;
    }
}
