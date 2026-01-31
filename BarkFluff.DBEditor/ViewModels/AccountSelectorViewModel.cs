using BarkFluff.DBEditor.Models;
using BarkFluff.DBEditor.Services;

using CommunityToolkit.Mvvm.ComponentModel;

using System.Collections.ObjectModel;

namespace BarkFluff.DBEditor.ViewModels
{
    public partial class AccountSelectorViewModel : ObservableObject
    {
        private readonly CredentialsService _credentialsService;

        [ObservableProperty]
        private ObservableCollection<SavedAccount> _accounts;

        [ObservableProperty]
        private SavedAccount? _lastUsedAccount;

        [ObservableProperty]
        private bool _hasAccounts;

        public AccountSelectorViewModel(CredentialsService credentialsService)
        {
            _credentialsService = credentialsService;
            _accounts = new ObservableCollection<SavedAccount>();

            LoadAccounts();
        }

        private void LoadAccounts()
        {
            var allAccounts = _credentialsService.GetAllAccounts();

            Accounts.Clear();
            foreach (var account in allAccounts)
            {
                Accounts.Add(account);
            }

            LastUsedAccount = _credentialsService.GetLastUsedAccount();
            HasAccounts = Accounts.Count > 0;
        }

        public void RefreshAccounts()
        {
            LoadAccounts();
        }
    }
}
