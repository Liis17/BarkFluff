using BarkFluff.DBEditor.Models;
using BarkFluff.DBEditor.Services;
using BarkFluff.DBEditor.ViewModels;

using System.Windows;

namespace BarkFluff.DBEditor.Views
{
    public partial class AccountSelectorWindow : Wpf.Ui.Controls.FluentWindow
    {
        public AccountSelectorViewModel ViewModel { get; }
        public SavedAccount? SelectedAccount { get; private set; }

        private readonly CredentialsService _credentialsService;

        public AccountSelectorWindow(CredentialsService credentialsService)
        {
            InitializeComponent();
            _credentialsService = credentialsService;
            ViewModel = new AccountSelectorViewModel(credentialsService);
            DataContext = this;

            UpdateQuickLoginVisibility();
        }

        private void UpdateQuickLoginVisibility()
        {
            QuickLoginButton.Visibility = ViewModel.LastUsedAccount != null
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void QuickLoginButton_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel.LastUsedAccount != null)
            {
                SelectedAccount = ViewModel.LastUsedAccount;
                DialogResult = true;
                Close();
            }
        }

        private void AccountSelectButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.FrameworkElement element && element.Tag is string accountId)
            {
                var account = ViewModel.Accounts.FirstOrDefault(a => a.Id == accountId);
                if (account != null)
                {
                    SelectedAccount = account;
                    DialogResult = true;
                    Close();
                }
            }
        }

        private void EditButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.FrameworkElement element && element.Tag is string accountId)
            {
                var account = ViewModel.Accounts.FirstOrDefault(a => a.Id == accountId);
                if (account != null)
                {
                    ShowLoginWindowForEdit(account);
                }
            }
        }

        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.FrameworkElement element && element.Tag is string accountId)
            {
                var account = ViewModel.Accounts.FirstOrDefault(a => a.Id == accountId);
                if (account != null)
                {
                    var result = System.Windows.MessageBox.Show(
                        $"Are you sure you want to delete account \"{account.DisplayName}\"?",
                        "Confirm Delete",
                        System.Windows.MessageBoxButton.YesNo,
                        System.Windows.MessageBoxImage.Question);

                    if (result == System.Windows.MessageBoxResult.Yes)
                    {
                        _credentialsService.DeleteAccount(account.Id);
                        ViewModel.RefreshAccounts();
                        UpdateQuickLoginVisibility();

                        // If no accounts left, show login window
                        if (ViewModel.Accounts.Count == 0)
                        {
                            ShowLoginWindowForNewAccount();
                        }
                    }
                }
            }
        }

        private void AddNewAccountButton_Click(object sender, RoutedEventArgs e)
        {
            ShowLoginWindowForNewAccount();
        }

        private void ShowLoginWindowForNewAccount()
        {
            var loginWindow = new LoginWindow(_credentialsService, null);
            loginWindow.Owner = this;
            var result = loginWindow.ShowDialog();

            if (result == true && loginWindow.SuccessfullyLoggedInAccount != null)
            {
                // Login successful - select this account
                SelectedAccount = loginWindow.SuccessfullyLoggedInAccount;
                DialogResult = true;
                Close();
            }
        }

        private void ShowLoginWindowForEdit(SavedAccount account)
        {
            var loginWindow = new LoginWindow(_credentialsService, account);
            loginWindow.Owner = this;
            var result = loginWindow.ShowDialog();

            if (result == true && loginWindow.SuccessfullyLoggedInAccount != null)
            {
                // Edit successful - select this account
                SelectedAccount = loginWindow.SuccessfullyLoggedInAccount;
                DialogResult = true;
                Close();
            }
        }
    }
}
