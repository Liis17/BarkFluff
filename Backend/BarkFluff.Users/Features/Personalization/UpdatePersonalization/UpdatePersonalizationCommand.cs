using BarkFluff.Proto.Users;

using MediatR;

namespace BarkFluff.Users.Features.Personalization.UpdatePersonalization;

public class UpdatePersonalizationCommand : IRequest
{
    public UserPersonalizationData Personalization { get; set; }
}
