using BarkFluff.DBEditor.Models;
using BarkFluff.DBEditor.Services;
using BarkFluff.DBEditor.Views;

using System.Windows;

namespace BarkFluff.DBEditor
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private CredentialsService _credentialsService = new();

        private void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            MessageBox.Show($"Unhandled exception: {e.Exception.Message}\n\n{e.Exception.StackTrace}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        }

        private void OnStartup(object sender, StartupEventArgs e)
        {
            var accounts = _credentialsService.GetAllAccounts();

            if (accounts.Count == 0)
            {
                // No accounts - show login window
                ShowLoginWindow();
            }
            else
            {
                // Has accounts - show account selector
                ShowAccountSelectorWindow();
            }
        }

        public void ShowLoginWindow(SavedAccount? editingAccount = null)
        {
            var loginWindow = new LoginWindow(_credentialsService, editingAccount);
            loginWindow.Closed += (s, e) =>
            {
                // If login was successful, MainWindow was opened by the login window logic
                // If login was cancelled and no accounts exist, show login again
                if (!IsMainWindowOpen() && !HasAccounts())
                {
                    var newLoginWindow = new LoginWindow(_credentialsService, null);
                    newLoginWindow.Show();
                }
                // If login was cancelled and accounts exist, show selector
                else if (!IsMainWindowOpen() && HasAccounts())
                {
                    ShowAccountSelectorWindow();
                }
            };
            loginWindow.Show();
        }

        public void ShowAccountSelectorWindow()
        {
            // Prevent app from closing when selector closes
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            var selectorWindow = new AccountSelectorWindow(_credentialsService);
            var result = selectorWindow.ShowDialog();

            if (result == true && selectorWindow.SelectedAccount != null)
            {
                // Account selected - open main window
                OpenMainWindow(selectorWindow.SelectedAccount);
            }
            // If selector was closed without action
            else if (!IsMainWindowOpen())
            {
                if (HasAccounts())
                {
                    ShowAccountSelectorWindow();
                }
                else
                {
                    ShowLoginWindow();
                }
            }
        }

        public void ShowAccountSelectorFromMainWindow(ViewModels.MainViewModel callerViewModel)
        {
            var selectorWindow = new AccountSelectorWindow(_credentialsService);
            var mainWindow = Application.Current.Windows.OfType<MainWindow>().FirstOrDefault();

            if (mainWindow != null)
            {
                selectorWindow.Owner = mainWindow;
            }

            var result = selectorWindow.ShowDialog();

            if (result == true && selectorWindow.SelectedAccount != null)
            {
                // Switch account in existing main window
                callerViewModel.SwitchToAccount(selectorWindow.SelectedAccount);
            }
        }

        public void OpenMainWindow(SavedAccount account)
        {
            _credentialsService.UpdateLastUsed(account.Id);

            var mainWindow = new MainWindow(account, _credentialsService);
            mainWindow.Show();

            // Restore normal shutdown mode now that MainWindow is open
            ShutdownMode = ShutdownMode.OnMainWindowClose;

            // Close all non-main windows
            CloseAllWindowsExcept<MainWindow>();
        }

        private bool HasAccounts()
        {
            return _credentialsService.GetAllAccounts().Count > 0;
        }

        private bool IsMainWindowOpen()
        {
            return Application.Current.Windows.OfType<MainWindow>().Any();
        }

        private void CloseAllWindowsExcept<T>() where T : Window
        {
            foreach (Window win in Application.Current.Windows)
            {
                if (win is not T)
                {
                    win.Close();
                }
            }
        }
    }
}
