using BarkFluff.Client.WPF.Pages.Messenger.Controllers;
using BarkFluff.Client.WPF.Reactive;
using BarkFluff.Client.WPF.UserControls;
using BarkFluff.WebApi.Core.MessengerData;
using BarkFluff.WebApi.Core.MessengerData.NonSavedData;

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

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

        public bool IsOpenChatEmpty { get; set; } = false;
        public ReactiveLong ChatIdbyUserId { get; set; } = new ReactiveLong(0);
        public bool IsGroup { get; set; } = false;

        // Контроллер онлайн-статуса текущего открытого чата
        private readonly OnlineStatusController _onlineStatus;

        // Контроллер отметки сообщений как прочитанные
        private readonly MessageReadController _read;

        // Контроллер истории, отображения сообщений и скролла
        private readonly ChatHistoryController _history;

        // Контроллер списка чатов и профиля в заголовке
        private readonly ChatListController _chatListCtrl;

        // Контроллер отправки сообщений и стикеров
        private readonly MessageSendController _send;

        // Контроллер вложений, drag&drop и paste
        private readonly AttachmentController _attachments;

        // Контроллер открытия/закрытия чатов и заголовка
        private readonly ChatOpeningController _opening;

        // Контроллер WinRT-уведомлений и обновления аватарок при кешировании
        private readonly NotificationController _notifications;

        // Контроллер real-time обновлений (новые сообщения, read receipts, статус подключения)
        private readonly RealtimeMessagesController _realtime;

        public MessengerPage()
        {
            InitializeComponent();

            _onlineStatus = new OnlineStatusController(UserOnlineTime);
            _read = new MessageReadController(this, MessageArea, ChatList);
            _history = new ChatHistoryController(this, MessageScrollViewer, MessageArea, _read);
            _chatListCtrl = new ChatListController(this, ChatList, AvatarTitleWindowButton);
            _send = new MessageSendController(this, TextForMessage, StickerPopup, StickerPickerControl, _history, _chatListCtrl);
            _attachments = new AttachmentController(this, AttachmentPreview, AttachmentOverlay, DragDropOverlay, TextForMessage, _history, _chatListCtrl);
            _opening = new ChatOpeningController(this, MessageArea, ChatList, ChatTitleUsername, ChatAvatarButton, OpenedChat, _history, _onlineStatus);
            _notifications = new NotificationController(this, ChatAvatarButton, AvatarTitleWindowButton, _chatListCtrl, _opening);
            _realtime = new RealtimeMessagesController(this, ChatList, MessageArea, ErrorConnection, _history, _chatListCtrl, _read, _notifications);

            Loaded += MessengerPage_Loaded;
            Unloaded += MessengerPage_Unloaded;

            _opening.Attach();
            StartSlideDownAndFadeIn();

            _attachments.Attach();
        }

        private void MessengerPage_Unloaded(object sender, RoutedEventArgs e)
        {
            _opening.Detach();

            // Отписываемся от событий уведомлений и кеширования аватарок
            _notifications.Detach();

            // Отписываемся от обновлений онлайн статуса
            _onlineStatus.Detach();

            // Отписываемся от событий вложений
            _attachments.Detach();

            // Отписываемся от подписок сервиса реального времени
            _realtime.Cleanup();

            // Останавливаем debounce-таймер контроллера чтения сообщений
            _read.Dispose();

            IsOpenChat?.Dispose();
            ChatId?.Dispose();
            ChatIdbyUserId?.Dispose();
        }


        #region Обработчики событий
        private async void MessengerPage_Loaded(object sender, RoutedEventArgs e)
        {
            // Подписываемся на события уведомлений и кеширования аватарок
            _notifications.Attach();

            // Подписываемся на обновления онлайн статуса
            _onlineStatus.Attach();

            //вызом метода для подписки на обновление приложения
            App.MainPageLoaded();

            // Устанавливаем placeholder для аватарок
            ChatAvatarButton.FileId = null;
            ChatAvatarButton.FileUrl = null;
            ChatAvatarButton.AvatarType = AvatarType.UserWithoutAvatar;
            AvatarTitleWindowButton.FileId = null;
            AvatarTitleWindowButton.FileUrl = null;
            AvatarTitleWindowButton.AvatarType = AvatarType.UserWithoutAvatar;

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
                App.GParam.SocketIdentity = WebApi.Core.WebApi.BuildEndpointUrl(serverInfo.Identity.Endpoint.Host, serverInfo.Identity.Endpoint.Port, serverInfo.Identity.TlsEnabled);
                App.GParam.SocketUsers = WebApi.Core.WebApi.BuildEndpointUrl(serverInfo.Users.Endpoint.Host, serverInfo.Users.Endpoint.Port, serverInfo.Users.TlsEnabled);
                App.GParam.SocketFiles = WebApi.Core.WebApi.BuildEndpointUrl(serverInfo.Files.Endpoint.Host, serverInfo.Files.Endpoint.Port, serverInfo.Files.TlsEnabled);
                App.GParam.SocketMessages = WebApi.Core.WebApi.BuildEndpointUrl(serverInfo.Messages.Endpoint.Host, serverInfo.Messages.Endpoint.Port, serverInfo.Messages.TlsEnabled);
                App.GParam.SocketUpdates = WebApi.Core.WebApi.BuildEndpointUrl(serverInfo.Updates.Endpoint.Host, serverInfo.Updates.Endpoint.Port, serverInfo.Updates.TlsEnabled);
                App.GParam.SocketOnliner = WebApi.Core.WebApi.BuildEndpointUrl(serverInfo.Onliner.Endpoint.Host, serverInfo.Onliner.Endpoint.Port, serverInfo.Onliner.TlsEnabled);
                App.GParam.SocketFastAuth = WebApi.Core.WebApi.BuildEndpointUrl(serverInfo.FastAuth.Endpoint.Host, serverInfo.FastAuth.Endpoint.Port, serverInfo.FastAuth.TlsEnabled);
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

            await UserInfoUpdate();
            ChatUpdate();
            await Task.Run(() => _realtime.Start(App.GParam));

            // Выполнение задачи от протокола

            App.MessagerTask.PropertyChanged += MessagerTask_PropertyChanged;

            var task = App.MessagerTask.Value;


            if (!string.IsNullOrEmpty(task))
            {
                App.MessagerTask.Value = string.Empty;
                OpenCommandViaProtocol(task);
            }
        }

        #endregion


        public void UpdateChatWithMessage(MessageModel message)
            => _chatListCtrl.UpdateChatWithMessage(message);

        #region Вспомогательные методы

        public Task ChatUpdate() => _chatListCtrl.ChatUpdateAsync();
        public Task UserInfoUpdate() => _chatListCtrl.UserInfoUpdateAsync();

        public void StartSlideDownAndFadeIn()
        {
            var storyboard = (Storyboard)this.Resources["SlideDownAndFadeIn"];
            storyboard.Begin();
        }

        #endregion

        #region Чаты — публичные обёртки

        public async void OpenChatFromSearch(long userId)
            => await _opening.OpenChatFromSearchAsync(userId);

        public void OpenChatById(string chatId, long lastMessageId, bool isGroupChat, long userId, string title, long firstUnreadId = 0)
            => _opening.OpenChatById(chatId, lastMessageId, isGroupChat, userId, title, firstUnreadId);

        #endregion

        #region Attachments — публичные обёртки

        public void ShowAttachmentPreview(List<string> filePaths)
            => _attachments.ShowAttachmentPreview(filePaths);

        /// <summary>
        /// Открывает чат по ID и сразу показывает превью вложений с файлами.
        /// Используется при перетаскивании файлов на ChatItem.
        /// </summary>
        public void OpenChatAndShowAttachments(string chatId, long lastMessageId, bool isGroupChat, long userId, string title, List<string> filePaths)
        {
            // Сначала открываем чат
            OpenChatById(chatId, lastMessageId, isGroupChat, userId, title, 0);

            // Затем показываем превью вложений (BeginInvoke даёт чату время открыться)
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _attachments.ShowAttachmentPreview(filePaths);
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        #endregion

    }
}
