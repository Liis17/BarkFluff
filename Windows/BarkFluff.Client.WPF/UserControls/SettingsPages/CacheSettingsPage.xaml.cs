using System.Globalization;
using System.IO;
using System.Windows;

namespace BarkFluff.Client.WPF.UserControls.SettingsPages
{
    /// <summary>
    /// Логика взаимодействия для CacheSettingsPage.xaml
    /// </summary>
    public partial class CacheSettingsPage : BaseSettingsPage
    {
        public override string Title => "Кеш";

        public CacheSettingsPage()
        {
            InitializeComponent();
        }

        public override void OnNavigatedTo()
        {
            CalculateCacheSize();
        }

        private void CalculateCacheSize()
        {
            try
            {
                var cachePath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "BarkFluff", "Cache");

                if (Directory.Exists(cachePath))
                {
                    long totalSize = Directory.GetFiles(cachePath, "*", SearchOption.AllDirectories)
                        .Sum(f => new FileInfo(f).Length);
                    CacheSizeText.Text = FormatBytes(totalSize);
                }
                else
                {
                    CacheSizeText.Text = "0 MB";
                }
            }
            catch
            {
                CacheSizeText.Text = "Не удалось подсчитать";
            }
        }

        private void ClearCache_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                App.FileCacheService?.ClearCache();
                StatusText.Text = "Кеш очищен";
                CalculateCacheSize();
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Ошибка: {ex.Message}";
            }
        }

        private static string FormatBytes(long bytes)
        {
            const double OneMb = 1024.0 * 1024.0;
            const double OneGb = 1024.0 * 1024.0 * 1024.0;

            if (bytes >= OneGb)
                return (bytes / OneGb).ToString("0.##", CultureInfo.InvariantCulture) + " GB";
            return (bytes / OneMb).ToString("0.##", CultureInfo.InvariantCulture) + " MB";
        }
    }
}
