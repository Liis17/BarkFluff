using BarkFluff.WebApi.Core.MessengerData;

using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

using Wpf.Ui.Controls;

using Erida = BarkFluff.Client.WPF.Services.Erida.MessageType;
using MType = BarkFluff.Client.WPF.Services.Erida.MessageType.MessageTypeEnum;
using TextBox = System.Windows.Controls.TextBox;

namespace BarkFluff.Client.WPF.Pages.PinCode
{
    /// <summary>
    /// Логика взаимодействия для CreatePinCodePage.xaml
    /// </summary>
    public partial class CreatePinCodePage : UserControl
    {
        private string firstPinCode = "";
        private string secondPinCode = "";

        private TextBox[] firstBoxes;
        private TextBox[] secondBoxes;
        public CreatePinCodePage()
        {
            InitializeComponent();
            firstBoxes = new TextBox[] { FirstBox0, FirstBox1, FirstBox2, FirstBox3 };
            secondBoxes = new TextBox[] { SecondBox0, SecondBox1, SecondBox2, SecondBox3 };

            foreach (var box in firstBoxes)
            {
                box.MaxLength = 1;
            }

            foreach (var box in secondBoxes)
            {
                box.MaxLength = 1;
            }

            UpdateContinueButtonState();
        }

        private void FirstPinBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            TextBox currentBox = sender as TextBox;
            int currentIndex = Array.IndexOf(firstBoxes, currentBox);

            if (!string.IsNullOrEmpty(currentBox.Text))
            {
                if (currentIndex < firstBoxes.Length - 1)
                {
                    firstBoxes[currentIndex + 1].Focus();
                }
                else
                {
                    secondBoxes[0].Focus();
                }
            }

            UpdateFirstPinCode();
            UpdateStepIndicator();
        }

        private void SecondPinBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            TextBox currentBox = sender as TextBox;
            int currentIndex = Array.IndexOf(secondBoxes, currentBox);

            if (!string.IsNullOrEmpty(currentBox.Text) && currentIndex < secondBoxes.Length - 1)
            {
                secondBoxes[currentIndex + 1].Focus();
            }

            UpdateSecondPinCode();
            UpdateStepIndicator();
        }

        private void PinBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            Regex regex = new Regex("[^0-9]+");
            e.Handled = regex.IsMatch(e.Text);
        }

        private void FirstPinBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            TextBox currentBox = sender as TextBox;
            int currentIndex = Array.IndexOf(firstBoxes, currentBox);

            if (e.Key == Key.Back)
            {
                if (string.IsNullOrEmpty(currentBox.Text) && currentIndex > 0)
                {
                    firstBoxes[currentIndex - 1].Focus();
                    firstBoxes[currentIndex - 1].CaretIndex = firstBoxes[currentIndex - 1].Text.Length;
                }
            }
        }

        private void SecondPinBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            TextBox currentBox = sender as TextBox;
            int currentIndex = Array.IndexOf(secondBoxes, currentBox);

            if (e.Key == Key.Back)
            {
                if (string.IsNullOrEmpty(currentBox.Text))
                {
                    if (currentIndex == 0 && !string.IsNullOrEmpty(firstBoxes[firstBoxes.Length - 1].Text))
                    {
                        firstBoxes[firstBoxes.Length - 1].Focus();
                        firstBoxes[firstBoxes.Length - 1].CaretIndex = firstBoxes[firstBoxes.Length - 1].Text.Length;
                    }
                    else if (currentIndex > 0)
                    {
                        secondBoxes[currentIndex - 1].Focus();
                        secondBoxes[currentIndex - 1].CaretIndex = secondBoxes[currentIndex - 1].Text.Length;
                    }
                }
            }
        }

        private void PinBox_GotFocus(object sender, RoutedEventArgs e)
        {
            TextBox focusedBox = sender as TextBox;

            int firstBoxIndex = Array.IndexOf(firstBoxes, focusedBox);
            if (firstBoxIndex != -1)
            {
                for (int i = 0; i < firstBoxIndex; i++)
                {
                    if (string.IsNullOrEmpty(firstBoxes[i].Text))
                    {
                        firstBoxes[i].Focus();
                        return;
                    }
                }
            }

            int secondBoxIndex = Array.IndexOf(secondBoxes, focusedBox);
            if (secondBoxIndex != -1)
            {
                for (int i = 0; i < firstBoxes.Length; i++)
                {
                    if (string.IsNullOrEmpty(firstBoxes[i].Text))
                    {
                        firstBoxes[i].Focus();
                        return;
                    }
                }

                for (int i = 0; i < secondBoxIndex; i++)
                {
                    if (string.IsNullOrEmpty(secondBoxes[i].Text))
                    {
                        secondBoxes[i].Focus();
                        return;
                    }
                }
            }

            UpdateStepIndicator();
        }

        private void FocusFirstBoxZero(object sender, RoutedEventArgs e)
        {
            firstBoxes[0].Focus();
        }

        private async void ContinueButton_Click(object sender, RoutedEventArgs e)
        {
            if (firstPinCode.Length == 4 && secondPinCode.Length == 4)
            {

                if (firstPinCode != secondPinCode)
                {
                    App.ErideMessage.AddMessage("Пинкоды не совпадают", new Erida { Type = MType.Warning });
                }
                else
                {
                    App.GParam.AppPass = firstPinCode;
                    App.GParam.IpAddress = await SystemInfo.GetExternalIp();
                    string filePath = Path.Combine(App.GParam.AppPath, "datas", "GlobalParam.json");
                    GlobalParam.Save(App.GParam, filePath, App.GParam.AppPass);
                    App.MessengerWindow.OpenServerListPage();
                }
            }
            else
            {
                App.ErideMessage.AddMessage("Заполните обе строки пинкода", new Erida { Type = MType.Warning });
            }
        }

        private void UpdateFirstPinCode()
        {
            firstPinCode = "";
            foreach (var box in firstBoxes)
            {
                firstPinCode += box.Text;
            }
            UpdateContinueButtonState();
        }

        private void UpdateSecondPinCode()
        {
            secondPinCode = "";
            foreach (var box in secondBoxes)
            {
                secondPinCode += box.Text;
            }
            UpdateContinueButtonState();
        }

        private void UpdateContinueButtonState()
        {
            bool isFirstComplete = firstPinCode.Length == 4;
            bool isSecondComplete = secondPinCode.Length == 4;

            if (isFirstComplete && isSecondComplete)
            {
                if (firstPinCode == secondPinCode)
                {
                    ContinueButton.Appearance = ControlAppearance.Success;
                    ContinueButton.IsEnabled = true;
                }
                else
                {
                    ContinueButton.Appearance = ControlAppearance.Danger;
                    ContinueButton.IsEnabled = true;
                }
            }
            else if (isFirstComplete || secondPinCode.Length > 0)
            {
                ContinueButton.Appearance = ControlAppearance.Secondary;
                ContinueButton.IsEnabled = false;
            }
            else
            {
                ContinueButton.Appearance = ControlAppearance.Caution;
                ContinueButton.IsEnabled = false;
            }
        }

        private void UpdateStepIndicator()
        {
            bool isFirstStepComplete = firstPinCode.Length == 4;
            bool isSecondStepActive = !string.IsNullOrEmpty(secondPinCode) || isFirstStepComplete;

            if (isSecondStepActive)
            {
                // Активируем шаг 2
                Step2Circle.Background = (Brush)FindResource("StepCircleBackground");
                Step2CircleText.Foreground = (Brush)FindResource("StepCircleForeground");

                // Делаем шаг 1 завершенным
                if (isFirstStepComplete)
                {
                    Step1Circle.Background = (Brush)FindResource("StepCircleSuccesBackground");
                    Step1CircleText.Foreground = (Brush)FindResource("StepCircleSuccesForeground");
                }
            }
            else
            {
                // Активен шаг 1
                Step1Circle.Background = (Brush)FindResource("StepCircleBackground");
                Step1CircleText.Foreground = (Brush)FindResource("StepCircleForeground");


                // Шаг 2 неактивен
                Step2Circle.Background = (Brush)FindResource("StepCircleDisableBackground");
                Step2CircleText.Foreground = (Brush)FindResource("StepCircleDisableForeground");
            }
        }
    }
}
