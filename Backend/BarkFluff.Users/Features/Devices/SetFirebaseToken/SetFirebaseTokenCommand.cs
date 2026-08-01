using BarkFluff.Users.Domain;

using MediatR;

namespace BarkFluff.Users.Features.Devices.SetFirebaseToken;

public class SetFirebaseTokenCommand : IRequest<Unit>
{
    public string FirebaseToken { get; set; } = string.Empty;
    public DevicePushPlatform PushPlatform { get; set; } = DevicePushPlatform.Android;
}
