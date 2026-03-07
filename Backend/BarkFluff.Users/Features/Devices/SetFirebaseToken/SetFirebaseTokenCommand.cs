using MediatR;

namespace BarkFluff.Users.Features.Devices.SetFirebaseToken;

public class SetFirebaseTokenCommand : IRequest<Unit>
{
    public string FirebaseToken { get; set; } = string.Empty;
}
