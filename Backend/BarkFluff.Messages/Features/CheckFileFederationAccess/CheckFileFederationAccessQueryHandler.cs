using BarkFluff.Messages.Persistence.Services;
using BarkFluff.Proto.Messages;

using MediatR;

namespace BarkFluff.Messages.Features.CheckFileFederationAccess;

/// <summary>
/// Авторизация файла на уровне НОДЫ (этап 3.2): знание file_id само по себе прав не даёт —
/// файл отдаётся, только если он вложен в активный федеративный чат с запрашивающей нодой.
/// </summary>
/// <remarks>
/// Это первый из двух независимых уровней проверки: здесь origin решает «этой ноде можно»,
/// а принимающая нода отдельно проверяет «этому пользователю можно» при выдаче ссылки (этап 3.3).
/// Ни один уровень не доверяет другому.
///
/// Аватары этим RPC не обслуживаются: <c>UserAvatar</c> вообще не является вложением сообщения,
/// у него своя ветка доступа по <c>AvatarVisibility</c> (этап 3.4).
/// </remarks>
public class CheckFileFederationAccessQueryHandler
    : IRequestHandler<CheckFileFederationAccessQuery, CheckFileFederationAccessResponse>
{
    private readonly ChatsStorage _chatsStorage;

    public CheckFileFederationAccessQueryHandler(ChatsStorage chatsStorage)
    {
        _chatsStorage = chatsStorage;
    }

    public async Task<CheckFileFederationAccessResponse> Handle(
        CheckFileFederationAccessQuery request,
        CancellationToken cancellationToken)
    {
        // ChatMember.ServerName хранится канонизированным (2.3) — приводим вход к той же форме.
        var requestingServer = request.RequestingServer.Trim().ToLowerInvariant();

        if (requestingServer.Length == 0 || !Guid.TryParse(request.FileId, out _))
        {
            return new CheckFileFederationAccessResponse { Allowed = false };
        }

        var allowed = await _chatsStorage.IsFileSharedWithServerAsync(request.FileId, requestingServer);

        return new CheckFileFederationAccessResponse { Allowed = allowed };
    }
}
