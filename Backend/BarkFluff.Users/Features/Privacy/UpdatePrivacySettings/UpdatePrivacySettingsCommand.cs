using BarkFluff.Proto.Users;

using MediatR;

namespace BarkFluff.Users.Features.Privacy.UpdatePrivacySettings;

public class UpdatePrivacySettingsCommand : IRequest
{
    public PrivacySettings Settings { get; set; }
}
