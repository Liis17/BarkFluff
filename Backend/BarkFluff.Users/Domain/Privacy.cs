namespace BarkFluff.Users.Domain;

public class Privacy
{
    public long Id { get; set; }

    public long UserId { get; set; }

    public User User { get; set; }

    public bool ProfileVisibleOnSite { get; set; } = true;

    public ProfileFieldVisibility AvatarVisibility { get; set; } = ProfileFieldVisibility.All;

    public ProfileFieldVisibility BioVisibility { get; set; } = ProfileFieldVisibility.All;

    public ProfileFieldVisibility EmailVisibility { get; set; } = ProfileFieldVisibility.None;

    public bool SearchVisible { get; set; } = true;

    public ProfileFieldVisibility OnlineVisibility { get; set; } = ProfileFieldVisibility.All;
}
