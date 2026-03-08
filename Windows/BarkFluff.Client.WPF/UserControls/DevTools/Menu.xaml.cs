using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BarkFluff.Client.WPF.UserControls.DevTools
{
    /// <summary>
    /// Логика взаимодействия для Menu.xaml
    /// </summary>
    public partial class Menu : UserControl
    {
        public Menu()
        {
            InitializeComponent();
        }

        private async void GetFileLink(object sender, RoutedEventArgs e)
        {
            var response = await App.ServerCommunication.GetFile(App.GParam, FileIdText.Text);
            if (response.error.IsSuccess)
            {
                var fileLink = response.url;
                FileLinkText.Text = fileLink;
            }
            else
            {
                App.ErideMessage.AddMessage($"Failed to get file link. Status code: {response.error.ErrorCode}", new Services.Erida.MessageType { Type = Services.Erida.MessageType.MessageTypeEnum.Debug });
            }
        }

        private void OpenVideoEditor(object sender, RoutedEventArgs e)
        {
            var videoPath = VideoPathText.Text.Trim();

            if (string.IsNullOrEmpty(videoPath))
            {
                App.ErideMessage.AddMessage("Введите путь к видео файлу",
                    new Services.Erida.MessageType { Type = Services.Erida.MessageType.MessageTypeEnum.Warning });
                return;
            }

            if (!File.Exists(videoPath))
            {
                App.ErideMessage.AddMessage("Файл не найден",
                    new Services.Erida.MessageType { Type = Services.Erida.MessageType.MessageTypeEnum.Error });
                return;
            }

            var messengerPage = FindVisualParent<Pages.MessengerPage>(this);
            messengerPage?.OpenVideoEditor(videoPath);
        }

        private T? FindVisualParent<T>(DependencyObject child) where T : DependencyObject
        {
            var parent = VisualTreeHelper.GetParent(child);
            if (parent == null) return null;
            return parent is T typedParent ? typedParent : FindVisualParent<T>(parent);
        }
    }
}
