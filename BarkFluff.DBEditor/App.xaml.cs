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
        private void OnStartup(object sender, StartupEventArgs e)
        {
            // Проверяем сохраненные данные
            var creds = CredentialsService.LoadCredentials();

            if (creds != null)
            {
                // Если есть данные, пробуем сразу открыть главное окно
                // В идеале тут можно сделать быструю проверку коннекта
                OpenMainWindow(creds);
            }
            else
            {
                // Если данных нет, открываем окно входа
                var loginWindow = new LoginWindow();
                loginWindow.Show();
            }
        }

        public void OpenMainWindow(Models.DbCredentials creds)
        {
            // Создаем главное окно и передаем креды
            var mainWindow = new MainWindow(creds);
            mainWindow.Show();

            // Закрываем окна входа, если они открыты
            foreach (Window win in Application.Current.Windows)
            {
                if (win is LoginWindow) win.Close();
            }
        }
    }

}
