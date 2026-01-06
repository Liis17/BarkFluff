using System.Windows;
using System.Windows.Controls;

namespace BarkFluff.Client.WPF.UserControls.Debug
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
    }
}
