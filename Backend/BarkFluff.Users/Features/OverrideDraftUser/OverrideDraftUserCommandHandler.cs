using BarkFluff.Proto.Users;
using BarkFluff.Users.Persistence.Services;
using Grpc.Core;
using MediatR;

namespace BarkFluff.Users.Features.OverrideDraftUser;

public class OverrideDraftUserCommandHandler(UsersStorage usersStorage, ILogger<OverrideDraftUserCommandHandler> logger)
    : IRequestHandler<OverrideDraftUserCommand, AddDraftUserResponse>
{
    // A username or email collision is not proof that the caller owns a draft.
    // Registration is resumed through Identity's challenge secret instead.
    public Task<AddDraftUserResponse> Handle(OverrideDraftUserCommand request, CancellationToken cancellationToken) =>
        throw new RpcException(new Status(StatusCode.FailedPrecondition,
            "Resume the original registration challenge; draft replacement is disabled"));
}
