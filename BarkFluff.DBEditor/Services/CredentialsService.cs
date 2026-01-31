using BarkFluff.DBEditor.Models;

using System.IO;
using System.Text;
using System.Text.Json;

namespace BarkFluff.DBEditor.Services
{
    /// <summary>
    /// Service for managing multiple saved database accounts
    /// </summary>
    public class CredentialsService
    {
        private readonly string _folderPath;
        private readonly string _filePath;
        private readonly string _legacyFilePath;

        public CredentialsService()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            _folderPath = Path.Combine(baseDir, "data");
            _filePath = Path.Combine(_folderPath, "accounts.json");
            _legacyFilePath = Path.Combine(_folderPath, "db_config.json");
        }

        /// <summary>
        /// Get all saved accounts
        /// </summary>
        public List<SavedAccount> GetAllAccounts()
        {
            MigrateFromOldFormatIfNeeded();

            if (!File.Exists(_filePath))
                return new List<SavedAccount>();

            try
            {
                string json = File.ReadAllText(_filePath);
                var storage = JsonSerializer.Deserialize<AccountsStorage>(json);
                return storage?.Accounts.OrderBy(a => a.LastUsed).Reverse().ToList() ?? new List<SavedAccount>();
            }
            catch
            {
                return new List<SavedAccount>();
            }
        }

        /// <summary>
        /// Save or update an account
        /// </summary>
        public void SaveAccount(SavedAccount account)
        {
            if (!Directory.Exists(_folderPath))
                Directory.CreateDirectory(_folderPath);

            var storage = LoadStorage();

            // Check if account with same ID exists
            var existingIndex = storage.Accounts.FindIndex(a => a.Id == account.Id);

            if (existingIndex >= 0)
            {
                // Update existing
                storage.Accounts[existingIndex] = account;
            }
            else
            {
                // Add new
                storage.Accounts.Add(account);
            }

            storage.LastUsedAccountId = account.Id;
            SaveStorage(storage);
        }

        /// <summary>
        /// Delete an account by ID
        /// </summary>
        public bool DeleteAccount(string id)
        {
            var storage = LoadStorage();
            var count = storage.Accounts.RemoveAll(a => a.Id == id);

            // If we deleted the last used account, clear the reference
            if (storage.LastUsedAccountId == id)
            {
                storage.LastUsedAccountId = storage.Accounts.FirstOrDefault()?.Id;
            }

            SaveStorage(storage);
            return count > 0;
        }

        /// <summary>
        /// Update the LastUsed timestamp for an account
        /// </summary>
        public void UpdateLastUsed(string id)
        {
            var storage = LoadStorage();
            var account = storage.Accounts.FirstOrDefault(a => a.Id == id);

            if (account != null)
            {
                account.LastUsed = DateTime.UtcNow;
                storage.LastUsedAccountId = id;
                SaveStorage(storage);
            }
        }

        /// <summary>
        /// Get the last used account
        /// </summary>
        public SavedAccount? GetLastUsedAccount()
        {
            var storage = LoadStorage();

            if (!string.IsNullOrEmpty(storage.LastUsedAccountId))
            {
                var account = storage.Accounts.FirstOrDefault(a => a.Id == storage.LastUsedAccountId);
                if (account != null)
                    return account;
            }

            // Fallback to most recently used
            return storage.Accounts.OrderByDescending(a => a.LastUsed).FirstOrDefault();
        }

        /// <summary>
        /// Get an account by ID
        /// </summary>
        public SavedAccount? GetAccountById(string id)
        {
            var storage = LoadStorage();
            return storage.Accounts.FirstOrDefault(a => a.Id == id);
        }

        private AccountsStorage LoadStorage()
        {
            if (!File.Exists(_filePath))
                return new AccountsStorage();

            try
            {
                string json = File.ReadAllText(_filePath);
                return JsonSerializer.Deserialize<AccountsStorage>(json) ?? new AccountsStorage();
            }
            catch
            {
                return new AccountsStorage();
            }
        }

        private void SaveStorage(AccountsStorage storage)
        {
            if (!Directory.Exists(_folderPath))
                Directory.CreateDirectory(_folderPath);

            var options = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(storage, options);
            File.WriteAllText(_filePath, json);
        }

        /// <summary>
        /// Migrate from old single-account format to new multi-account format
        /// </summary>
        private void MigrateFromOldFormatIfNeeded()
        {
            // Skip if new format exists or old format doesn't exist
            if (File.Exists(_filePath) || !File.Exists(_legacyFilePath))
                return;

            try
            {
                string base64 = File.ReadAllText(_legacyFilePath);
                string json = Encoding.UTF8.GetString(Convert.FromBase64String(base64));
                var oldCreds = JsonSerializer.Deserialize<DbCredentials>(json);

                if (oldCreds != null)
                {
                    // Create default display name
                    string displayName = $"Migrated ({oldCreds.Database})";

                    var storage = new AccountsStorage
                    {
                        Accounts = new List<SavedAccount>
                        {
                            new SavedAccount
                            {
                                Id = Guid.NewGuid().ToString(),
                                DisplayName = displayName,
                                Host = oldCreds.Host,
                                Database = oldCreds.Database,
                                Username = oldCreds.Username,
                                Password = oldCreds.Password,
                                CreatedAt = DateTime.UtcNow,
                                LastUsed = DateTime.UtcNow
                            }
                        },
                        LastUsedAccountId = null
                    };

                    SaveStorage(storage);

                    // Backup old file
                    string backupPath = _legacyFilePath + ".backup";
                    File.Move(_legacyFilePath, backupPath);
                }
            }
            catch
            {
                // If migration fails, just continue with empty storage
            }
        }
    }
}
