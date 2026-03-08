namespace BarkFluff.Shared.Queue.Users;

public class UserChangedName
{
    public long UserId { get; set; }

    public string NewFirstName { get; set; }

    public string NewLastName { get; set; }
}