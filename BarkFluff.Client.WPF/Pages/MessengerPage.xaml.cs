using BarkFluff.Client.WPF.Reactive;
using BarkFluff.Client.WPF.Services.App.Caching;
using BarkFluff.Client.WPF.UserControls;
using BarkFluff.Proto.Shared;
using BarkFluff.WebApi.Core.MessengerData;
using BarkFluff.WebApi.Core.MessengerData.NonSavedData;

using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
        private string? _currentChatAvatarFileId;
        private string? _currentUserAvatarFileId;

        public MessengerPage()
        {
            InitializeComponent();

            Loaded += MessengerPage_Loaded;
            Unloaded += MessengerPage_Unloaded;

            SubscribeToReactiveProperties();
            StartSlideDownAndFadeIn();
        }

        private void SubscribeToReactiveProperties()
        {
            IsOpenChat.PropertyChanged += IsOpenChat_PropertyChanged;
            ChatId.PropertyChanged += ChatId_PropertyChanged;
            ChatIdbyUserId.PropertyChanged += ChatIdbyUserId_PropertyChanged;
        }

        private void UnsubscribeFromReactiveProperties()
        {
            IsOpenChat.PropertyChanged -= IsOpenChat_PropertyChanged;
            ChatId.PropertyChanged -= ChatId_PropertyChanged;
            ChatIdbyUserId.PropertyChanged -= ChatIdbyUserId_PropertyChanged;
        }

        private void MessengerPage_Unloaded(object sender, RoutedEventArgs e)
        {
            UnsubscribeFromReactiveProperties();

            // Отписываемся от событий кеширования
            App.FileCacheService.FileCached -= OnFileCached;

            // Cleanup realtime service subscriptions
            CleanupRealtimeService();

            IsOpenChat?.Dispose();
            ChatId?.Dispose();
            ChatIdbyUserId?.Dispose();
        }

        private void OnFileCached(string fileId, string filePath, FileType fileType)
        {
            if (fileType != FileType.Avatar) return;

            Dispatcher.Invoke(() =>
            {
                if (fileId == _currentChatAvatarFileId)
                {
                    SetChatAvatarImage(filePath);
                }
                if (fileId == _currentUserAvatarFileId)
                {
                    SetTitleWindowAvatarImage(filePath);
                }
            });
        }

        private void SetChatAvatarImage(string imagePath)
        {
            try
            {
                ChatAvatar.ImageSource = new BitmapImage(new Uri(imagePath, UriKind.RelativeOrAbsolute));
            }
            catch { }
        }

        private void SetTitleWindowAvatarImage(string imagePath)
        {
            try
            {
                AvatarTitleWindow.ImageSource = new BitmapImage(new Uri(imagePath, UriKind.RelativeOrAbsolute));
            }
            catch { }
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
            catch { } // игнорируем ошибку если не получилось сфокусироваться на текстбоксе

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

        private async void ChatId_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (string.IsNullOrEmpty(ChatId.Value) || IsOpenChatEmpty || _openedLastMessageId == 0)
            {
                return;
            }
            ChatIdbyUserId.Value = 0;
            App.ErideMessage.AddMessage($"Загрузка сообщений чата с ID: {ChatId.Value}", new Erida { Type = MType.Debug });

            // Сначала показываем кешированные сообщения
            var cachedMessages = App.CacheManager.GetMessages(ChatId.Value, _openedLastMessageId, 50);
            if (cachedMessages != null && cachedMessages.Count > 0)
            {
                App.ErideMessage.AddMessage($"Показываем {cachedMessages.Count} кешированных сообщений", new Erida { Type = MType.Debug });
                DisplayMessages(cachedMessages);
            }

            // Затем загружаем актуальные сообщения с сервера
            var response = await App.ServerCommunication.GetMessages(App.GParam, ChatId.Value, _openedLastMessageId);
            if (!response.error.IsSuccess)
            {
                if (response.error.ErrorCode != 1)
                {
                    App.ErideMessage.AddMessage($"Ошибка при открытии чата: {response.error.ErrorMessage}", new Erida { Type = MType.Error });
                    return;
                }
            }

            if (response.messages != null && response.messages.Count > 0)
            {
                // Сохраняем сообщения в кеш
                foreach (var msg in response.messages)
                {
                    App.CacheManager.SaveMessage(ChatId.Value, TitleChat, msg, MessageOperation.Added);
                }

                DisplayMessages(response.messages);
            }
        }

        private void DisplayMessages(List<MessageModel> messages)
        {
            if (messages == null || messages.Count == 0)
            {
                return;
            }

            MessageArea.Children.Clear();
            var sortedMessages = messages.OrderBy(m => m.SentAt.ToDateTime()).ToList();

            // Группировка по дням
            var groupedMessages = sortedMessages.GroupBy(m => m.SentAt.ToDateTime().Date)
                                              .OrderBy(g => g.Key);

            foreach (var group in groupedMessages)
            {
                // Добавляем контрол с датой по центру
                var dateHeader = GetDateHeader(group.Key);
                var dateControl = new DateHeaderControl { Text = dateHeader };
                dateControl.HorizontalAlignment = HorizontalAlignment.Center;
                dateControl.Margin = new Thickness(0, 10, 0, 10);
                MessageArea.Children.Add(dateControl);

                // Добавляем сообщения группы
                foreach (var item in group)
                {
                    var owner = item.SenderId == App.GParam.UserId ? MessageBubble.MessageOwner.Me : MessageBubble.MessageOwner.Interlocutor;
                    var type = GetMessageType(item);
                    var messageItem = new MessageBubble(owner, type, item, IsGroup);
                    AddMessage(messageItem);
                }
            }
        }

        private static MessageBubble.MessageType GetMessageType(MessageModel message)
        {
            if (message.Attachments == null || message.Attachments.Count == 0)
            {
                return MessageBubble.MessageType.Text;
            }

            var firstAttachment = message.Attachments.FirstOrDefault();
            if (firstAttachment == null)
            {
                return MessageBubble.MessageType.Text;
            }

            return firstAttachment.Type switch
            {
                MessageAttachmentType.Image => MessageBubble.MessageType.Image,
                MessageAttachmentType.Video => MessageBubble.MessageType.Video,
                MessageAttachmentType.Gif => MessageBubble.MessageType.Gif,
                MessageAttachmentType.Document => MessageBubble.MessageType.Document,
                _ => MessageBubble.MessageType.Text
            };
        }

        private string GetDateHeader(DateTime date)
        {
            if (date.Date == DateTime.Today) return "Сегодня";
            if (date.Date == DateTime.Today.AddDays(-1)) return "Вчера";
            return date.ToString("dd.MM.yyyy");
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
            // Подписываемся на события кеширования
            App.FileCacheService.FileCached += OnFileCached;

            // Устанавливаем placeholder для аватарок
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

            // Выполнение задачи от протокола

            App.MessagerTask.PropertyChanged += MessagerTask_PropertyChanged;

            var task = App.MessagerTask.Value;


            if (!string.IsNullOrEmpty(task))
            {
                App.MessagerTask.Value = string.Empty;
                OpenChatViaProtocol(task);
            }
        }

        private async void MessagerTask_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            var task = App.MessagerTask.Value;
            if (!string.IsNullOrEmpty(task))
            {
                App.MessagerTask.Value = string.Empty;
                OpenChatViaProtocol(task);
            }
            else
            {
                return;
            }




        }
        #endregion

        #region Обработчка аргументов из протокола

        private async void OpenChatViaProtocol(string task)
        {
            try
            {
                var temp1 = task.Split("//");
                var temp2 = temp1[1].Replace("/", "");
                var command = temp2.Split("=")[0];
                var arg = temp2.Split("=")[1];
                if (command == "user-username")
                {
                    var result = await App.ServerCommunication.SearchUser(App.GParam, arg);
                    if (result.userList.Count > 0)
                    {
                        var user = result.userList[0];
                        IsOpenChatEmpty = true;
                        IsOpenChat.Value = true;
                        ChatIdbyUserId.Value = user.Id;
                    }
                    else
                    {
                        MessageBox.Show("Пользователь не найден");
                    }
                }
            }
            catch (Exception ex)
            {
                var a = ex.Message;
                MessageBox.Show("Ошибка при выполнении задачи из протокола");
            }
        }

        #endregion

        #region Сообщения
        private const int MESSAGE_LIMIT = 4096; // лимит символов в одном сообщении
        private string tempMessage; // временное хранение сообщения
        private List<string> attachedFiles { get; set; } = new List<string>(); //список ID прикрепленных файлов
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

                (bool, bool, string) options = new(true, isUserId, resipientId);
                var messageControl = new MessageBubble(part, options, attachedFiles);
                var message = new MessageModel
                {
                    Text = part,
                    ChatId = ChatId.Value,
                    SenderId = App.GParam.UserId,
                    SentAt = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UtcNow)
                };
                AddMessage(messageControl);
                UpdateChatWithMessage(message);
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

        public void UpdateChatWithMessage(MessageModel message)
        {
            if (message == null || string.IsNullOrEmpty(message.ChatId))
            {
                App.ErideMessage.AddMessage("Ошибка: MessageModel или ChatId пустые", new Erida { Type = MType.Error });
                return;
            }

            // Находим ChatItem с совпадающим ChatId
            ChatItem targetChatItem = null;
            foreach (var child in ChatList.Children)
            {
                if (child is ChatItem chatItem && chatItem.ChatId == message.ChatId)
                {
                    targetChatItem = chatItem;
                    break;
                }
            }

            if (targetChatItem == null)
            {
                App.ErideMessage.AddMessage($"Чат с ID {message.ChatId} не найден в ChatList", new Erida { Type = MType.Debug });
                return;
            }

            // Передаем сообщение и обновляем
            targetChatItem.TransferMessage = message;
            targetChatItem.UpdateMessage();

            // Сортировка ChatItem по времени последнего сообщения (новые сверху)
            var sortedChatItems = ChatList.Children.OfType<ChatItem>()
                .OrderByDescending(chatItem => chatItem.TransferMessage?.SentAt.ToDateTime() ?? DateTime.MinValue)
                .ToList();

            // Очищаем и перестраиваем ChatList
            ChatList.Children.Clear();
            foreach (var chatItem in sortedChatItems)
            {
                ChatList.Children.Add(chatItem);
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
        public async Task ChatUpdate()
        {
            var response = await App.ServerCommunication.GetChats(App.GParam);
            ChatList.Children.Clear(); // Очищаем список перед добавлением

            if (response.chats.Count == 0)
            {
                EmptyChatListBlock.Visibility = Visibility.Visible;
                return;
            }

            EmptyChatListBlock.Visibility = Visibility.Collapsed;

            // Сортировка чатов по времени последнего сообщения (новые выше)
            var sortedChats = response.chats
                .OrderByDescending(chat => chat.LastMessage?.SentAt.ToDateTime() ?? DateTime.MinValue)
                .ToList();

            foreach (var item in sortedChats)
            {
                if (item.IsGroupChat)
                {
                    App.ErideMessage.AddMessage($"Пропущен групповой чат {item.Title}", new Erida { Type = MType.Debug });
                    continue;
                }

                // Определяем аватар
                string avatar = string.IsNullOrEmpty(item.Picture)
                    ? "pack://application:,,,/Barkfluff.Client.WPF;component/Resources/Placeholders/userplaceholder.png"
                    : item.Picture;

                // Определяем статус чтения и заголовок
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
                long userId = membersId.FirstOrDefault(); // Возвращаем 0, если список пуст

                if (userId == 0)
                {
                    App.ErideMessage.AddMessage($"Ошибка: нет доступных userId для чата {item.Id}", new Erida { Type = MType.Error });
                    continue;
                }

                var messageItem = new ChatItem(
                    avatar,
                    title,
                    item.LastMessage?.Content.Text ?? string.Empty,
                    time: item.LastMessage?.SentAt.ToString() ?? string.Empty,
                    reading: isRead,
                    readBy: item.LastMessage?.ReadBy.ToList() ?? new List<long>(),
                    unReaded: item.CountUnread,
                    chatId: item.Id,
                    lastMessageId: item.LastMessage?.Id ?? 0,
                    isGroupChat: item.IsGroupChat,
                    userId: userId
                );

                ChatList.Children.Add(messageItem);
            }

            // Загружаем аватар текущего пользователя через кеш-сервис
            if (!string.IsNullOrEmpty(App.GParam.PictureUrl))
            {
                _currentUserAvatarFileId = FileCacheService.ExtractFileIdFromUrl(App.GParam.PictureUrl);
                var imagePath = App.FileCacheService.GetCachedFilePath(_currentUserAvatarFileId ?? string.Empty, FileType.Avatar, App.GParam.PictureUrl);
                SetTitleWindowAvatarImage(imagePath);
            }
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
                avatar = FileCacheService.DefaultPlaceholder;
            }

            if (App.GParam.UserId == response.Data.Id)
            {

                ChatTitleUsername.Text = "Избранное";
                avatar = "pack://application:,,,/Barkfluff.Client.WPF;component/Resources/Placeholders/savedplaceholder.png";
                // Для избранного используем placeholder напрямую
                ChatAvatar.ImageSource = new BitmapImage(new Uri(avatar, UriKind.RelativeOrAbsolute));
            }
            else
            {
                ChatTitleUsername.Text = $"{response.Data.FirstName} {response.Data.LastName}";
                // Загружаем аватар через кеш-сервис
                _currentChatAvatarFileId = FileCacheService.ExtractFileIdFromUrl(avatar);
                var imagePath = App.FileCacheService.GetCachedFilePath(_currentChatAvatarFileId ?? string.Empty, FileType.Avatar, avatar);
                SetChatAvatarImage(imagePath);
            }

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
        private Profile? _currentProfile = null;

        private void OpenCenterBlock(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var senderElement = sender as FrameworkElement;

            if (senderElement != null && !string.IsNullOrEmpty(senderElement.Tag?.ToString()))
            {
                var tag = senderElement.Tag.ToString();

                if (tag == "UserProfile")
                {
                    // Определяем, чей профиль открывать
                    if (senderElement.Name == "AvatarTitleWindowButton")
                    {
                        // Клик на аватар в заголовке - открываем свой профиль
                        ShowUserProfile(isCurrentUser: true);
                    }
                    else if (senderElement.Name == "ChatAvatarButton")
                    {
                        // Клик на аватар в чате - открываем профиль собеседника
                        if (ChatIdbyUserId.Value > 0)
                        {
                            ShowUserProfile(userId: ChatIdbyUserId.Value);
                        }
                    }

                    if (!isOpenCenter)
                    {
                        OpenCenterPanel();
                    }
                }
                else if (tag == "UpdateBlock")
                {
                    if (!isOpenCenter)
                    {
                        OpenCenterPanel();
                    }
                    else
                    {
                        CloseCenterPanel();
                    }
                }
            }
            else
            {
                if (!isOpenCenter)
                {
                    OpenCenterPanel();
                }
                else
                {
                    CloseCenterPanel();
                }
            }
        }

        /// <summary>
        /// Показывает профиль пользователя в центральной панели
        /// </summary>
        /// <param name="isCurrentUser">Если true, загружает профиль текущего пользователя</param>
        /// <param name="userId">ID пользователя для загрузки (если isCurrentUser = false)</param>
        private void ShowUserProfile(bool isCurrentUser = false, long userId = 0)
        {
            // Очищаем предыдущий контент
            CenterPanel.Child = null;

            // Создаем новый Profile контрол
            _currentProfile = new Profile();
            CenterPanel.Child = _currentProfile;

            if (isCurrentUser)
            {
                _currentProfile.LoadCurrentUserProfile();
            }
            else if (userId > 0)
            {
                _currentProfile.LoadUserProfile(userId);
            }
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
                // Очищаем контент при закрытии
                CenterPanel.Child = null;
                _currentProfile = null;
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

            // Subscribe to the RealtimeUpdateService events
            Services.App.RealtimeUpdateService.Instance.NewMessageReceived += OnNewMessageReceived;
            Services.App.RealtimeUpdateService.Instance.ConnectionStatusChanged += OnConnectionStatusChanged;

            // Start the realtime update service
            Services.App.RealtimeUpdateService.Instance.Start(globalParam);
        }

        private void OnNewMessageReceived(string chatId, MessageModel message)
        {
            // Update UI on the main thread
            Application.Current.Dispatcher.Invoke(() =>
            {
                // Update the chat list with the new message
                UpdateChatWithMessage(message);

                // If this message is for the currently open chat, add it to the message area
                if (!string.IsNullOrEmpty(ChatId.Value) && chatId == ChatId.Value)
                {
                    // Don't add messages we sent ourselves (they're already shown)
                    if (message.SenderId != App.GParam.UserId)
                    {
                        var owner = MessageBubble.MessageOwner.Interlocutor;
                        var type = GetMessageType(message);
                        var messageItem = new MessageBubble(owner, type, message, IsGroup);
                        AddMessage(messageItem);
                    }
                }
            });
        }

        private void OnConnectionStatusChanged(bool isConnected)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (isConnected)
                {
                    App.ErideMessage.AddMessage("Подключено к потоку обновлений", new Erida { Type = MType.Debug });
                }
                else
                {
                    App.ErideMessage.AddMessage("Отключено от потока обновлений", new Erida { Type = MType.Warning });
                }
            });
        }

        private void CleanupRealtimeService()
        {
            Services.App.RealtimeUpdateService.Instance.NewMessageReceived -= OnNewMessageReceived;
            Services.App.RealtimeUpdateService.Instance.ConnectionStatusChanged -= OnConnectionStatusChanged;
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

            GetChatInfo(userId);
            ChatIdbyUserId.Dispose();
            ChatIdbyUserId = new ReactiveLong(userId);
            App.ErideMessage.AddMessage($"Открытие чата с ID: {ChatId.Value}", new Erida { Type = MType.Debug });
        }

        private async void CloseChatButton(object sender, RoutedEventArgs e)
        {
            App.ErideMessage.AddMessage($"Закрытие чата с ID: {ChatId.Value}", new Erida { Type = MType.Debug });
            IsOpenChat.Value = false;
            _openedLastMessageId = 0;
            ChatId.Value = string.Empty;
            ChatIdbyUserId.Dispose();
            ChatIdbyUserId = new ReactiveLong(0);
        }

        #endregion
    }
}
