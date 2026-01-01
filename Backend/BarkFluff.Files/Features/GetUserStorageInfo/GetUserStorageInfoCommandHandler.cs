using BarkFluff.Files.Persistence;
using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Proto.Files;

using MediatR;

namespace BarkFluff.Files.Features.GetUserStorageInfo;

public class GetUserStorageInfoCommandHandler : IRequestHandler<GetUserStorageInfoCommand, GetUserStorageInfoResponse>
{
    private readonly UploadedFilesStorage _uploadedFilesStorage;
    private readonly UserContext _userContext;
    private readonly ILogger<GetUserStorageInfoCommandHandler> _logger;

    // Лимит хранилища по умолчанию - 5 ГБ в байтах
    private const long DefaultStorageLimit = 5L * 1024 * 1024 * 1024;

    public GetUserStorageInfoCommandHandler(
        UploadedFilesStorage uploadedFilesStorage,
        UserContext userContext,
        ILogger<GetUserStorageInfoCommandHandler> logger)
    {
        _uploadedFilesStorage = uploadedFilesStorage;
        _userContext = userContext;
        _logger = logger;
    }

    public async Task<GetUserStorageInfoResponse> Handle(GetUserStorageInfoCommand request, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Запрос информации о хранилище. UserId: {UserId}",
            _userContext.UserId
        );

        var usedStorage = await _uploadedFilesStorage.GetUserStorageUsed(_userContext.UserId);

        _logger.LogInformation(
            "Информация о хранилище получена. UserId: {UserId}, Использовано: {UsedStorage} байт, Лимит: {TotalStorage} байт",
            _userContext.UserId,
            usedStorage,
            DefaultStorageLimit
        );

        return new GetUserStorageInfoResponse
        {
            UsedStorage = usedStorage,
            TotalStorage = DefaultStorageLimit
        };
    }
}
