using BarkFluff.Client.WPF.UserControls;

using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

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
                List<long> list = item.LastMessage.ReadBy.ToList();
                var messageItem = new ChatItem(avatar, item.Title, item.LastMessage.Content.Text, time: item.LastMessage.SentAt.ToString(), reading: ChatItem.ReadingStatus.None, list);
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
        #region SearchBox
        private void SearchBoxFocus(object sender, RoutedEventArgs e)
        {

        }

        private void SearchTextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            ExpandGrid();
        }

        private void ExpandGrid()
        {
            var expandAnimation = (Storyboard)FindResource("ExpandAnimation");
            expandAnimation.Begin();
        }

        private void CollapseGrid()
        {
            var collapseAnimation = (Storyboard)FindResource("CollapseAnimation");
            collapseAnimation.Begin();
        }

        public void ClearSearchAndHideResults()
        {
            SearchTextBox.Text = string.Empty;
            CollapseGrid();
        }

        private void SearchTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            // потом заменить на что то другое а то так хуева оставлять
            ClearSearchAndHideResults();
        }
        private async void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var response = await App.ServerCommunication.SearchUser(App.GParam, SearchTextBox.Text);
            SearchCollectin.Children.Clear();
            foreach (var item in response.userList)
            {
                var a = new SearchElement(item);
                SearchCollectin.Children.Add(a);
            }
        }

        #endregion

        #region боковая панель

        private bool isOpenPanel = false;
        private readonly CubicEase easingPanel = new CubicEase { EasingMode = EasingMode.EaseInOut };
        private void OpenPanelClick(object sender, RoutedEventArgs e)
        {
            if (!isOpenPanel)
                OpenPanel();
            else
                ClosePanel();
        }
        private void OverlayPanel_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            ClosePanel();
        }
        private void OpenPanel()
        {
            var anim = new ThicknessAnimation
            {
                From = new Thickness(-350, 0, 0, 0),
                To = new Thickness(0, 0, 0, 0),
                Duration = TimeSpan.FromSeconds(0.2),
                EasingFunction = easingPanel
            };
            SidePanel.BeginAnimation(MarginProperty, anim);
            OverlayPanel.Visibility = Visibility.Visible;
            isOpenPanel = true;
        }
        private void ClosePanel()
        {
            var anim = new ThicknessAnimation
            {
                From = new Thickness(0, 0, 0, 0),
                To = new Thickness(-350, 0, 0, 0),
                Duration = TimeSpan.FromSeconds(0.2),
                EasingFunction = easingPanel
            };
            SidePanel.BeginAnimation(MarginProperty, anim);
            OverlayPanel.Visibility = Visibility.Collapsed;
            isOpenPanel = false;
        }
        #endregion

        #region Центральный блок контента

        private bool isOpenCenter = false;
        private readonly CubicEase easingCenter = new CubicEase { EasingMode = EasingMode.EaseInOut };

        private void OpenCenterBlock(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (!isOpenCenter)
                OpenCenterPanel();
            else
                CloseCenterPanel();
        }
        private void OpenCenterPanel()
        {
            CenterPanel.Visibility = Visibility.Visible;
            OverlayCenter.Visibility = Visibility.Visible;

            var anim = new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = TimeSpan.FromSeconds(0.2),
                EasingFunction = easingCenter
            };
            CenterPanel.BeginAnimation(OpacityProperty, anim);
            isOpenCenter = true;
        }

        private void CloseCenterPanel()
        {
            var anim = new DoubleAnimation
            {
                From = 1,
                To = 0,
                Duration = TimeSpan.FromSeconds(0.2),
                EasingFunction = easingCenter
            };
            anim.Completed += (s, e) =>
            {
                CenterPanel.Visibility = Visibility.Collapsed;
                OverlayCenter.Visibility = Visibility.Collapsed;
            };
            CenterPanel.BeginAnimation(OpacityProperty, anim);
            isOpenCenter = false;
        }
        private void OverlayCenter_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            CloseCenterPanel();
        }

        #endregion


    }
}
