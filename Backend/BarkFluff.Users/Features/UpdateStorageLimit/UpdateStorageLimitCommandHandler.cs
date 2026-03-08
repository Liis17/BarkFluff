using BarkFluff.Proto.Users;
using BarkFluff.Users.Mapping;
using BarkFluff.Users.Persistence.Services;

using MediatR;

namespace BarkFluff.Users.Features.UpdateStorageLimit;

public class UpdateStorageLimitCommandHandler : IRequestHandler<UpdateStorageLimitCommand, UpdateStorageLimitResponse>
{
    private readonly UsersStorage _usersStorage;
    private readonly ILogger<UpdateStorageLimitCommandHandler> _logger;

    public UpdateStorageLimitCommandHandler(UsersStorage usersStorage, ILogger<UpdateStorageLimitCommandHandler> logger)
    {
        _usersStorage = usersStorage;
        _logger = logger;
    }

    public async Task<UpdateStorageLimitResponse> Handle(UpdateStorageLimitCommand request, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Обновление лимита хранилища для пользователя {UserId}: {StorageLimitGb} ГБ",
            request.UserId, request.StorageLimitGb);

        await _usersStorage.UpdateStorageLimitGb(request.UserId, request.StorageLimitGb);

        var user = await _usersStorage.GetById(request.UserId);

        return new UpdateStorageLimitResponse
        {
            User = user!.ToGrpc()
        };
    }
}
