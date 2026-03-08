using System.Windows;
using System.Windows.Controls;

namespace BarkFluff.Client.WPF.Debugs
{
    /// <summary>
    /// Логика взаимодействия для DebugSendMessageUI.xaml
    /// </summary>
    public partial class DebugSendMessageUI : UserControl
    {
        public DebugSendMessageUI()
        {
            InitializeComponent();
        }

        private async void Button_Click(object sender, RoutedEventArgs e)
        {
            //var response = await App.ServerCommunication.SendMessage(App.GParam, ChatID.Text, 0, new WebApi.Core.MessengerData.NonSavedData.MessageModel { Text = Text.Text });
            App.ErideMessage.AddMessage("Не удалось отправть, часть кода закоментированна и больше не работает", new Services.Erida.MessageType { Type = Services.Erida.MessageType.MessageTypeEnum.Debug });
        } // отправить

        private async void Button_Click_1(object sender, RoutedEventArgs e)
        {
            //await App.ServerCommunication.SendMessage(App.GParam, "", long.Parse(UserID.Text), new WebApi.Core.MessengerData.NonSavedData.MessageModel { Text = Text.Text });
            App.ErideMessage.AddMessage("Не удалось отправть, часть кода закоментированна и больше не работает", new Services.Erida.MessageType { Type = Services.Erida.MessageType.MessageTypeEnum.Debug });
        }//отправить
    }
}
