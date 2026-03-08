namespace BarkFluff.DBEditor.Models
{
    /// <summary>
    /// Container for storing multiple saved accounts
    /// </summary>
    public class AccountsStorage
    {
        public int Version { get; set; } = 1;
        public List<SavedAccount> Accounts { get; set; } = new();
        public string? LastUsedAccountId { get; set; }
    }
}
