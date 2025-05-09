using BarkFluff.Proto.Users;
using MediatR;

namespace BarkFluff.Users.Features.SetProfilePicture;

public class SetProfilePictureCommandHandler : IRequestHandler<SetProfilePictureCommand, SetProfilePictureResponse>
{
    public Task<SetProfilePictureResponse> Handle(SetProfilePictureCommand request, CancellationToken cancellationToken)
    {
        return null;
    }
}