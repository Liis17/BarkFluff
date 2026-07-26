using BarkFluff.Files.Persistence;
using BarkFluff.Proto.Files;
using BarkFluff.Proto.Users;

using MediatR;

using UploadFileType = BarkFluff.Files.Domain.UploadFileType;

namespace BarkFluff.Files.Features.CheckFedAvatarAccess;

/// <summary>
/// Разрешено ли отдать аватар в федерацию (этап 3.4).
/// </summary>
/// <remarks>
/// У аватара своя ветка доступа: он не является вложением сообщения, поэтому проверка
/// «есть общий чат» (<c>CheckFileFederationAccess</c>, 3.2) к нему неприменима — доступ
/// определяется приватностью владельца.
///
/// Ошибка Users трактуется как «скрыт» (fail-closed): лучше не показать аватар, чем
/// показать вопреки настройке.
/// </remarks>
public class CheckFedAvatarAccessCommandHandler
    : IRequestHandler<CheckFedAvatarAccessCommand, CheckFedAvatarAccessResponse>
{
    private readonly UploadedFilesStorage _filesStorage;
    private readonly UsersServerApi.UsersServerApiClient _usersClient;
    private readonly ILogger<CheckFedAvatarAccessCommandHandler> _logger;

    public CheckFedAvatarAccessCommandHandler(
        UploadedFilesStorage filesStorage,
        UsersServerApi.UsersServerApiClient usersClient,
        ILogger<CheckFedAvatarAccessCommandHandler> logger)
    {
        _filesStorage = filesStorage;
        _usersClient = usersClient;
        _logger = logger;
    }

    public async Task<CheckFedAvatarAccessResponse> Handle(
        CheckFedAvatarAccessCommand request,
        CancellationToken cancellationToken)
    {
        var denied = new CheckFedAvatarAccessResponse { Allowed = false };

        if (!Guid.TryParse(request.FileId, out var fileId))
        {
            return denied;
        }

        var file = await _filesStorage.GetFile(fileId);

        // «Не аватар» и «нет файла» снаружи неразличимы — существование файлов не светим.
        if (file is null || file.Type != UploadFileType.UserAvatar || string.IsNullOrEmpty(file.Etag))
        {
            return denied;
        }

        var ownerId = file.Uploaders?.FirstOrDefault() ?? 0;
        if (ownerId <= 0)
        {
            return denied;
        }

        try
        {
            var visibility = await _usersClient.IsAvatarVisibleToFederationAsync(
                new IsAvatarVisibleToFederationRequest { UserId = ownerId },
                cancellationToken: cancellationToken);

            return new CheckFedAvatarAccessResponse { Allowed = visibility.Visible };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Не удалось проверить приватность аватара {FileId} (владелец {OwnerId}) — отказ (fail-closed)",
                fileId, ownerId);
            return denied;
        }
    }
}
