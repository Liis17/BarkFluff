using BarkFluff.Client.WPF.Debugs;
using BarkFluff.WebApi.Core.MessengerData;

using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

using Erida = BarkFluff.Client.WPF.Services.Erida.MessageType;
using MType = BarkFluff.Client.WPF.Services.Erida.MessageType.MessageTypeEnum;

namespace BarkFluff.Client.WPF.Pages.PinCode
{
    public partial class PincodeSecure : UserControl
    {
        private char[] pinDigits = new char[4];
        private int attempts = 3;

        // Cached brushes for key indicators
        private static readonly SolidColorBrush ActiveKeyBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2D4A20"));
        private static readonly SolidColorBrush InactiveKeyBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4A2020"));

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
                            PlayShakeAnimation();
                            ClearAll();
                            GetBox(0).Focus();
                        }
                        if (attempts == 0)
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

        private void PlayShakeAnimation()
        {
            var storyboard = (Storyboard)this.Resources["ShakeAnimation"];
            storyboard.Begin();
        }

        private void KeysIconUpdate()
        {
            // Update visibility and color based on remaining attempts
            key3.Visibility = attempts >= 3 ? Visibility.Visible : Visibility.Collapsed;
            key2.Visibility = attempts >= 2 ? Visibility.Visible : Visibility.Collapsed;
            key1.Visibility = attempts >= 1 ? Visibility.Visible : Visibility.Collapsed;

            UpdateKeyIndicatorStyle(key3, attempts >= 3);
            UpdateKeyIndicatorStyle(key2, attempts >= 2);
            UpdateKeyIndicatorStyle(key1, attempts >= 1);
        }

        private void UpdateKeyIndicatorStyle(Border keyBorder, bool isActive)
        {
            keyBorder.Background = isActive ? ActiveKeyBrush : InactiveKeyBrush;
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
            string exePath = AppContext.BaseDirectory;
            string exeDirectory = Path.GetDirectoryName(exePath);
            string filePath = Path.Combine(exeDirectory, "datas", "GlobalParam.json");
            if (File.Exists(filePath))
            {
                var a = GlobalParam.VerifyPassword(filePath, pin);
                return a;
            }
            else
            {
                App.ErideMessage.AddMessage("Файл GlobalParam.json не найден", new Erida { Type = MType.Error });
                return false;
            }

        }
        private async void Next()
        {
            string exeDirectory = Path.GetDirectoryName(AppContext.BaseDirectory);
            string filePath = Path.Combine(Path.GetDirectoryName(AppContext.BaseDirectory), "datas", "GlobalParam.json");
            App.GParam = GlobalParam.Load(filePath, new string(pinDigits));
            App.GParam.AppPass = new string(pinDigits);
            App.GParam.AppPath = exeDirectory ?? string.Empty;
            App.GParam.IpAddress = await SystemInfo.GetExternalIp();

            Box0.Focusable = false;
            Box1.Focusable = false;
            Box2.Focusable = false;
            Box3.Focusable = false;

            StartSlideDownAndFadeOut(() =>
            {
                // Код, который выполнится после завершения анимации
                // Например, переход на следующую страницу
                App.MessengerWindow.PincodeSuccess();
            });
        }

        public void StartSlideDownAndFadeOut(Action value)
        {
            var storyboard = (Storyboard)this.Resources["SlideDownAndFadeOut"];
            storyboard.Completed += (s, e) => value?.Invoke();
            storyboard.Begin();
        }

        private void RemoveSettings(object sender, RoutedEventArgs e)
        {
            string filePath = Path.Combine(Path.GetDirectoryName(AppContext.BaseDirectory), "datas", "GlobalParam.json");
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

        private void ButtonStack_Loaded(object sender, RoutedEventArgs e)
        {
            if (Debugger.IsAttached)
            {
                var btn = new Button
                {
                    Content = "Открыть дебаг окно",
                    Width = 165,
                    Height = 35,
                    Margin = new Thickness(5),
                    HorizontalAlignment = HorizontalAlignment.Center,
                };

                btn.Click += OpenDebugWindow;

                ButtonStack.Children.Add(btn);
            }
        }

        private void OpenDebugWindow(object sender, RoutedEventArgs e)
        {
            var debugWindow = new DebugWindow();
            debugWindow.Show();
        }
    }
}
