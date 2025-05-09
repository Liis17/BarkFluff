using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace BarkFluff.Client.WPF.Pages
{
    /// <summary>
    /// Логика взаимодействия для Register.xaml
    /// </summary>
    public partial class Register : UserControl
    {
        private int currentStep = 0;
        private List<StackPanel> steps;

        public enum SlideDirection
        {
            Forward,
            Backward
        }
        public Register()
        {
            InitializeComponent();
            Loaded += Register_Loaded;
        }

        private void Register_Loaded(object sender, RoutedEventArgs e)
        {
            steps = new List<StackPanel> { Step1, Step2, Step3, Step4, Step5, Step6 };

            foreach (var item in steps)
            {
                item.Visibility = Visibility.Collapsed;
            }
            steps[0].Visibility = Visibility.Visible;
            UpdateNavigationButtons();
            Enter_TextChanged(sender, null);
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
                    //ProcessLogin(login, password);
                }
                else
                {
                    // Можно подсветить поля или показать сообщение
                    MessageBox.Show("Логин должен быть длиннее 3 символов, пароль — длиннее 8.");
                }
            }
        }

        private void Registration(object sender, RoutedEventArgs e)
        {
            var regData = MainWindow.IdentityAC.CreateAccountAsync(new Proto.Identity.CreateAccountRequest { FirstName = FirstNameEnter.Text, LastName = LastNameEnter.Text,  });
        }

        private void TextBlock_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is TextBlock textBlock)
            {
                textBlock.Text = MainWindow.GParam.ServerName;
            }
        }

        #region анимация и переход между шагами
        public static void AnimateTransition(UIElement fromPanel, UIElement toPanel, SlideDirection direction, double durationMs = 300)
        {
            if (fromPanel == null || toPanel == null)
                return;

            double fromOffset = direction == SlideDirection.Forward ? -500 : 500;
            double toOffset = direction == SlideDirection.Forward ? 500 : -500;

            var easing = new CubicEase { EasingMode = EasingMode.EaseInOut };

            // Подготовка новых трансформаций
            var fromTransform = new TranslateTransform();
            var toTransform = new TranslateTransform();
            fromPanel.RenderTransform = fromTransform;
            toPanel.RenderTransform = toTransform;

            // Начальные значения
            toPanel.Visibility = Visibility.Visible;
            toPanel.Opacity = 0;
            toTransform.X = toOffset;

            // Анимации сдвига и прозрачности
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(durationMs)) { EasingFunction = easing };
            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(durationMs)) { EasingFunction = easing };

            var slideOut = new DoubleAnimation(0, fromOffset, TimeSpan.FromMilliseconds(durationMs)) { EasingFunction = easing };
            var slideIn = new DoubleAnimation(toOffset, 0, TimeSpan.FromMilliseconds(durationMs)) { EasingFunction = easing };

            fadeOut.Completed += (s, e) =>
            {
                fromPanel.Visibility = Visibility.Collapsed;
                fromPanel.Opacity = 1;
                fromTransform.X = 0;
            };

            // Запуск анимаций
            fromPanel.BeginAnimation(UIElement.OpacityProperty, fadeOut);
            fromTransform.BeginAnimation(TranslateTransform.XProperty, slideOut);

            toPanel.BeginAnimation(UIElement.OpacityProperty, fadeIn);
            toTransform.BeginAnimation(TranslateTransform.XProperty, slideIn);
        }

        private void UpdateNavigationButtons()
        {
            BackButton.Visibility = currentStep > 0 ? Visibility.Visible : Visibility.Collapsed;
            NextButton.Visibility = currentStep < steps.Count - 1 ? Visibility.Visible : Visibility.Collapsed;
            CreateButton.Visibility = currentStep == steps.Count - 1 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (currentStep > 0)
            {
                AnimateTransition(steps[currentStep], steps[currentStep - 1], SlideDirection.Backward);
                currentStep--;
                UpdateNavigationButtons();
            }
        }

        private void NextButton_Click(object sender, RoutedEventArgs e)
        {
            if (currentStep < steps.Count - 1)
            {
                if (currentStep == 0)
                {
                    if(FirstNameEnter.Text != string.Empty && LastNameEnter.Text != string.Empty)
                    {
                        AnimateTransition(steps[currentStep], steps[currentStep + 1], SlideDirection.Forward);
                        currentStep++;
                        UpdateNavigationButtons();
                        Enter_TextChanged(sender,null);
                    }
                    else
                    {
                        EmptyNames.Visibility = Visibility.Visible;
                    }
                }
                else if (currentStep == 1)
                {
                    if (LoginEnter.Text != string.Empty)
                    {
                        AnimateTransition(steps[currentStep], steps[currentStep + 1], SlideDirection.Forward);
                        currentStep++;
                        UpdateNavigationButtons();
                        Enter_TextChanged(sender, null);
                    }
                    else
                    {
                        UsernameEmpty.Visibility = Visibility.Visible;
                    }
                }
                else if(currentStep == 2)
                {
                    if (EmailEnter.Text != string.Empty)
                    {
                        if (ContainsEmail(EmailEnter.Text))
                        {
                            AnimateTransition(steps[currentStep], steps[currentStep + 1], SlideDirection.Forward);
                            currentStep++;
                            UpdateNavigationButtons();
                            Enter_TextChanged(sender, null);
                        }
                        else
                        {
                            EmailIncorrect.Visibility = Visibility.Visible;
                        }
                        
                    }
                    else
                    {
                        EmailEmpty.Visibility = Visibility.Visible;
                    }
                }
                else if(currentStep == 3)
                {
                    if (VerificationCodeEnter.Text != string.Empty)
                    {
                        if (true)//проверка кода
                        {
                            AnimateTransition(steps[currentStep], steps[currentStep + 1], SlideDirection.Forward);
                            currentStep++;
                            UpdateNavigationButtons();
                            Enter_TextChanged(sender, null);
                        }
                        else
                        {
                            IncorrentCode.Visibility = Visibility.Visible;
                        }

                    }
                    else
                    {
                        UndersizedCode.Visibility = Visibility.Visible;
                    }
                }
                else if (currentStep == 4)
                {
                    if (PasswordEnter.Text != string.Empty)
                    {
                        if (true)//проверка кода
                        {
                            AnimateTransition(steps[currentStep], steps[currentStep + 1], SlideDirection.Forward);
                            currentStep++;
                            UpdateNavigationButtons();
                            Enter_TextChanged(sender, null);
                        }
                        else
                        {
                            IncorrentCode.Visibility = Visibility.Visible;
                        }

                    }
                    else
                    {
                        UndersizedCode.Visibility = Visibility.Visible;
                    }
                }

            }
        }
        private bool ContainsEmail(string input)
        {
            // Простая регулярка для определения email'а
            string pattern = @"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}";
            return Regex.IsMatch(input, pattern);
        }

        #endregion

        private void Enter_TextChanged(object sender, TextChangedEventArgs e)
        {
            EmptyNames.Visibility = Visibility.Collapsed;
            UsernameEmpty.Visibility = Visibility.Collapsed;
            EmailEmpty.Visibility = Visibility.Collapsed;
            EmailIncorrect.Visibility = Visibility.Collapsed;
            IncorrentCode.Visibility = Visibility.Collapsed;
            UndersizedCode.Visibility = Visibility.Collapsed;
            IncorrectPassword.Visibility = Visibility.Collapsed;
        }
    }
}
