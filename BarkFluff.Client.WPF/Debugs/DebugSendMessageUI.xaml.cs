using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

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
            var response = await App.ServerCommunication.SendMessage(App.GParam, ChatID.Text, 0, new WebApi.Core.MessengerData.NonSavedData.MessageModel { Text = Text.Text});
        } // отправить

        private async void Button_Click_1(object sender, RoutedEventArgs e)
        {
            await App.ServerCommunication.SendMessage(App.GParam, "" , long.Parse(UserID.Text), new WebApi.Core.MessengerData.NonSavedData.MessageModel { Text = Text.Text });
        }//отправить
    }
}
