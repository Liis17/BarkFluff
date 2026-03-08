using BarkFluff.Files.Persistence;
using BarkFluff.Proto.Files;

using MediatR;

namespace BarkFluff.Files.Features.GetUserStorageInfoServer;

public class GetUserStorageInfoServerCommandHandler : IRequestHandler<GetUserStorageInfoServerCommand, GetUserStorageInfoResponse>
{
    private readonly UploadedFilesStorage _uploadedFilesStorage;
    private readonly BarkFluff.Proto.Users.UsersServerApi.UsersServerApiClient _usersClient;
    private readonly ILogger<GetUserStorageInfoServerCommandHandler> _logger;

    public GetUserStorageInfoServerCommandHandler(
        UploadedFilesStorage uploadedFilesStorage,
        BarkFluff.Proto.Users.UsersServerApi.UsersServerApiClient usersClient,
        ILogger<GetUserStorageInfoServerCommandHandler> logger)
    {
        _uploadedFilesStorage = uploadedFilesStorage;
        _usersClient = usersClient;
        _logger = logger;
    }

    public async Task<GetUserStorageInfoResponse> Handle(GetUserStorageInfoServerCommand request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Получение информации о хранилище. UserId: {UserId}", request.UserId);

        var userResponse = await _usersClient.GetByIdAsync(new BarkFluff.Proto.Users.GetByIdRequest
        {
            UserId = request.UserId
        }, cancellationToken: cancellationToken);

        long storageLimitBytes = (long)userResponse.User.StorageLimitGb * 1024 * 1024 * 1024;

        var totalUsedStorage = await _uploadedFilesStorage.GetUserStorageUsed(request.UserId);
        var storageByType = await _uploadedFilesStorage.GetUserStorageByType(request.UserId);

        var response = new GetUserStorageInfoResponse
        {
            TotalUsedStorage = totalUsedStorage,
            StorageLimit = storageLimitBytes
        };

        foreach (var (fileType, size) in storageByType)
        {
            response.StorageByTypes.Add(new GetUserStorageInfoResponse.Types.StorageByType
            {
                FileType = (UploadFileType)(int)fileType,
                UsedStorage = size
            });
        }

        return response;
    }
}
