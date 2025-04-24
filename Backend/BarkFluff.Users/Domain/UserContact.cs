namespace BarkFluff.Users.Domain;

public class UserContact
{
    public long Id { get; set; }
    
    public string Email { get; set; }
    
    public int UserId { get; set; }
    
    public User User { get; set; }
}