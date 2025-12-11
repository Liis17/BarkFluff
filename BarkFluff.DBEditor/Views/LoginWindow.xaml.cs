using BarkFluff.DBEditor.Models;
using BarkFluff.DBEditor.Services;

using System.Windows;

using Wpf.Ui.Controls;

namespace BarkFluff.DBEditor.Views
{
    /// <summary>
    /// Логика взаимодействия для LoginWindow.xaml
    /// </summary>
    public partial class LoginWindow : FluentWindow
    {
        public LoginWindow()
        {
            InitializeComponent();
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
                // Пробуем подключиться
                var dbService = new DatabaseService(creds);
                await dbService.TestConnectionAsync();

                // Если ок - сохраняем
                CredentialsService.SaveCredentials(creds);

                // Переходим к главной программе
                ((App)Application.Current).OpenMainWindow(creds);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Connection Failed:\n{ex.Message}", "Connection Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }
    }
}
