using BarkFluff.Messages.Persistence.Services;
using BarkFluff.Proto.Messages;

using MediatR;

namespace BarkFluff.Messages.Features.CheckFedFileUserAccess;

/// <summary>
/// Авторизация federated-файла на уровне ПОЛЬЗОВАТЕЛЯ (этап 3.3) — второй, независимый от
/// origin уровень проверки: origin решает «этой ноде можно», мы — «этому пользователю можно».
/// Ни один уровень не доверяет другому.
/// </summary>
/// <remarks>
/// Вместе с ответом возвращается снапшот метаданных (3.1), чтобы скачивание не ходило в
/// Messages второй раз: имя нужно для <c>Content-Disposition</c>, размер — для отсечения
/// по объёму (риск №44).
/// </remarks>
public class CheckFedFileUserAccessQueryHandler
    : IRequestHandler<CheckFedFileUserAccessQuery, CheckFedFileUserAccessResponse>
{
    private readonly ChatsStorage _chatsStorage;

    public CheckFedFileUserAccessQueryHandler(ChatsStorage chatsStorage)
    {
        _chatsStorage = chatsStorage;
    }

    public async Task<CheckFedFileUserAccessResponse> Handle(
        CheckFedFileUserAccessQuery request,
        CancellationToken cancellationToken)
    {
        var denied = new CheckFedFileUserAccessResponse { Allowed = false };

        var originServer = request.OriginServer.Trim().ToLowerInvariant();

        if (request.UserId <= 0 || originServer.Length == 0 || !Guid.TryParse(request.FileId, out _))
        {
            return denied;
        }

        var snapshot = await _chatsStorage.GetFederatedAttachmentForUserAsync(
            request.UserId, originServer, request.FileId);

        if (snapshot is null)
        {
            return denied;
        }

        return new CheckFedFileUserAccessResponse
        {
            Allowed = true,
            FileName = snapshot.FileName ?? string.Empty,
            SizeBytes = snapshot.SizeBytes,
            AttachmentType = snapshot.AttachmentType,
        };
    }
}
