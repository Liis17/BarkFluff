namespace BarkFluff.DBEditor.Models
{
    public class DbCredentials
    {
        public string Host { get; set; }
        public string Database { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }

        /// <summary>
        /// Converts DbCredentials to SavedAccount with a display name
        /// </summary>
        public SavedAccount ToSavedAccount(string displayName)
        {
            return new SavedAccount
            {
                DisplayName = displayName,
                Host = Host,
                Database = Database,
                Username = Username,
                Password = Password,
                CreatedAt = DateTime.UtcNow,
                LastUsed = DateTime.UtcNow
            };
        }

        /// <summary>
        /// Creates DbCredentials from a SavedAccount
        /// </summary>
        public static DbCredentials FromSavedAccount(SavedAccount account)
        {
            return new DbCredentials
            {
                Host = account.Host,
                Database = account.Database,
                Username = account.Username,
                Password = account.Password
            };
        }
    }
}
