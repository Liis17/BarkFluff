using BarkFluff.DBEditor.Models;
using BarkFluff.DBEditor.Services;

using System.Linq;
using System.Windows;

using Wpf.Ui.Controls;

namespace BarkFluff.DBEditor.Views
{
    /// <summary>
    /// Логика взаимодействия для LoginWindow.xaml
    /// </summary>
    public partial class LoginWindow : FluentWindow
    {
        private readonly CredentialsService _credentialsService;
        private readonly SavedAccount? _editingAccount;

        public SavedAccount? SuccessfullyLoggedInAccount { get; private set; }

        public LoginWindow(CredentialsService credentialsService, SavedAccount? editingAccount = null)
        {
            InitializeComponent();
            _credentialsService = credentialsService;
            _editingAccount = editingAccount;

            // If editing, populate fields
            if (_editingAccount != null)
            {
                Title = "Edit Account";
                TbDisplayName.Text = _editingAccount.DisplayName;
                TbHost.Text = _editingAccount.Host;
                TbDatabase.Text = _editingAccount.Database;
                TbUser.Text = _editingAccount.Username;
                PbPassword.Password = _editingAccount.Password;
            }
        }

        private async void BtnConnect_Click(object sender, RoutedEventArgs e)
        {
            var creds = new DbCredentials
            {
                Host = TbHost.Text,
                Database = TbDatabase.Text,
                Username = TbUser.Text,
                Password = PbPassword.Password
            };

            if (string.IsNullOrWhiteSpace(creds.Host) || string.IsNullOrWhiteSpace(creds.Database))
            {
                System.Windows.MessageBox.Show("Please fill all fields", "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                return;
            }

            try
            {
                // Test connection
                var dbService = new DatabaseService(creds);
                await dbService.TestConnectionAsync();

                // Generate display name if not provided
                string displayName = TbDisplayName.Text.Trim();
                if (string.IsNullOrWhiteSpace(displayName))
                {
                    displayName = GenerateDisplayName(creds);
                }

                // Check for duplicate names
                if (_editingAccount == null)
                {
                    var existingAccounts = _credentialsService.GetAllAccounts();
                    if (existingAccounts.Any(a => a.DisplayName.Equals(displayName, StringComparison.OrdinalIgnoreCase)))
                    {
                        var result = System.Windows.MessageBox.Show(
                            $"An account named \"{displayName}\" already exists. Use a different name?",
                            "Duplicate Name",
                            System.Windows.MessageBoxButton.YesNo,
                            System.Windows.MessageBoxImage.Warning);

                        if (result == System.Windows.MessageBoxResult.Yes)
                        {
                            displayName = $"{displayName} ({DateTime.Now:yyyyMMdd_HHmmss})";
                        }
                        else
                        {
                            return;
                        }
                    }
                }

                SavedAccount savedAccount;
                if (_editingAccount != null)
                {
                    // Update existing account
                    savedAccount = _editingAccount;
                    savedAccount.DisplayName = displayName;
                    savedAccount.Host = creds.Host;
                    savedAccount.Database = creds.Database;
                    savedAccount.Username = creds.Username;
                    savedAccount.Password = creds.Password;
                }
                else
                {
                    // Create new account
                    savedAccount = creds.ToSavedAccount(displayName);
                }

                _credentialsService.SaveAccount(savedAccount);

                // Set success and let App handle the rest
                SuccessfullyLoggedInAccount = savedAccount;
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Connection Failed:\n{ex.Message}", "Connection Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        private string GenerateDisplayName(DbCredentials creds)
        {
            // Generate a reasonable display name from the credentials
            var db = creds.Database;
            var host = creds.Host.Contains(":") ? creds.Host.Split(':')[0] : creds.Host;
            return $"{db}@{host}";
        }
    }
}
