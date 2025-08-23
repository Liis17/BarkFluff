namespace BarkFluff.WebApi.Core.MessengerData.NonSavedData
{
    public class UserData
    {
        public string Username { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;

        public long Id { get; set; }
        public Google.Protobuf.WellKnownTypes.Timestamp RegistrationDate { get; set; }
        public string ProfilePictureUrl { get; set; } = string.Empty;
        public string ProfilePicturePreviewUrl { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;

    }
}
