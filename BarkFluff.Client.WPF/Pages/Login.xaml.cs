using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace BarkFluff.Client.WPF.Pages
{
    public partial class Login : UserControl
    {
        public Login()
        {
            InitializeComponent();
        }

        private void TextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                string login = LoginEnter.Text;
                string password = PasswordEnter.Password; // если PasswordBox, иначе PasswordEnter.Text

                if (login.Length > 3 && password.Length > 8)
                {
                    e.Handled = true;
                    ProcessLogin(login, password);
                }
                else
                {
                    // Можно подсветить поля или показать сообщение
                    MessageBox.Show("Логин должен быть длиннее 3 символов, пароль — длиннее 8.");
                }
            }
        }

        private void ProcessLogin(string login, string password)
        {
            MessageBox.Show($"Вход: {login} / {password}");
        }

        private void LogIn(object sender, RoutedEventArgs e)
        {
            string login = LoginEnter.Text;
            string password = PasswordEnter.Password;

            if (login.Length > 3 && password.Length > 8)
            {
                ProcessLogin(login, password);
            }
            else
            {
                MessageBox.Show("Логин должен быть длиннее 3 символов, пароль — длиннее 8.");
            }
        }

        private void OpenNewProfilePage(object sender, RoutedEventArgs e)
        {
            MainWindow.MWindow.OpenNewProfilePage();
        }

        private void TextBlock_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is TextBlock textBlock)
            {
                textBlock.Text = MainWindow.GParam.ServerName;
            }
        }
    }
}
