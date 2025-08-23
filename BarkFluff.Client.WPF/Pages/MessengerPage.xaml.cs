using BarkFluff.Client.WPF.Reactive;
using BarkFluff.Client.WPF.UserControls;
using BarkFluff.Proto.Shared;
using BarkFluff.WebApi.Core.MessengerData;
using BarkFluff.WebApi.Core.MessengerData.NonSavedData;

using System.ComponentModel;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

using Erida = BarkFluff.Client.WPF.Services.Erida.MessageType;
using MType = BarkFluff.Client.WPF.Services.Erida.MessageType.MessageTypeEnum;
namespace BarkFluff.Client.WPF.Pages
{
    /// <summary>
    /// Логика взаимодействия для MessengerPage.xaml
    /// </summary>
    public partial class MessengerPage : UserControl
    {
        public ReactiveBool IsOpenChat { get; set; } = new ReactiveBool(false);
        public ReactiveString ChatId { get; set; } = new ReactiveString(string.Empty);
        private long _openedLastMessageId { get; set; } = 0;
        private string _myId { get; set; } = string.Empty;

        public bool IsOpenChatEmpty { get; set; } = false;
        public ReactiveLong ChatIdbyUserId { get; set; } = new ReactiveLong(0);
        public bool IsGroup { get; set; } = false;
        public MessengerPage()
        {
            InitializeComponent();
            _myId = App.GParam.UserId.ToString();

            Loaded += MessengerPage_Loaded;
            IsOpenChat.PropertyChanged += IsOpenChat_PropertyChanged;
            ChatId.PropertyChanged += ChatId_PropertyChanged;
            ChatIdbyUserId.PropertyChanged += ChatIdbyUserId_PropertyChanged;

            StartSlideDownAndFadeIn();
        }

        #region Обработчики событий
        private async void ChatIdbyUserId_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            IsOpenChat.Value = true;
            IsOpenChatEmpty = true;
            IsGroup = false;
            GetChatInfo(ChatIdbyUserId.Value); // получаем информацию о чате для вывода в заголовке и аватара

        }

        private async void ChatId_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (ChatId.Value == string.Empty) { return; } //если chatId пустой, то выходим из метода
            if (IsOpenChatEmpty) { return; } // если открываемый чат имеет тег IsOpenChatEmpty, то выходим из метода

            App.ErideMessage.AddMessage($"Загрузка сообщений чата с ID: {ChatId.Value}", new Erida { Type = MType.Debug });

            var response = await App.ServerCommunication.GetMessages(App.GParam, ChatId.Value, _openedLastMessageId);
            if (!response.error.IsSuccess && response.error.ErrorCode != 1)
            {
                App.ErideMessage.AddMessage("Ошибка при открытии чата" + response.error.ErrorMessage, new Erida { Type = MType.Error });
                return;
            }
            MessageArea.Children.Clear();
            foreach (var item in response.messages)
            {
                var owner = MessageBubble.MessageOwner.Me;

                if (item.SenderId != App.GParam.UserId)
                {
                    owner = MessageBubble.MessageOwner.Interlocutor;
                }
                else
                {
                    owner = MessageBubble.MessageOwner.Me;
                }

                var type = MessageBubble.MessageType.Text;

                foreach (var messageType in item.Attachments)
                {
                    if (messageType.Type == MessageAttachmentType.Image)
                    {
                        type = MessageBubble.MessageType.Image;
                    }
                    else if (messageType.Type == MessageAttachmentType.Video)
                    {
                        type = MessageBubble.MessageType.Video;
                    }
                    else if (messageType.Type == MessageAttachmentType.Gif)
                    {
                        type = MessageBubble.MessageType.Gif;
                    }
                    else if (messageType.Type == MessageAttachmentType.Document)
                    {
                        type = MessageBubble.MessageType.Document;
                    }
                }

                var messageContentType = MessageBubble.MessageContentType.Unknown;

                if (item.Type == MessageContentType.Generic)
                {
                    messageContentType = MessageBubble.MessageContentType.Generic;
                }
                else if (item.Type == MessageContentType.System)
                {
                    messageContentType = MessageBubble.MessageContentType.System;
                }

                var messageItem = new MessageBubble(owner, type, item, IsGroup);

                AddMessage(messageItem);
            }
        }

        private void IsOpenChat_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (IsOpenChat.Value)
            {
                OpenedChat.Visibility = Visibility.Visible;
            }
            else
            {
                OpenedChat.Visibility = Visibility.Collapsed;
            }
        }

        private async void MessengerPage_Loaded(object sender, RoutedEventArgs e)
        {

            OpenedChat.Visibility = Visibility.Collapsed;
            App.ServerCommunication.CreateOnlyBeaconAC(App.GParam);
            var (error, serverInfo) = await App.ServerCommunication.GetServerInfo(App.GParam);
            if (!error.IsSuccess)
            {
                App.ErideMessage.AddMessage(error.ErrorMessage, new Erida { Type = MType.Error });
                return;
            }
            else
            {
                App.ErideMessage.AddMessage("Получена информация о сервере", new Erida { Type = MType.Debug });

                App.GParam.ServerName = serverInfo.Name;
                App.GParam.ServerDescription = serverInfo.Description;
                App.GParam.SocketIdentity = WebApi.Core.WebApi.EnsureHttpPrefix(serverInfo.Identity.Endpoint.Host + ":" + serverInfo.Identity.Endpoint.Port);
                App.GParam.SocketUsers = WebApi.Core.WebApi.EnsureHttpPrefix(serverInfo.Users.Endpoint.Host + ":" + serverInfo.Users.Endpoint.Port);
                App.GParam.SocketFiles = WebApi.Core.WebApi.EnsureHttpPrefix(serverInfo.Files.Endpoint.Host + ":" + serverInfo.Files.Endpoint.Port);
                App.GParam.SocketMessages = WebApi.Core.WebApi.EnsureHttpPrefix(serverInfo.Messages.Endpoint.Host + ":" + serverInfo.Messages.Endpoint.Port);
                App.GParam.Colors = new ClientColors()
                {
                    LiteHex = serverInfo.Color.LiteHex,
                    MainHex = serverInfo.Color.MainHex,
                    HardHex = serverInfo.Color.HardHex,
                };
                MainWindow.SaveSettings();

            }



            var response = App.ServerCommunication.CreateAC(App.GParam, App.GParam.MachineName, SystemInfo.GetFriendlyWindowsVersion(), AppVersion.AppName, AppVersion.Version, App.GParam.IpAddress);
            if (!response.IsSuccess)
            {
                App.ErideMessage.AddMessage(response.ErrorMessage ?? "Неизвестная проблема", new Erida { Type = MType.Error });
                return;
            }
            else
            {
                App.ErideMessage.AddMessage("API клиент успешно обновлён", new Erida { Type = MType.Debug });
            }

            TitleWindow.Text = "Barkfluff";

            UserInfoUpdate();
            ChatUpdate();
        }
        #endregion

        #region Сообщения
        string tempMessage;
        private void SendMessage(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(tempMessage))
            {
                var messageControl = new MessageBubble(tempMessage);
                AddMessage(messageControl);
            }
        }
        private void AddMessage(UserControl control)
        {
            tempMessage = string.Empty;
            MessageArea.Children.Add(control);

            var animation = (Storyboard)FindResource("MessageAppearAnimation");
            Storyboard.SetTarget(animation, control);
            animation.Begin();

            MessageScrollViewer.ScrollToEnd();
        }
        private void TextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                var textBox = sender as TextBox;
                tempMessage = textBox.Text;
                SendMessage(sender, null);
                textBox.Text = string.Empty;
                e.Handled = true;
            }
            else if (e.Key == Key.Enter && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                var textBox = sender as TextBox;
                textBox.Text += Environment.NewLine;
                textBox.CaretIndex = textBox.Text.Length;
                e.Handled = true;
            }
        }
        #endregion


        #region Вспомогательные методы
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
                    avatar = "pack://application:,,,/Barkfluff.Client.WPF;component/Resources/Placeholders/userplaceholder.png";
                }
                List<long> list = item.LastMessage.ReadBy.ToList();
                var isRead = ChatItem.ReadingStatus.ForMe;
                var title = item.Title;
                if (App.GParam.UserId == item.Members[0].UserId && App.GParam.UserId == item.Members[1].UserId)
                {
                    isRead = ChatItem.ReadingStatus.My;
                    title = "Избранное";
                    avatar = "pack://application:,,,/Barkfluff.Client.WPF;component/Resources/Placeholders/savedplaceholder.png";
                }
                var messageItem = new ChatItem(avatar, title, item.LastMessage.Content.Text, time: item.LastMessage.SentAt.ToString(), reading: isRead, list, item.CountUnread, chatId: item.Id, item.LastMessage.Id, item.IsGroupChat);
                ChatList.Children.Add(messageItem);
            }
            AvatarTitleWindow.ImageSource = new BitmapImage(new Uri(App.GParam.PictureUrl, UriKind.RelativeOrAbsolute));
        }
        public async void UserInfoUpdate()
        {
            var response = await App.ServerCommunication.GetUserData(App.GParam);

            App.GParam.UserId = response.Data.Id;
            App.GParam.UserName = response.Data.Username;
            App.GParam.FirstName = response.Data.FirstName;
            App.GParam.LastName = response.Data.LastName;
            App.GParam.Description = response.Data.Description;
            App.GParam.PictureUrl = response.Data.ProfilePictureUrl;
            // App.GParam.PictureId = response.Data.PictureId; // славик переделай
            MainWindow.SaveSettings();


        }
        public void StartSlideDownAndFadeIn()
        {
            var storyboard = (Storyboard)this.Resources["SlideDownAndFadeIn"];
            storyboard.Begin();
        }

        public async void GetChatInfo(long userId)
        {
            var response = await App.ServerCommunication.GetUserData(App.GParam, userId);
            var avatar = string.Empty;
            if (!string.IsNullOrEmpty(response.Data.ProfilePictureUrl))
            {
                avatar = response.Data.ProfilePictureUrl;
            }
            if (!string.IsNullOrEmpty(response.Data.ProfilePicturePreviewUrl))
            {
                avatar = response.Data.ProfilePicturePreviewUrl;
            }
            else
            {
                avatar = "pack://application:,,,/Barkfluff.Client.WPF;component/Resources/Placeholders/userplaceholder.png";
            }
            ChatAvatar.ImageSource = new BitmapImage(new Uri(avatar, UriKind.RelativeOrAbsolute));
            ChatTitleUsername.Text = $"{response.Data.FirstName} {response.Data.LastName}";
        }
        #endregion


        #region SearchBox

        private void SearchBoxFocus(object sender, RoutedEventArgs e)
        {

        }
        public void ChatListFadeIn()
        {
            var storyboard = new Storyboard();
            var opacityAnimation = new DoubleAnimation
            {
                From = 0.0,
                To = 1.0,
                Duration = new Duration(TimeSpan.FromMilliseconds(500)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
            };
            Storyboard.SetTarget(opacityAnimation, ChatList);
            Storyboard.SetTargetProperty(opacityAnimation, new PropertyPath("Opacity"));
            storyboard.Children.Add(opacityAnimation);
            storyboard.Begin();
        }

        public void ChatListFadeOut()
        {
            var storyboard = new Storyboard();
            var opacityAnimation = new DoubleAnimation
            {
                From = 1.0,
                To = 0.0,
                Duration = new Duration(TimeSpan.FromMilliseconds(150)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
            };
            Storyboard.SetTarget(opacityAnimation, ChatList);
            Storyboard.SetTargetProperty(opacityAnimation, new PropertyPath("Opacity"));
            storyboard.Children.Add(opacityAnimation);
            storyboard.Begin();
        }
        private void SearchTextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            ExpandGrid();
        }

        private void ExpandGrid()
        {
            var expandAnimation = (Storyboard)FindResource("ExpandAnimation");
            expandAnimation.Begin();
            ChatListFadeOut();
        }

        private void CollapseGrid()
        {
            var collapseAnimation = (Storyboard)FindResource("CollapseAnimation");
            collapseAnimation.Begin();
            ChatListFadeIn();
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
        private void SidePanel_Loaded(object sender, RoutedEventArgs e)
        {
            SidePanel.Children.Clear();
            var sideBar = new SideBar();
            SidePanel.Children.Add(sideBar);
        }
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

        

        #region Чаты

        public void OpenChatById(string chatId, long lastMessageId, bool isGroupChat)
        {
            IsOpenChatEmpty = false;
            IsOpenChat.Value = true;
            _openedLastMessageId = lastMessageId;
            ChatId.Value = chatId;
            IsGroup = isGroupChat;
            App.ErideMessage.AddMessage($"Открытие чата с ID: {ChatId.Value}", new Erida { Type = MType.Debug });
        }

        private async void CloseChatButton(object sender, RoutedEventArgs e)
        {
            App.ErideMessage.AddMessage($"Закрытие чата с ID: {ChatId.Value}", new Erida { Type = MType.Debug });
            IsOpenChat.Value = false;
            _openedLastMessageId = 0;   
            ChatId.Value = string.Empty;
        }
        #endregion

        
    }
}
