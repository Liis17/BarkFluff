namespace BarkFluff.WebApi.Core.MessengerData.NonSavedData
{
    public class UserData
    {
        public string Username { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;

        public long Id { get; set; }
        public System.DateTime RegistrationDate { get; set; } = System.DateTime.MinValue;
        public string Badges { get; set; } = string.Empty;
        public string ProfilePictureUrl { get; set; } = string.Empty;
        public string ProfilePicturePreviewUrl { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;

    }
}
