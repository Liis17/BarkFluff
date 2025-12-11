using BarkFluff.Client.WPF.Services.Erida;

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace BarkFluff.Client.WPF.Pages.SetupPages
{
    public partial class WelcomePage : UserControl
    {
        public WelcomePage()
        {
            InitializeComponent();
            Loaded += WelcomePage_Loaded;
        }

        private void WelcomePage_Loaded(object sender, RoutedEventArgs e)
        {
            StartEntranceAnimations();
        }

        private void StartEntranceAnimations()
        {
            var duration = TimeSpan.FromMilliseconds(600);
            var easing = new QuadraticEase { EasingMode = EasingMode.EaseOut };

            // Content card fade-in
            AnimateElement(ContentCard, 0, duration, easing);

            // Logo animation with slight delay
            AnimateElement(LogoImage, 100, duration, easing);

            // Title animation
            AnimateElement(TitleText, 200, duration, easing);

            // Subtitle animation
            AnimateElement(SubtitleText, 300, duration, easing);

            // Description animation
            AnimateElement(DescriptionText, 400, duration, easing);

            // Button animation
            AnimateElement(StartButton, 500, duration, easing);

            // Footer animation
            AnimateFadeOnly(FooterPanel, 600, duration, easing);
        }

        private void AnimateElement(FrameworkElement element, int delayMs, TimeSpan duration, IEasingFunction easing)
        {
            // Opacity animation
            var opacityAnimation = new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = new Duration(duration),
                EasingFunction = easing,
                BeginTime = TimeSpan.FromMilliseconds(delayMs)
            };

            // Translate Y animation (slide up effect)
            var translateAnimation = new DoubleAnimation
            {
                From = 20,
                To = 0,
                Duration = new Duration(duration),
                EasingFunction = easing,
                BeginTime = TimeSpan.FromMilliseconds(delayMs)
            };

            element.BeginAnimation(OpacityProperty, opacityAnimation);

            if (element.RenderTransform is TranslateTransform transform)
            {
                transform.BeginAnimation(TranslateTransform.YProperty, translateAnimation);
            }
        }

        private void AnimateFadeOnly(FrameworkElement element, int delayMs, TimeSpan duration, IEasingFunction easing)
        {
            var opacityAnimation = new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = new Duration(duration),
                EasingFunction = easing,
                BeginTime = TimeSpan.FromMilliseconds(delayMs)
            };

            element.BeginAnimation(OpacityProperty, opacityAnimation);
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            App.MessengerWindow.OpenCreatePinCodePage();
            App.ErideMessage.AddMessage("Добро пожаловать в BarkFluff!", new MessageType { Type = MessageType.MessageTypeEnum.Info});
        }

        private void ClickFooter(object sender, MouseButtonEventArgs e)
        {
            MessageBoxOptions options = MessageBoxOptions.DefaultDesktopOnly | MessageBoxOptions.None;
            MessageBox.Show("BarkFluff Client WPF\nVersion: " + AppVersion.VersionName + " " + AppVersion.Version, "About", MessageBoxButton.OK, MessageBoxImage.Exclamation, MessageBoxResult.OK, options);
        }

        private void ClickPrivacyPolicyAndTermsOfUse(object sender, MouseButtonEventArgs e)
        {

        }
    }
}
