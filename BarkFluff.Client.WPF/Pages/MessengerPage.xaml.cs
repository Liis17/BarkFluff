using BarkFluff.Client.WPF.Reactive;
using BarkFluff.Client.WPF.UserControls;
using BarkFluff.Proto.Messages;
using BarkFluff.Proto.Shared;
using BarkFluff.WebApi.Core.MessengerData;
using BarkFluff.WebApi.Core.MessengerData.NonSavedData;

using System.ComponentModel;
using System.Security.Cryptography;
using System.Threading;
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
        public string TitleChat { get; set; } = string.Empty;
        private long _openedLastMessageId { get; set; } = 0;

        public bool IsOpenChatEmpty { get; set; } = false;
        public ReactiveLong ChatIdbyUserId { get; set; } = new ReactiveLong(0);
        public bool IsGroup { get; set; } = false;
        public MessengerPage()
        {
            InitializeComponent();

            Loaded += MessengerPage_Loaded;
            IsOpenChat.PropertyChanged += IsOpenChat_PropertyChanged;
            ChatId.PropertyChanged += ChatId_PropertyChanged;
            ChatIdbyUserId.PropertyChanged += ChatIdbyUserId_PropertyChanged;

            StartSlideDownAndFadeIn();
        }

        #region Обработчики событий
        private async void ChatIdbyUserId_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (ChatIdbyUserId.Value == 0) { return; } //если chatId пустой, то выходим из метода
            IsOpenChat.Value = true;
            IsOpenChatEmpty = true;
            IsGroup = false;
            ChatId.Value = string.Empty;
            App.ErideMessage.AddMessage($"Открытие чата с UserID: {ChatIdbyUserId.Value}", new Erida { Type = MType.Debug });
            _openedLastMessageId = 0;
            MessageArea.Children.Clear();
            GetChatInfo(ChatIdbyUserId.Value); // получаем информацию о чате для вывода в заголовке и аватара

        }
        private void TextForMessage_LostFocus(object sender, RoutedEventArgs e)
        {
            try
            {
                //var textBox = sender as TextBox;
                //textBox.Focus();
            }
            catch{ } // игнорируем ошибку если не получилось сфокусироваться на текстбоксе

        }

        private async void ChatId_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (ChatId.Value == string.Empty) { return; } //если chatId пустой, то выходим из метода
            if (IsOpenChatEmpty || _openedLastMessageId == 0) { return; } // если открываемый чат имеет тег IsOpenChatEmpty или _openedLastMessageId равен 0, то выходим из метода

            ChatIdbyUserId.Value = 0; // обнуляем chatIdbyUserId чтобы не мешал открытию других чатов

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
            //временное удаление аватарки-заглушки габена пока нет кеша 
            ChatAvatar.ImageSource = null;
            AvatarTitleWindow.ImageSource = null;

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
                App.GParam.SocketUpdates = WebApi.Core.WebApi.EnsureHttpPrefix(serverInfo.Updates.Endpoint.Host + ":" + serverInfo.Updates.Endpoint.Port);
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
            await Task.Run(() => ProcessMessages(App.GParam));
        }
        #endregion

        #region Сообщения
        const int MESSAGE_LIMIT = 4096; // лимит символов в одном сообщении
        string tempMessage; // временное хранение сообщения
        List<string> attachedFiles { get; set; } = new List<string>(); //список ID прикрепленных файлов
        private void SendMessage(object sender, RoutedEventArgs e)
        {
            tempMessage = TextForMessage.Text;
            
            if (string.IsNullOrEmpty(tempMessage)) return;

            List<string> messageParts = SplitMessage(tempMessage, MESSAGE_LIMIT);
            TextForMessage.Text = string.Empty;
            foreach (var part in messageParts)
            {
                string resipientId = "0";
                bool isUserId = false;
                if (IsOpenChatEmpty)
                {
                    resipientId = ChatIdbyUserId.Value.ToString();
                    isUserId = true;
                }
                else
                {
                    resipientId = ChatId.Value;
                    isUserId = false;
                }

                (bool, bool, string) options = new (true, isUserId, resipientId);
                var messageControl = new MessageBubble(part, options, attachedFiles);
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
        private async void ReadMessage(List<long> messageIds)
        {
            var response = await App.ServerCommunication.MarkMessageAsRead(App.GParam, messageIds);
            if (!response.IsSuccess)
            {
                App.ErideMessage.AddMessage("Ошибка при отметке сообщения как прочитанного: " + response.ErrorMessage, new Erida { Type = MType.Debug });
                return;
            }
        }
        private void TextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.LeftShift || e.Key == Key.LeftCtrl)
            {
                var textBox = sender as TextBox;
                textBox.AcceptsReturn = true;
            }
            if (e.Key == Key.Enter)
            {
                var textBox = sender as TextBox;
                tempMessage = textBox.Text;
                var a = tempMessage.Replace(" ", "");
                if (a == "")
                {
                    return;
                }
                SendMessage(sender, null);
                textBox.Text = string.Empty;
                textBox.AcceptsReturn = false;
            }
        }

        private void TextForMessage_KeyUp(object sender, KeyEventArgs e)
        {
            var textBox = sender as TextBox;
            if (e.Key == Key.LeftShift || e.Key == Key.LeftCtrl)
            {
                textBox.AcceptsReturn = false;
            }
        }
        #endregion


        #region Вспомогательные методы

        private List<string> SplitMessage(string message, int maxLength)
        {
            List<string> parts = new List<string>();
            int currentIndex = 0;

            while (currentIndex < message.Length)
            {
                int length = Math.Min(maxLength, message.Length - currentIndex);
                if (length == 0) break;

                // Проверяем, чтобы не разрывать слово
                if (currentIndex + length < message.Length)
                {
                    int lastSpace = message.LastIndexOf(' ', currentIndex + length - 1, length);
                    if (lastSpace > currentIndex)
                    {
                        length = lastSpace - currentIndex;
                    }
                }

                string part = message.Substring(currentIndex, length);
                parts.Add(part.TrimEnd());
                currentIndex += length;
            }

            return parts;
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
                if (item.IsGroupChat)
                {
                    //групповой чат

                    App.ErideMessage.AddMessage($"Пропущен групповой чат {item.Title}", new Erida { Type = MType.Debug });
                }
                else
                {
                    var avatar = item.Picture;
                    if (string.IsNullOrEmpty(avatar))
                    {
                        avatar = "pack://application:,,,/Barkfluff.Client.WPF;component/Resources/Placeholders/userplaceholder.png";
                    }
                    List<long> list = item.LastMessage.ReadBy.ToList();
                    var isRead = ChatItem.ReadingStatus.ForMe;
                    var title = item.Title;
                    var membersId = item.Members.Select(m => m.UserId).ToList();
                    if (App.GParam.UserId == item.Members[0].UserId && App.GParam.UserId == item.Members[1].UserId)
                    {
                        isRead = ChatItem.ReadingStatus.My;
                        title = "Избранное";
                        avatar = "pack://application:,,,/Barkfluff.Client.WPF;component/Resources/Placeholders/savedplaceholder.png";
                    }
                    membersId.Remove(App.GParam.UserId);
                    var messageItem = new ChatItem(avatar, title, item.LastMessage.Content.Text, time: item.LastMessage.SentAt.ToString(), reading: isRead, list, item.CountUnread, chatId: item.Id, item.LastMessage.Id, item.IsGroupChat, userId: membersId.Count > 0 ? membersId[0] : throw new InvalidOperationException("List is empty after removal"));
                    ChatList.Children.Add(messageItem);
                }
                    
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

            if (App.GParam.UserId == response.Data.Id)
            {

                ChatTitleUsername.Text = "Избранное";
                avatar = "pack://application:,,,/Barkfluff.Client.WPF;component/Resources/Placeholders/savedplaceholder.png";
            }
            else
            {
                ChatTitleUsername.Text = $"{response.Data.FirstName} {response.Data.LastName}";
            }
            ChatAvatar.ImageSource = new BitmapImage(new Uri(avatar, UriKind.RelativeOrAbsolute));
            
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
            SearchTextBox.PlaceholderText = "Введите минимум 3 символа для поиска";
            SearchResultsHeader.Text = string.Empty;
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
            SearchTextBox.PlaceholderText = "Поиск";

            // потом заменить на что то другое а то так хуева оставлять
            ClearSearchAndHideResults();
        }
        private async void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (SearchTextBox.Text.Length >= 3)
            {
                
                var response = await App.ServerCommunication.SearchUser(App.GParam, SearchTextBox.Text);
               SearchCollection.Children.Clear();
               foreach (var item in response.userList)
               {
                   var a = new SearchElement(item);
                   SearchCollection.Children.Add(a);
               }
                SearchResultsHeader.Text = "Найдено " + response.userList.Count + " результатов";
            }
            else if (SearchTextBox.Text.Length <= 2 && SearchTextBox.Text.Length >= 1)
            {
                SearchCollection.Children.Clear();
                SearchTextBox.PlaceholderText = string.Empty;
                SearchResultsHeader.Text = "Введите минимум 3 символа для поиска";
            }
            else if (SearchTextBox.Text.Length == 0)
            {
                SearchCollection.Children.Clear();
                SearchResultsHeader.Text = string.Empty;
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

        #region обновление

        public async Task ProcessMessages(GlobalParam globalParam)
        {
            App.ErideMessage.AddMessage("Запуск процесса получения обновлений...", new Erida { Type = MType.Debug });
            var (error, stream) = await App.ServerCommunication.JustUpdate(globalParam);
            if (!error.IsSuccess)
            {
                App.ErideMessage.AddMessage("Ошибка при подключении к потоку обновлений: " + error.ErrorMessage, new Erida { Type = MType.Error });
                return;
            }
            if (stream == null)
            {
                App.ErideMessage.AddMessage("Поток обновлений недоступен.", new Erida { Type = MType.Error });
                return;
            }

            await foreach (var messageEvent in stream)
            {
                // Формируем сообщение для отладки
                string messageInfo = $"Новое сообщение в чате {messageEvent.ChatId}: " +
                                    $"ID={messageEvent.Message.Id}, " +
                                    $"Отправитель={messageEvent.Message.SenderId}, " +
                                    $"Текст={messageEvent.Message.Content.Text}, " +
                                    $"Тип={(messageEvent.Message.Type == MessageContentType.System ? "Системное" : "Обычное")}, " +
                                    $"Вложения={messageEvent.Message.Content.Attachments.Count}, " +
                                    $"Отправлено={messageEvent.Message.SentAt.ToDateTime()}";

                App.ErideMessage.AddMessage(messageInfo, new Erida { Type = MType.Debug });
                var message = new MessageModel
                {
                    ChatId = messageEvent.ChatId,
                    MessageId = messageEvent.Message.Id,
                    SenderId = messageEvent.Message.SenderId,
                    Text = messageEvent.Message.Content.Text,
                    SentAt = messageEvent.Message.SentAt,
                    Attachments = messageEvent.Message.Content.Attachments
                            .Select(a => new AttachmentsModel
                            {
                                Id = a.Id,
                                Type = a.Type,
                                FileId = a.FileId,
                                PreviewUrl = a.PreviewUrl,
                                Size = a.AttachmentSize
                            }).ToList(),
                    IsSystemMessage = messageEvent.Message.Type == MessageContentType.System
                };
                App.CacheManager.SaveMessage(message.ChatId, TitleChat, message, Services.App.Caching.MessageOperation.Added);
                // Обновляем UI в главном потоке
                Application.Current.Dispatcher.Invoke(() =>
                {
                   // тут пока ничего нет, хз нужно ли вообще
                });
            }
        }

        #endregion

        #region Чаты

        public void OpenChatById(string chatId, long lastMessageId, bool isGroupChat, long userId, string title)
        {
            
            IsOpenChatEmpty = false;
            IsOpenChat.Value = true;
            TitleChat = title;
            _openedLastMessageId = lastMessageId;
            ChatId.Value = chatId;
            IsGroup = isGroupChat;
            GetChatInfo(userId); // получаем информацию о чате для вывода в заголовке и аватара
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
