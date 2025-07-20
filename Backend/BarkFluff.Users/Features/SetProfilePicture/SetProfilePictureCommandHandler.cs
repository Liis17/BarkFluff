using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Proto.Files;
using BarkFluff.Proto.Users;
using BarkFluff.Shared.Exceptions.Users;
using BarkFluff.Users.Infrastructure;
using BarkFluff.Users.Persistence.Services;
using MediatR;

namespace BarkFluff.Users.Features.SetProfilePicture;

public class SetProfilePictureCommandHandler : IRequestHandler<SetProfilePictureCommand, SetProfilePictureResponse>
{
    private readonly FilesServerApi.FilesServerApiClient _filesServerApiClient;
    private readonly UsersStorage _usersStorage;
    private readonly UserContext _userContext;
    private readonly UserInfoQueueSender _userInfoQueueSender;

    public SetProfilePictureCommandHandler(
        FilesServerApi.FilesServerApiClient filesServerApiClient,
        UsersStorage usersStorage, UserContext userContext, UserInfoQueueSender userInfoQueueSender)
    {
        _filesServerApiClient = filesServerApiClient;
        _usersStorage = usersStorage;
        _userContext = userContext;
        _userInfoQueueSender = userInfoQueueSender;
    }

    public async Task<SetProfilePictureResponse> Handle(SetProfilePictureCommand request, CancellationToken cancellationToken)
    {
        var fileUrl = string.Empty;
        var previewUrl = string.Empty;
        
        if (request.FileId != null)
        {
            // Получаем информацию о файле по его ID
            var fileDataRequest = new GetFileDataRequest
            {
                FileId = request.FileId.ToString()
            };
        
            var fileDataResponse = await _filesServerApiClient.GetFileDataAsync(fileDataRequest, cancellationToken: cancellationToken);

            if (fileDataResponse.FileInfo.Type != UploadFileType.UserAvatar)
            {
                throw new ProfilePictureHasNotValidType();
            }
            
            fileUrl = fileDataResponse.FileInfo.FileUrl;
            previewUrl = fileDataResponse.FileInfo.PreviewUrl;
        }
        
        // Обновляем профильное изображение пользователя
        await _usersStorage.UpdateProfilePicture(_userContext.UserId, fileUrl, previewUrl);

        await _userInfoQueueSender.UserChangedAvatarEvent(_userContext.UserId, fileUrl, previewUrl);
        
        return new SetProfilePictureResponse();
    }
}