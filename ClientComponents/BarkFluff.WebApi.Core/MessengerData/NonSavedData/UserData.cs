namespace BarkFluff.WebApi.Core.MessengerData.NonSavedData
{
    public class UserData
    {
        public string Username { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string Email { get; set; }

        public long Id { get; set; }
        public Google.Protobuf.WellKnownTypes.Timestamp RegistrationDate { get; set; }
        public string ProfilePictureUrl { get; set; }

    }
}
