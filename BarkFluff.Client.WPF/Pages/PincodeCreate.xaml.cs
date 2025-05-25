using BarkFluff.WebApi.Core.MessengerData;
using System;
using System.Collections.Generic;
using System.ComponentModel;
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
    public partial class PincodeCreate : UserControl
    {
        private char[] pinDigits = new char[4];
        public PincodeCreate()
        {
            InitializeComponent();
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
                    Next();
                }
            }
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
        private void Next()
        {
            App.GParam = new GlobalParam();
            App.GParam.AppPass = new string(pinDigits);
            string exeDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string filePath = Path.Combine(exeDirectory, "GlobalParam.json");
            App.GParam.IpAddress = SystemInfo.GetExternalIp();
            App.GParam.AppPath = exeDirectory;
            GlobalParam.Save(App.GParam, filePath, App.GParam.AppPass);

            //App.OpenPincodeSecure();
        }

        private void FocusBoxZero(object sender, RoutedEventArgs e)
        {
            Box0.Focus();
        }
    }
}
