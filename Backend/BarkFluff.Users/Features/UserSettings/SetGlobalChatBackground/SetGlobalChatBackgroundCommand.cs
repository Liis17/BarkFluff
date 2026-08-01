using MediatR;

namespace BarkFluff.Users.Features.UserSettings.SetGlobalChatBackground;

public class SetGlobalChatBackgroundCommand : IRequest
{
    public string? FileId { get; set; }
}
