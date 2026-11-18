using BarkFluff.Users.Persistence.Services;

using MediatR;

namespace BarkFluff.Users.Features.AccountRestrictions;

public sealed class CompleteAccountCleanupCommandHandler : IRequestHandler<CompleteAccountCleanupCommand>
{
    private readonly UsersStorage _usersStorage;

    public CompleteAccountCleanupCommandHandler(UsersStorage usersStorage) => _usersStorage = usersStorage;

    public Task Handle(CompleteAccountCleanupCommand request, CancellationToken cancellationToken) =>
        _usersStorage.CompleteAccountCleanup(request.UserId, cancellationToken);
}
