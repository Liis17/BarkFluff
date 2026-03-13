using System.Windows;
using System.Windows.Media;

namespace BarkFluff.Client.WPF.UserControls.SettingsPages
{
    /// <summary>
    /// Логика взаимодействия для DevicesSettingsPage.xaml
    /// </summary>
    public partial class DevicesSettingsPage : BaseSettingsPage
    {
        public override string Title => "Устройства";

        private string? _currentDeviceId;

        public DevicesSettingsPage()
        {
            InitializeComponent();
        }

        public override void OnNavigatedTo()
        {
            LoadDevices();
        }

        private async void LoadDevices()
        {
            var gp = App.GParam;
            if (gp == null) return;

            try
            {
                // Загрузить текущее устройство
                var (devError, currentDevice) = await App.ServerCommunication.GetCurrentDevice(gp);
                if (devError.IsSuccess && currentDevice != null)
                {
                    _currentDeviceId = currentDevice.DeviceId;
                    CurrentDeviceName.Text = !string.IsNullOrEmpty(currentDevice.CustomName)
                        ? currentDevice.CustomName
                        : currentDevice.OriginalName;
                    CurrentDeviceInfo.Text = $"{currentDevice.OperationSystem} · Сейчас";
                }

                // Загрузить все сессии
                var (sessError, sessions) = await App.ServerCommunication.GetDevicesList(gp);
                if (sessError.IsSuccess && sessions != null)
                {
                    SessionsList.Children.Clear();
                    var otherSessions = sessions.Where(s => s.DeviceId != _currentDeviceId).ToList();

                    foreach (var session in otherSessions)
                    {
                        var deviceId = session.DeviceId;
                        var border = new System.Windows.Controls.Border
                        {
                            Padding = new Thickness(16),
                            Background = (Brush)FindResource("TextBackground"),
                            CornerRadius = new CornerRadius(8),
                            Margin = new Thickness(0, 4, 0, 4)
                        };

                        var grid = new System.Windows.Controls.Grid();
                        grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                        grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = GridLength.Auto });

                        var info = new System.Windows.Controls.StackPanel();
                        var nameText = new System.Windows.Controls.TextBlock
                        {
                            Text = !string.IsNullOrEmpty(session.CustomName) ? session.CustomName : session.OriginalName,
                            Foreground = (Brush)FindResource("TitleText"),
                            FontSize = 14,
                            FontWeight = FontWeights.Medium
                        };
                        var detailText = new System.Windows.Controls.TextBlock
                        {
                            Text = $"{session.OperationSystem} · {session.AppName}",
                            Foreground = (Brush)FindResource("DescriptionText"),
                            FontSize = 12,
                            Margin = new Thickness(0, 2, 0, 0)
                        };
                        info.Children.Add(nameText);
                        info.Children.Add(detailText);

                        var terminateBtn = new Wpf.Ui.Controls.Button
                        {
                            Content = "Завершить",
                            Cursor = System.Windows.Input.Cursors.Hand,
                            VerticalAlignment = VerticalAlignment.Center,
                            FontSize = 12,
                            Tag = deviceId
                        };
                        terminateBtn.Click += TerminateSession_Click;

                        System.Windows.Controls.Grid.SetColumn(info, 0);
                        System.Windows.Controls.Grid.SetColumn(terminateBtn, 1);
                        grid.Children.Add(info);
                        grid.Children.Add(terminateBtn);

                        border.Child = grid;
                        SessionsList.Children.Add(border);
                    }

                    if (otherSessions.Count > 0)
                        TerminateAllButton.Visibility = Visibility.Visible;
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Ошибка загрузки: {ex.Message}";
            }
        }

        private async void TerminateSession_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement element || element.Tag is not string deviceId) return;

            var error = await App.ServerCommunication.RemoveActiveSession(deviceId, App.GParam);
            if (error.IsSuccess)
            {
                StatusText.Text = "Сессия завершена";
                LoadDevices();
            }
            else
            {
                StatusText.Text = $"Ошибка: {error.ErrorMessage}";
            }
        }

        private async void TerminateAll_Click(object sender, RoutedEventArgs e)
        {
            var (error, sessions) = await App.ServerCommunication.GetDevicesList(App.GParam);
            if (!error.IsSuccess || sessions == null) return;

            var others = sessions.Where(s => s.DeviceId != _currentDeviceId);
            foreach (var session in others)
            {
                await App.ServerCommunication.RemoveActiveSession(session.DeviceId, App.GParam);
            }

            StatusText.Text = "Все сессии завершены";
            LoadDevices();
        }
    }
}
