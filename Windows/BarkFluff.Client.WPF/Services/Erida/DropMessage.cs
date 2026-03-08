using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace BarkFluff.Client.WPF.Services.Erida
{
    public class DropMessage
    {
        private readonly StackPanel _stackPanel;

        public DropMessage(StackPanel stackPanel)
        {
            _stackPanel = stackPanel;
        }

        public void AddMessage(string text, MessageType messageType)
        {
            if (text.Replace(" ", "") == string.Empty)
            {
                return;
            }
#if (!DEBUG)
            if (messageType.Type == MessageType.MessageTypeEnum.Debug)
            {
                return;
            }
#endif

            try
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    var messageControl = new UserControl
                    {
                        Content = new EridaMessage
                        {
                            Text = text,
                            MsgType = messageType
                        },
                        Opacity = 0,
                        Margin = new Thickness(5)
                    };
                    _stackPanel.Children.Add(messageControl);

                    var fadeInAnimation = new DoubleAnimation
                    {
                        From = 0,
                        To = 1,
                        Duration = TimeSpan.FromSeconds(0.5)
                    };

                    messageControl.BeginAnimation(UIElement.OpacityProperty, fadeInAnimation);

                    var fadeOutAnimation = new DoubleAnimation
                    {
                        From = 1,
                        To = 0,
                        BeginTime = TimeSpan.FromSeconds(2),
                        Duration = TimeSpan.FromSeconds(0.5)
                    };

                    fadeOutAnimation.Completed += (s, e) =>
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            _stackPanel.Children.Remove(messageControl);
                        });
                    };

                    var dispatcherTimer = new DispatcherTimer
                    {
                        Interval = TimeSpan.FromSeconds(2.5)
                    };

                    dispatcherTimer.Tick += (sender, args) =>
                    {
                        dispatcherTimer.Stop();
                        messageControl.BeginAnimation(UIElement.OpacityProperty, fadeOutAnimation);
                    };

                    dispatcherTimer.Start();
                });
            }
            catch
            {
                // Ignore exceptions to prevent crashes from UI thread issues
            }

        }
    }
}
