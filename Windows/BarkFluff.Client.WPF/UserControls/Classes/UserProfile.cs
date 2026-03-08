namespace BarkFluff.Client.WPF.UserControls.Classes
{
    public class UserProfile
    {
        public string PublicName { get; set; }
        public string Username { get; set; }
        public string Email { get; set; }
        public string UserId { get; set; }
        public DateTime? RegistrationDate { get; set; }
        public DateTime? LastSeen { get; set; }
        public bool IsOnline { get; set; }
        public string AvatarPath { get; set; }
        public BadgeInfo[] Badges { get; set; }
    }
}
