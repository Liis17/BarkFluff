using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace BarkFluff.Client.WPF.Pages.SetupPages
{
    public partial class CompletionRegistration : UserControl
    {
        private DispatcherTimer? _delayTimer;
        private Storyboard? _pulseAnimation;
        
        public CompletionRegistration()
        {
            InitializeComponent();
            Loaded += CompletionRegistration_Loaded;
            Unloaded += CompletionRegistration_Unloaded;
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
        
        private void CompletionRegistration_Unloaded(object sender, RoutedEventArgs e)
        {
            // Stop the pulse animation to prevent resource usage when unloaded
            StopPulseAnimation();
            
            // Clean up timer
            if (_delayTimer != null)
            {
                _delayTimer.Stop();
                _delayTimer.Tick -= DelayTimer_Tick;
            }
        }
        
        private void StartEntranceAnimations()
        {
            try
            {
                // Start the fade-in slide-up animation
                if (Resources["FadeInSlideUp"] is Storyboard fadeInAnimation)
                {
                    fadeInAnimation.Begin(this);
                }
                
                // Start the icon appear animation
                if (Resources["IconAppear"] is Storyboard iconAppearAnimation)
                {
                    iconAppearAnimation.Completed += OnIconAppearCompleted;
                    iconAppearAnimation.Begin(this);
                }
            }
            catch
            {
                // Animation failed to start - continue without animations
            }
        }
        
        private void OnIconAppearCompleted(object? sender, EventArgs e)
        {
            // Unsubscribe from the completed event to prevent memory leaks
            if (sender is Storyboard iconAppearAnimation)
            {
                iconAppearAnimation.Completed -= OnIconAppearCompleted;
            }
            
            try
            {
                // Start the pulse animation after icon appears
                if (Resources["IconPulse"] is Storyboard iconPulseAnimation)
                {
                    _pulseAnimation = iconPulseAnimation;
                    _pulseAnimation.Begin(this);
                }
            }
            catch
            {
                // Animation failed to start - continue without pulse animation
            }
        }
        
        private void StopPulseAnimation()
        {
            try
            {
                _pulseAnimation?.Stop(this);
            }
            catch
            {
                // Animation failed to stop - ignore
            }
            _pulseAnimation = null;
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
