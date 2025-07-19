using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace BarkFluff.Client.WPF.Pages.SetupPages
{
    public partial class CompletionRegistration : UserControl
    {
        private DispatcherTimer _delayTimer;
        public CompletionRegistration()
        {
            InitializeComponent();
            Loaded += CompletionRegistration_Loaded;
        }

        private async void CompletionRegistration_Loaded(object sender, RoutedEventArgs e)
        {
            _delayTimer = new DispatcherTimer();
            _delayTimer.Interval = TimeSpan.FromSeconds(2);
            _delayTimer.Tick += DelayTimer_Tick;
            _delayTimer.IsEnabled = false; // Изначально таймер выключен
        }
        public void TimerStart()
        {
            _delayTimer.Start();
        }
        private void DelayTimer_Tick(object sender, EventArgs e)
        {
            _delayTimer.Stop();
            App.OpenMessengerPage();
        }
    }
}
