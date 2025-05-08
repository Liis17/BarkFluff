using BarkFluff.Client.WPF.MessagerData;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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

namespace BarkFluff.Client.WPF.Pages
{
    public partial class PincodeSecure : UserControl
    {
        private char[] pinDigits = new char[4];
        private int attempts = 3;
        public PincodeSecure()
        {
            InitializeComponent();
            Loaded += PincodeSecure_Loaded;
        }

        private void PincodeSecure_Loaded(object sender, RoutedEventArgs e)
        {
            KeysIconUpdate();
            helper.Text = "";
        }

        private void PinBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !e.Text.All(char.IsDigit);
        }

        private void PinBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            TextBox currentBox = (TextBox)sender;
            int index = GetBoxIndex(currentBox);

            if (string.IsNullOrEmpty(currentBox.Text))
            {
                pinDigits[index] = '\0';
                return;
            }

            string input = currentBox.Text;
            if (!char.IsDigit(input[0]))
                return;

            pinDigits[index] = input[0];

            currentBox.TextChanged -= PinBox_TextChanged;
            currentBox.Text = "●";
            currentBox.CaretIndex = 1;
            currentBox.TextChanged += PinBox_TextChanged;

            if (index < 3)
            {
                GetBox(index + 1).Focus();
            }
            else
            {
                string pin = new string(pinDigits);
                if (pin.All(char.IsDigit))
                {
                    if (IsValid(pin))
                    {
                        Next();
                    }
                    else
                    {
                        if (attempts > 0)
                        {
                            attempts--;
                            KeysIconUpdate();
                            ClearAll();
                            GetBox(0).Focus();
                        }
                        if(attempts == 0)
                        {
                            helper.Text = "Попытки исчерпаны. Помянем";
                            RemoveSettings(sender, e);
                        }
                        else if (attempts == 1)
                        {
                            helper.Text = "Последняя попытка ввести правильный код\nПри неудаче последует сброс...";
                        }
                        else if (attempts == 2)
                        {
                            helper.Text = "Это PIN код можно отключить в настройках";
                        }
                    }
                }
            }
        }
        private void KeysIconUpdate()
        {
            key3.Visibility = attempts >= 3 ? Visibility.Visible : Visibility.Collapsed;
            key2.Visibility = attempts >= 2 ? Visibility.Visible : Visibility.Collapsed;
            key1.Visibility = attempts >= 1 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void PinBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            TextBox currentBox = (TextBox)sender;
            int index = GetBoxIndex(currentBox);

            if (e.Key == Key.Back)
            {
                e.Handled = true;
                pinDigits[index] = '\0';
                currentBox.Clear();

                if (index > 0)
                {
                    GetBox(index - 1).Focus();
                    GetBox(index - 1).Clear();
                    pinDigits[index - 1] = '\0';
                }
            }
        }

        private void PinBox_GotFocus(object sender, RoutedEventArgs e)
        {
            for (int i = 0; i < 4; i++)
            {
                if (pinDigits[i] == '\0')
                {
                    GetBox(i).Focus();
                    return;
                }
            }
            GetBox(3).Focus();
        }

        private TextBox GetBox(int index) => (TextBox)PinContainer.Children[index];
        private int GetBoxIndex(TextBox box) => PinContainer.Children.IndexOf(box);

        private void ClearAll()
        {
            for (int i = 0; i < 4; i++)
            {
                GetBox(i).Clear();
                pinDigits[i] = '\0';
            }
        }

        private bool IsValid(string pin)
        {
            string exePath = Assembly.GetExecutingAssembly().Location;
            string exeDirectory = Path.GetDirectoryName(exePath);
            string filePath = Path.Combine(exeDirectory, "GlobalParam.json");
            if (File.Exists(filePath))
            {
                var a = GlobalParam.VerifyPassword(filePath, pin);
                return a;
            }
            else
            {
                MessageBox.Show("Файл GlobalParam.json не найден", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
            
        }
        private void Next()
        {
            string exeDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string filePath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "GlobalParam.json");
            MainWindow.GParam = GlobalParam.Load(filePath, new string(pinDigits));
            MainWindow.GParam.AppPass = new string(pinDigits);
            MainWindow.GParam.AppPath = exeDirectory ?? string.Empty;

            MainWindow.MWindow.PincodeSuccessful();
        }

        private void RemoveSettings(object sender, RoutedEventArgs e)
        {
            string filePath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "GlobalParam.json");
            if (File.Exists(filePath))
            {
                File.Delete("GlobalParam.json");
                Application.Current.Shutdown();
            }
            
            
        }

        private void FocusBoxZero(object sender, RoutedEventArgs e)
        {
            Box0.Focus();
        }
    }
}
