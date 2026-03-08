using BarkFluff.Files.Persistence;
using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Proto.Files;

using MediatR;

namespace BarkFluff.Files.Features.GetUserStorageInfo;

public class GetUserStorageInfoCommandHandler : IRequestHandler<GetUserStorageInfoCommand, GetUserStorageInfoResponse>
{
    private readonly UploadedFilesStorage _uploadedFilesStorage;
    private readonly UserContext _userContext;
    private readonly BarkFluff.Proto.Users.UsersServerApi.UsersServerApiClient _usersClient;
    private readonly ILogger<GetUserStorageInfoCommandHandler> _logger;

    public GetUserStorageInfoCommandHandler(
        UploadedFilesStorage uploadedFilesStorage,
        UserContext userContext,
        BarkFluff.Proto.Users.UsersServerApi.UsersServerApiClient usersClient,
        ILogger<GetUserStorageInfoCommandHandler> logger)
    {
        _uploadedFilesStorage = uploadedFilesStorage;
        _userContext = userContext;
        _usersClient = usersClient;
        _logger = logger;
    }

    public async Task<GetUserStorageInfoResponse> Handle(GetUserStorageInfoCommand request, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Запрос информации о хранилище. UserId: {UserId}",
            _userContext.UserId
        );

        // Получаем информацию о пользователе для получения лимита
        var userResponse = await _usersClient.GetByIdAsync(new BarkFluff.Proto.Users.GetByIdRequest
        {
            UserId = _userContext.UserId
        }, cancellationToken: cancellationToken);

        // Конвертируем ГБ в байты
        long storageLimitBytes = (long)userResponse.User.StorageLimitGb * 1024 * 1024 * 1024;

        // Получаем общее использованное пространство
        var totalUsedStorage = await _uploadedFilesStorage.GetUserStorageUsed(_userContext.UserId);

        // Получаем использованное пространство по типам файлов
        var storageByType = await _uploadedFilesStorage.GetUserStorageByType(_userContext.UserId);

        var response = new GetUserStorageInfoResponse
        {
            TotalUsedStorage = totalUsedStorage,
            StorageLimit = storageLimitBytes
        };

        // Добавляем информацию по типам файлов
        foreach (var (fileType, size) in storageByType)
        {
            response.StorageByTypes.Add(new GetUserStorageInfoResponse.Types.StorageByType
            {
                FileType = (Proto.Files.UploadFileType)(int)fileType,
                UsedStorage = size
            });
        }

        _logger.LogInformation(
            "Информация о хранилище получена. UserId: {UserId}, Использовано: {UsedStorage} байт, Лимит: {TotalStorage} байт",
            _userContext.UserId,
            totalUsedStorage,
            storageLimitBytes
        );

        return response;
    }
}
