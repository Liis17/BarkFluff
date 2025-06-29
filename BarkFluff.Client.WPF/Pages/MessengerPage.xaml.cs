using BarkFluff.Client.WPF.UserControls;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace BarkFluff.Client.WPF.Pages
{
    /// <summary>
    /// Логика взаимодействия для MessengerPage.xaml
    /// </summary>
    public partial class MessengerPage : UserControl
    {
        public MessengerPage()
        {
            InitializeComponent();
            Loaded += MessengerPage_Loaded;
        }

        private async void MessengerPage_Loaded(object sender, RoutedEventArgs e)
        {
            StartSlideDownAndFadeIn();
            App.ServerCommunication.CreateOnlyBeaconAC(App.GParam);
            await App.ServerCommunication.GetServerInfo(App.GParam);
            App.ServerCommunication.CreateAC(App.GParam, App.GParam.MachineName, SystemInfo.GetFriendlyWindowsVersion(), AppVersion.AppName, AppVersion.Version, App.GParam.IpAddress);
            

            TitleWindow.Text = "Barkfluff";

            ChatUpdate();
            UserInfoUpdate();
        }
        public async void ChatUpdate()
        {
            var response = await App.ServerCommunication.GetChats(App.GParam);

            if (response.chats.Count == 0)
            {
                EmptyChatListBlock.Visibility = Visibility.Visible;
            }
            else
            {
                EmptyChatListBlock.Visibility = Visibility.Collapsed;
            }
            foreach (var item in response.chats)
            {
                var avatar = item.Picture;
                if (string.IsNullOrEmpty(avatar))
                {
                    avatar = "https://charlie.liis17.ru/Photoshop_TmPl02VbWB.png";
                }
                var messageItem = new ChatItem(avatar, item.Title, item.LastMessage.Content.Text, time: item.LastMessage.SentAt.ToString(), reading: ChatItem.ReadingStatus.None);
                ChatList.Children.Add(messageItem);
            }
        }
        public async void UserInfoUpdate()
        {

        }
        public void StartSlideDownAndFadeIn()
        {
            var storyboard = (Storyboard)this.Resources["SlideDownAndFadeIn"];
            storyboard.Begin();
        }
    }
}
