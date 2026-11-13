namespace BarkFluff.Shared.Queue.Users;

public class UserAccountStatusChanged
{
    public long UserId { get; set; }

    public int Status { get; set; }
}
