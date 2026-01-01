using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace BarkFluff.Client.WPF.UserControls.SettingsPages
{
    /// <summary>
    /// Логика взаимодействия для UsedSpaceProgressBar.xaml
    /// </summary>
    public partial class UsedSpaceProgressBar : UserControl
    {
        public UsedSpaceProgressBar()
        {
            InitializeComponent();
            Loaded += USPBLoaded;
        }

        private async void USPBLoaded(object sender, RoutedEventArgs e)
        {
            var userSize = await App.ServerCommunication.GetUserStorageInfoAsync(App.GParam);
            long usedBytes = userSize.totalUsedSpace;
            long totalBytes = userSize.totalSpace;
            UsedSpaceText.Text = $"{FormatBytes(usedBytes)}";
            LimitSpaceText.Text = $"{FormatBytes(totalBytes)}";
        }

        private string FormatBytes(long bytes)
        {
            // Определяем константы для вычислений (используем 1024)
            const double OneMb = 1024.0 * 1024.0;
            const double OneGb = 1024.0 * 1024.0 * 1024.0;

            // Если байтов больше или равно 1 ГБ (1024^3)
            if (bytes >= OneGb)
            {
                double result = bytes / OneGb;
                // CultureInfo.InvariantCulture гарантирует точку вместо запятой
                return result.ToString("0.##", CultureInfo.InvariantCulture) + " GB";
            }
            else
            {
                // Если меньше 1 ГБ, переводим в МБ (даже если число очень маленькое)
                double result = bytes / OneMb;
                return result.ToString("0.##", CultureInfo.InvariantCulture) + " MB";
            }
        }
    }
}
