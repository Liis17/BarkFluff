using MediatR;

namespace BarkFluff.Users.Features.UserSettings.SetChatBackground;

public class SetChatBackgroundCommand : IRequest
{
    public Guid ChatId { get; set; }

    public string? FileId { get; set; }
}
