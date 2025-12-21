using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Proto.Files;
using BarkFluff.Proto.Users;
using BarkFluff.Shared.Exceptions.Users;
using BarkFluff.Users.Infrastructure;
using BarkFluff.Users.Persistence.Services;
using MediatR;
using Microsoft.Extensions.Logging;

namespace BarkFluff.Users.Features.SetProfilePicture;

public class SetProfilePictureCommandHandler : IRequestHandler<SetProfilePictureCommand, SetProfilePictureResponse>
{
    private readonly FilesServerApi.FilesServerApiClient _filesServerApiClient;
    private readonly UsersStorage _usersStorage;
    private readonly UserContext _userContext;
    private readonly UserInfoQueueSender _userInfoQueueSender;
    private readonly ILogger<SetProfilePictureCommandHandler> _logger;

    public SetProfilePictureCommandHandler(
        FilesServerApi.FilesServerApiClient filesServerApiClient,
        UsersStorage usersStorage, UserContext userContext, UserInfoQueueSender userInfoQueueSender,
        ILogger<SetProfilePictureCommandHandler> logger)
    {
        _filesServerApiClient = filesServerApiClient;
        _usersStorage = usersStorage;
        _userContext = userContext;
        _userInfoQueueSender = userInfoQueueSender;
        _logger = logger;
    }

    public async Task<SetProfilePictureResponse> Handle(SetProfilePictureCommand request, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Начало установки аватара для пользователя {UserId}. FileId: {FileId}",
            _userContext.UserId,
            request.FileId?.ToString() ?? "null (удаление аватара)"
        );

        var fileUrl = string.Empty;
        var previewUrl = string.Empty;

        if (request.FileId != null)
        {
            _logger.LogDebug("Получение информации о файле {FileId} из сервиса Files", request.FileId);

            // Получаем информацию о файле по его ID
            var fileDataRequest = new GetFileDataRequest
            {
                FileId = request.FileId.ToString()
            };

            var fileDataResponse = await _filesServerApiClient.GetFileDataAsync(fileDataRequest, cancellationToken: cancellationToken);

            if (fileDataResponse.FileInfo.Type != UploadFileType.UserAvatar)
            {
                _logger.LogWarning(
                    "Файл {FileId} имеет неверный тип {FileType}, ожидается {ExpectedType}",
                    request.FileId,
                    fileDataResponse.FileInfo.Type,
                    UploadFileType.UserAvatar
                );
                throw new ProfilePictureHasNotValidType();
            }

            fileUrl = fileDataResponse.FileInfo.FileUrl;
            previewUrl = fileDataResponse.FileInfo.PreviewUrl;

            _logger.LogDebug(
                "Файл аватара проверен. URL: {FileUrl}",
                fileUrl
            );
        }

        // Обновляем профильное изображение пользователя
        await _usersStorage.UpdateProfilePicture(_userContext.UserId, fileUrl, previewUrl);

        _logger.LogDebug(
            "Отправка события об изменении аватара в очередь RabbitMQ для пользователя {UserId}",
            _userContext.UserId
        );

        await _userInfoQueueSender.UserChangedAvatarEvent(_userContext.UserId, fileUrl, previewUrl);

        _logger.LogInformation(
            "Аватар успешно установлен для пользователя {UserId}",
            _userContext.UserId
        );

        return new SetProfilePictureResponse();
    }
}