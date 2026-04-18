using BarkFluff.Proto.Updates;
using BarkFluff.WebApi.Core.Managers;
using BarkFluff.WebApi.Core.MessengerData;
using BarkFluff.WebApi.Core.MessengerData.NonSavedData;

using Grpc.Net.Client;

namespace BarkFluff.WebApi.Core
{
    public class WebApi : IDisposable
    {
        private bool _disposed = false;

        /// <summary>
        /// Событие вызывается когда refresh-токен стал недействителен (отозван, заблокирован через админ-панель, истёк срок действия).
        /// Приложение должно перенаправить пользователя на страницу выбора сервера для повторной авторизации.
        /// </summary>
        public event EventHandler? TokenInvalidated
        {
            add => TokenManager.TokenInvalidated += value;
            remove => TokenManager.TokenInvalidated -= value;
        }

        #region ApiClients (internal для доступа менеджеров)
        internal BarkFluff.Proto.Users.UsersApi.UsersApiClient? UsersAC;
        internal BarkFluff.Proto.Beacon.BeaconApi.BeaconApiClient? BeaconAC;
        internal BarkFluff.Proto.Identity.IdentityApi.IdentityApiClient? IdentityAC;
        internal BarkFluff.Proto.Files.FilesApi.FilesApiClient? FilesAC;
        internal BarkFluff.Proto.Messages.MessagesApi.MessagesApiClient? MessagesAC;
        internal BarkFluff.Proto.Navigator.NavigatorApi.NavigatorApiClient? NavigatorAC;
        internal BarkFluff.Proto.Updates.UpdatesApi.UpdatesApiClient? UpdatesAC;
        internal BarkFluff.Proto.Onliner.OnlinerApi.OnlinerApiClient? OnlinerAC;
        #endregion

        #region gRPC Channels (internal для доступа менеджеров)
        internal GrpcChannel? BeaconChannel;
        internal GrpcChannel? UserChannel;
        internal GrpcChannel? IdentityChannel;
        internal GrpcChannel? FilesChannel;
        internal GrpcChannel? MessagesChannel;
        internal GrpcChannel? NavigatorChannel;
        internal GrpcChannel? UpdatesChannel;
        internal GrpcChannel? OnlinerChannel;
        #endregion

        #region Менеджеры
        internal readonly WebApiClientManager ClientManager;
        internal readonly WebApiTokenManager TokenManager;
        internal readonly WebApiServerManager ServerManager;
        internal readonly WebApiUserManager UserManager;
        internal readonly WebApiAuthManager AuthManager;
        internal readonly WebApiRegistrationManager RegistrationManager;
        internal readonly WebApiPasswordManager PasswordManager;
        internal readonly WebApiMessageManager MessageManager;
        internal readonly WebApiSearchManager SearchManager;
        internal readonly WebApiFileManager FileManager;
        internal readonly WebApiUpdateManager UpdateManager;
        internal readonly WebApiOnlinerManager OnlinerManager;
        #endregion

        public bool ACisnull => UsersAC == null || BeaconAC == null || IdentityAC == null || FilesAC == null || MessagesAC == null || UpdatesAC == null;
        public bool BeaconIsnull => BeaconAC == null;

        public WebApi()
        {
            ClientManager = new WebApiClientManager(this);
            TokenManager = new WebApiTokenManager(this);
            ServerManager = new WebApiServerManager(this);
            UserManager = new WebApiUserManager(this);
            AuthManager = new WebApiAuthManager(this);
            RegistrationManager = new WebApiRegistrationManager(this);
            PasswordManager = new WebApiPasswordManager(this);
            MessageManager = new WebApiMessageManager(this);
            SearchManager = new WebApiSearchManager(this);
            FileManager = new WebApiFileManager(this);
            UpdateManager = new WebApiUpdateManager(this);
            OnlinerManager = new WebApiOnlinerManager(this);
        }

        #region IDisposable
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;
            if (disposing)
            {
                BeaconChannel?.Dispose();
                UserChannel?.Dispose();
                IdentityChannel?.Dispose();
                FilesChannel?.Dispose();
                MessagesChannel?.Dispose();
                NavigatorChannel?.Dispose();
                UpdatesChannel?.Dispose();
                OnlinerChannel?.Dispose();
            }
            _disposed = true;
        }
        #endregion

        #region Создание клиентов (делегирование к ClientManager)
        public ErrorReturner CreateOnlyBeaconAC(GlobalParam gParam) => ClientManager.CreateOnlyBeaconAC(gParam);
        public ErrorReturner CreateNavigatorAC(string navigatorUrl = "https://navigator.barkfluff.com:443") => ClientManager.CreateNavigatorAC(navigatorUrl);
        public ErrorReturner CreateAC(GlobalParam gParam, string deviceName, string os, string appName, string appVersion, string ip) => ClientManager.CreateAC(gParam, deviceName, os, appName, appVersion, ip);
        #endregion

        #region Работа с токенами (делегирование к TokenManager)
        public async Task<(ErrorReturner, string)> TokenUpdate(GlobalParam globalParam) => await TokenManager.TokenUpdate(globalParam);
        public async Task<TResponse> SafeCallAsync<TResponse>(Func<Task<TResponse>> apiCall, GlobalParam globalParam) => await TokenManager.SafeCallAsync(apiCall, globalParam);
        /// <summary>
        /// Проверяет срок действия токена и обновляет его при необходимости.
        /// Используется перед переподключением streaming соединений.
        /// </summary>
        public async Task<ErrorReturner> EnsureTokenValidAsync(GlobalParam globalParam, int bufferMinutes = 5)
            => await TokenManager.EnsureTokenValidAsync(globalParam, bufferMinutes);
        /// <summary>
        /// Принудительно обновляет токен и переинициализирует клиентов.
        /// Используется когда известно, что токен недействителен.
        /// </summary>
        public async Task<ErrorReturner> ForceRefreshTokenAsync(GlobalParam globalParam)
            => await TokenManager.ForceRefreshTokenAsync(globalParam);
        #endregion

        #region Обслуживание
        /// <summary>
        /// Добавляет http:// или https:// к URL, если он не начинается с них.
        /// </summary>
        public static string EnsureHttpPrefix(string _url)
        {
            return !_url.StartsWith("http://") && !_url.StartsWith("https://")
                   ? "http://" + _url
                   : _url;
        }
        #endregion

        #region Получение информации о сервере (делегирование к ServerManager)
        public async Task<(ErrorReturner error, Proto.Beacon.GetServerInfoResponse?)> GetServerInfo(GlobalParam param) => await ServerManager.GetServerInfo(param);
        public async Task<(ErrorReturner, List<ServerDataElement> ServerElements)> GetServerList(GlobalParam global) => await ServerManager.GetServerList(global);
        #endregion

        #region Работа с пользователями (делегирование к UserManager)
        public async Task<ErrorReturner> ChangeBio(string bio, GlobalParam globalParam) => await UserManager.ChangeBio(bio, globalParam);
        public async Task<ErrorReturner> ChangeUsername(string username, GlobalParam globalParam) => await UserManager.ChangeUsername(username, globalParam);
        public async Task<ErrorReturner> ChangeName(string firstName, string lastName, GlobalParam globalParam) => await UserManager.ChangeName(firstName, lastName, globalParam);
        public async Task<(ErrorReturner error, bool exists)> CheckEmail(string email, GlobalParam globalParam) => await UserManager.CheckEmail(email, globalParam);
        public async Task<(ErrorReturner error, bool exists)> CheckUsername(string username, GlobalParam globalParam) => await UserManager.CheckUsername(username, globalParam);
        public async Task<(ErrorReturner error, List<Proto.Identity.GetActiveSessionsResponse.Types.Session>? sessions)> GetDevicesList(GlobalParam globalParam) => await UserManager.GetDevicesList(globalParam);
        public async Task<(ErrorReturner error, List<Proto.Users.Device>? devices)> GetDevices(GlobalParam globalParam) => await UserManager.GetDevices(globalParam);
        public async Task<(ErrorReturner error, Proto.Users.Device? device)> GetCurrentDevice(GlobalParam globalParam) => await UserManager.GetCurrentDevice(globalParam);
        public async Task<ErrorReturner> RenameDevice(string deviceId, string customName, GlobalParam globalParam) => await UserManager.RenameDevice(deviceId, customName, globalParam);
        public async Task<ErrorReturner> RemoveActiveSession(string deviceId, GlobalParam globalParam) => await UserManager.RemoveActiveSession(deviceId, globalParam);
        public async Task<(ErrorReturner, string?)> GetUserAvatar(GlobalParam globalParam, long userId = 0) => await UserManager.GetUserAvatar(globalParam, userId);
        public async Task<(ErrorReturner Error, UserData? Data)> GetUserData(GlobalParam globalParam, long userId = 0) => await UserManager.GetUserData(globalParam, userId);
        public async Task<(ErrorReturner Error, Proto.Identity.Token? refreshToken, Proto.Identity.Token? accessToken, bool getMeOtpCode)> Authorizations(string _email, string _username, string _password, string _otpCode, GlobalParam global) => await UserManager.Authorizations(_email, _username, _password, _otpCode, global);
        public async Task<(ErrorReturner error, List<Proto.Users.UserBadge>? badges)> GetUserBadges(GlobalParam globalParam, long userId = 0, int? limit = null) => await UserManager.GetUserBadges(globalParam, userId, limit);
        public async Task<(ErrorReturner error, Proto.Users.PrivacySettings? settings)> GetPrivacySettings(GlobalParam globalParam) => await UserManager.GetPrivacySettings(globalParam);
        public async Task<ErrorReturner> UpdatePrivacySettings(Proto.Users.PrivacySettings settings, GlobalParam globalParam) => await UserManager.UpdatePrivacySettings(settings, globalParam);
        public async Task<ErrorReturner> SetNotificationsEnabled(bool enabled, GlobalParam globalParam) => await UserManager.SetNotificationsEnabled(enabled, globalParam);
        #endregion

        #region Настройка двухфакторной аутентификации (делегирование к AuthManager)
        public async Task<(ErrorReturner error, string? qrBase64, string? justCode)> OtpReceipt(GlobalParam globalParam) => await AuthManager.OtpReceipt(globalParam);
        public async Task<ErrorReturner> OtpAccept(GlobalParam globalParam, string code) => await AuthManager.OtpAccept(globalParam, code);
        public async Task<ErrorReturner> OtpDisable(GlobalParam globalParam) => await AuthManager.OtpDisable(globalParam);
        public async Task<(ErrorReturner error, bool authenticatorEnabled, bool emailEnabled)> OtpStatus(GlobalParam globalParam) => await AuthManager.OtpStatus(globalParam);
        #endregion

        #region Регистрация (делегирование к RegistrationManager)
        public async Task<(ErrorReturner error, string? userid)> CreateAccount(string firstName, string lastName, string email, string login, GlobalParam global) => await RegistrationManager.CreateAccount(firstName, lastName, email, login, global);
        public async Task<(ErrorReturner error, BarkFluff.Proto.Identity.Token? RefreshToken)> ConfirmAccount(string code, string verifyCode, GlobalParam global) => await RegistrationManager.ConfirmAccount(code, verifyCode, global);
        #endregion

        #region Сброс пароля (делегирование к PasswordManager)
        public async Task<ErrorReturner> SetPassword(string newPassword, GlobalParam globalParam) => await PasswordManager.SetPassword(newPassword, globalParam);
        public async Task<(ErrorReturner error, string? resetId)> ResetPassword(string email, string username, GlobalParam globalParam) => await PasswordManager.ResetPassword(email, username, globalParam);
        public async Task<(ErrorReturner error, BarkFluff.Proto.Identity.Token? refreshToken)> ConfirmResetCode(string resetId, string otpCode, GlobalParam globalParam) => await PasswordManager.ConfirmResetCode(resetId, otpCode, globalParam);
        #endregion

        #region Работа с сообщениями (делегирование к MessageManager)
        public async Task<(ErrorReturner error, List<Proto.Messages.Chat>? chats)> GetChats(GlobalParam globalParam) => await MessageManager.GetChats(globalParam);
        public async Task<(ErrorReturner error, ChatInfo chatInfo)> GetChatInfo(GlobalParam globalParam, string chatId) => await MessageManager.GetChatInfo(globalParam, chatId);
        public async Task<(ErrorReturner error, MessageModel? message)> SendMessage(GlobalParam globalParam, (bool isUserId, string recipient) options, ForwardingLetter letter) => await MessageManager.SendMessage(globalParam, options, letter);
        public async Task<(bool, string?)> CreateGroupChat(GlobalParam globalParam, string chatName, List<long> userIds) => await MessageManager.CreateGroupChat(globalParam, chatName, userIds);
        public async Task<(bool, string?)> CreateChat(GlobalParam globalParam, string userId) => await MessageManager.CreateChat(globalParam, userId);
        public async Task<(ErrorReturner error, List<MessageModel>? messages)> GetMessages(GlobalParam globalParam, string chatId, long fromMessageId) => await MessageManager.GetMessages(globalParam, chatId, fromMessageId);
        public async Task<(ErrorReturner error, List<MessageModel>? messages)> GetMessagesWithOffset(GlobalParam globalParam, string chatId, long fromMessageId, int offsetBefore, int offsetAfter) => await MessageManager.GetMessagesWithOffset(globalParam, chatId, fromMessageId, offsetBefore, offsetAfter);
        public async Task<ErrorReturner> MarkMessageAsRead(GlobalParam globalParam, List<long> messageId) => await MessageManager.MarkMessageAsRead(globalParam, messageId);
        public async Task<(ErrorReturner error, string chatId)> GetPersonChatId(GlobalParam globalParam, long userId) => await MessageManager.GetPersonChatId(globalParam, userId);
        public async Task<(ErrorReturner error, List<Proto.Messages.ListChatMembersResponse.Types.DetailedChatMemberInfo>? members, int totalCount)> ListChatMembers(GlobalParam globalParam, string chatId, int offset = 0, int size = 50) => await MessageManager.ListChatMembers(globalParam, chatId, offset, size);
        public async Task<(ErrorReturner error, List<Proto.Messages.ChatAttachmentInfo>? attachments, int totalCount)> ListChatAttachments(GlobalParam globalParam, string chatId, Proto.Shared.MessageAttachmentType attachmentType = Proto.Shared.MessageAttachmentType.Unknown, bool sortDescending = true, int offset = 0, int size = 50) => await MessageManager.ListChatAttachments(globalParam, chatId, attachmentType, sortDescending, offset, size);
        public async Task<ErrorReturner> KickUser(GlobalParam globalParam, string chatId, long userId) => await MessageManager.KickUser(globalParam, chatId, userId);
        #endregion

        #region Поиск (делегирование к SearchManager)
        public async Task<(ErrorReturner error, List<UserData>? userList)> SearchUser(GlobalParam globalParam, string userNameSearched) => await SearchManager.SearchUser(globalParam, userNameSearched);
        #endregion

        #region Файлы (делегирование к FileManager)
        public async Task<(ErrorReturner error, long totalUsedSpace, long totalSpace, Dictionary<Proto.Files.UploadFileType, long> storageByType)> GetUserStorageInfoAsync(GlobalParam globalParam) => await FileManager.GetUserStorageInfoAsync(globalParam);
        public async Task<(ErrorReturner error, string? fileId)> UploadFileAsync(GlobalParam globalParam, string filePath, Proto.Files.UploadFileType fileType) => await FileManager.UploadFileAsync(globalParam, filePath, fileType);
        public async Task<(ErrorReturner error, string? fileId)> UploadFileAsync(GlobalParam globalParam, string filePath, Proto.Files.UploadFileType fileType, IProgress<double>? progress) => await FileManager.UploadFileAsync(globalParam, filePath, fileType, progress);
        public async Task<ErrorReturner> UploadUserAvatarAsync(GlobalParam globalParam, byte[] jpegImageBytes) => await FileManager.UploadUserAvatarAsync(globalParam, jpegImageBytes);
        public async Task<(ErrorReturner error, string? url)> GetFile(GlobalParam globalParam, string fileId) => await FileManager.GetFile(globalParam, fileId);
        public async Task<(ErrorReturner error, List<string>? urls)> GetFiles(GlobalParam globalParam, List<string> fileId) => await FileManager.GetFiles(globalParam, fileId);
        public static async Task<string> ComputeFileHashAsync(string filePath) => await WebApiFileManager.ComputeFileHashAsync(filePath);
        public static string ComputeDataHash(byte[] data) => WebApiFileManager.ComputeDataHash(data);
        public async Task<(ErrorReturner error, string fileId)> CheckFileHashAsync(GlobalParam globalParam, string fileHash) => await FileManager.CheckFileHashAsync(globalParam, fileHash);
        public async Task<(ErrorReturner error, List<Proto.Files.StickerPackInfo>? packs, int totalCount)> ListStickerPacksAsync(GlobalParam globalParam, int offset = 0, int size = 50) => await FileManager.ListStickerPacksAsync(globalParam, offset, size);
        public async Task<(ErrorReturner error, Proto.Files.StickerPackInfo? pack, List<Proto.Files.StickerInfo>? stickers)> GetStickerPackAsync(GlobalParam globalParam, string packId) => await FileManager.GetStickerPackAsync(globalParam, packId);
        #endregion

        #region Реалтайм обновления (делегирование к UpdateManager)
        public async Task<(ErrorReturner error, IAsyncEnumerable<NewMessageEvent>? stream)> JustUpdate(GlobalParam globalParam) => await UpdateManager.JustUpdate(globalParam);
        public async Task<(ErrorReturner error, IAsyncEnumerable<MessageReadEvent>? stream)> SubscribeToReadReceipts(GlobalParam globalParam) => await UpdateManager.SubscribeToReadReceipts(globalParam);
        #endregion

        #region Работа с онлайн-статусами (делегирование к OnlinerManager)
        public async Task<(ErrorReturner error, IAsyncEnumerable<Proto.Onliner.UserOnlineStatus>? stream)> SubscribeToOnlineStatus(List<long> userIds, GlobalParam globalParam)
            => await OnlinerManager.SubscribeToOnlineStatus(userIds, globalParam);

        public async Task<ErrorReturner> SetOnlineStatus(GlobalParam globalParam)
            => await OnlinerManager.SetOnlineStatus(globalParam);

        public async Task<(ErrorReturner error, Proto.Onliner.GetOnlineStatusResponse? response)> GetOnlineStatus(List<long> userIds, GlobalParam globalParam)
            => await OnlinerManager.GetOnlineStatus(userIds, globalParam);

        public async Task<ErrorReturner> ChangeUsersInSubscription(List<long> userIds, GlobalParam globalParam)
            => await OnlinerManager.ChangeUsersInSubscription(userIds, globalParam);
        #endregion
    }
}
