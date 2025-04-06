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
    /// <summary>
    /// Логика взаимодействия для Login.xaml
    /// </summary>
    public partial class Login : Page
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
                string password = PasswordEnter.Text; // если PasswordBox, иначе PasswordEnter.Text

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
            // Твоя логика входа
            MessageBox.Show($"Вход: {login} / {password}");
        }
    }
}
