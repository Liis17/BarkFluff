using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace BarkFluff.Client.WPF.Pages.SetupPages
{
    public partial class CompletionRegistration : UserControl
    {
        private DispatcherTimer? _delayTimer;
        
        public CompletionRegistration()
        {
            InitializeComponent();
            Loaded += CompletionRegistration_Loaded;
        }

        private void CompletionRegistration_Loaded(object sender, RoutedEventArgs e)
        {
            _delayTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2),
                IsEnabled = false
            };
            _delayTimer.Tick += DelayTimer_Tick;
            
            // Start entrance animations
            StartEntranceAnimations();
        }
        
        private void StartEntranceAnimations()
        {
            // Start the fade-in slide-up animation
            if (Resources["FadeInSlideUp"] is Storyboard fadeInAnimation)
            {
                fadeInAnimation.Begin(this);
            }
            
            // Start the icon appear animation
            if (Resources["IconAppear"] is Storyboard iconAppearAnimation)
            {
                iconAppearAnimation.Begin(this);
                
                // After icon appears, start the pulse animation
                iconAppearAnimation.Completed += (s, e) =>
                {
                    if (Resources["IconPulse"] is Storyboard iconPulseAnimation)
                    {
                        iconPulseAnimation.Begin(this);
                    }
                };
            }
        }
        
        public void TimerStart()
        {
            _delayTimer?.Start();
        }
        
        private void DelayTimer_Tick(object? sender, EventArgs e)
        {
            _delayTimer?.Stop();
            App.OpenMessengerPage();
        }
    }
}
