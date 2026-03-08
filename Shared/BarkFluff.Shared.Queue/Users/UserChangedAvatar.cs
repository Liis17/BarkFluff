namespace BarkFluff.Shared.Queue.Users;

public class UserChangedAvatar
{
    public long UserId { get; set; }

    public string ProfilePictureUrl { get; set; }

    public string ProfilePictureUrlPreview { get; set; }
}