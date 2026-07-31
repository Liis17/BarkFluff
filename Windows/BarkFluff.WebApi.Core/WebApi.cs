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

        /// <summary>
        /// Событие вызывается после успешного проактивного обновления токена (каждые ~4 минуты).
        /// Подписчики должны пересоздать все стриминговые gRPC-соединения (новые сообщения, read receipts, онлайн-статусы).
        /// </summary>
        public event EventHandler? TokenRefreshed
        {
            add => TokenManager.TokenRefreshed += value;
            remove => TokenManager.TokenRefreshed -= value;
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
        internal BarkFluff.Proto.FastAuth.FastAuthApi.FastAuthApiClient? FastAuthAC;
        /// <summary>
        /// FastAuth поверх авторизованного канала: ScanFastAuth/AcceptFastAuth/RejectFastAuth
        /// требуют User-токен, анонимный <see cref="FastAuthAC"/> для них не подходит.
        /// </summary>
        internal BarkFluff.Proto.FastAuth.FastAuthApi.FastAuthApiClient? FastAuthUserAC;
        internal BarkFluff.Proto.Calls.CallsApi.CallsApiClient? CallsAC;
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
        internal GrpcChannel? FastAuthChannel;
        internal GrpcChannel? FastAuthUserChannel;
        internal GrpcChannel? CallsChannel;
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
        internal readonly WebApiFastAuthManager FastAuthManager;
        internal readonly WebApiChatFolderManager ChatFolderManager;
        internal readonly WebApiCallsManager CallsManager;
        internal readonly WebApiPrivateChatManager PrivateChatManager;
        internal readonly WebApiSecretChatManager SecretChatManager;
        #endregion

        public bool ACisnull => UsersAC == null || BeaconAC == null || IdentityAC == null || FilesAC == null || MessagesAC == null || UpdatesAC == null;
        public bool BeaconIsnull => BeaconAC == null;

        /// <summary>
        /// Доступны ли звонки: сервис Calls есть в ответе Beacon и его канал создан.
        /// Сервер может быть развёрнут без звонков — тогда все методы CallManager
        /// вернут ошибку вместо исключения.
        /// </summary>
        public bool CallsAvailable => CallsAC != null;

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
            FastAuthManager = new WebApiFastAuthManager(this);
            ChatFolderManager = new WebApiChatFolderManager(this);
            CallsManager = new WebApiCallsManager(this);
            PrivateChatManager = new WebApiPrivateChatManager(this);
            SecretChatManager = new WebApiSecretChatManager(this);
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
                TokenManager.StopAutoRefresh();
                BeaconChannel?.Dispose();
                UserChannel?.Dispose();
                IdentityChannel?.Dispose();
                FilesChannel?.Dispose();
                MessagesChannel?.Dispose();
                NavigatorChannel?.Dispose();
                UpdatesChannel?.Dispose();
                OnlinerChannel?.Dispose();
                FastAuthChannel?.Dispose();
                FastAuthUserChannel?.Dispose();
                CallsChannel?.Dispose();
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
        /// <summary>
        /// Запускает фоновый авто-обновитель токена (обновляет за 1 минуту до истечения).
        /// После успешного обновления вызывается событие <see cref="TokenRefreshed"/> —
        /// подписчики должны пересоздать стриминговые gRPC-соединения.
        /// </summary>
        public void StartAutoRefresh(GlobalParam globalParam) => TokenManager.StartAutoRefresh(globalParam);
        /// <summary>
        /// Останавливает фоновый авто-обновитель токена.
        /// </summary>
        public void StopAutoRefresh() => TokenManager.StopAutoRefresh();
        #endregion

        #region Обслуживание
        /// <summary>
        /// Добавляет схему к URL, если её нет. По умолчанию — https://, потому что
        /// production-сервера BarkFluff работают по TLS. Для локального plaintext-сервера
        /// нужно явно передать http:// в адресе. Пустая строка возвращается как есть —
        /// иначе на выходе получится мусор вида "https://" без хоста.
        /// </summary>
        public static string EnsureHttpPrefix(string _url)
        {
            if (string.IsNullOrWhiteSpace(_url)) return _url;
            return !_url.StartsWith("http://") && !_url.StartsWith("https://")
                   ? "https://" + _url
                   : _url;
        }

        /// <summary>
        /// Собирает URL вида {scheme}://{host}:{port}, где scheme выбирается
        /// по флагу tlsEnabled. Если в host уже есть схема — она срезается,
        /// чтобы избежать двойного префикса.
        /// </summary>
        public static string BuildEndpointUrl(string host, int port, bool tlsEnabled)
        {
            var cleanHost = host ?? string.Empty;
            if (cleanHost.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                cleanHost = cleanHost.Substring(8);
            else if (cleanHost.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                cleanHost = cleanHost.Substring(7);

            cleanHost = cleanHost.TrimEnd('/');

            var scheme = tlsEnabled ? "https" : "http";
            return $"{scheme}://{cleanHost}:{port}";
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
        public async Task<ErrorReturner> Logout(GlobalParam globalParam) => await UserManager.Logout(globalParam);
        public async Task<ErrorReturner> SetChatMuted(string chatId, bool muted, GlobalParam globalParam, DateTime? mutedUntil = null) => await UserManager.SetChatMuted(chatId, muted, globalParam, mutedUntil);
        public async Task<(ErrorReturner error, List<Proto.Users.MutedChat>? chats)> GetMutedChats(GlobalParam globalParam) => await UserManager.GetMutedChats(globalParam);
        public async Task<ErrorReturner> SetFirebaseToken(string firebaseToken, GlobalParam globalParam) => await UserManager.SetFirebaseToken(firebaseToken, globalParam);
        public async Task<(ErrorReturner error, Proto.Users.ResolveFederatedUserResponse? user)> ResolveFederatedUser(string fid, GlobalParam globalParam) => await UserManager.ResolveFederatedUser(fid, globalParam);
        #endregion

        #region Prekey bundle секретных чатов (делегирование к UserManager)
        /// <summary>
        /// Зарегистрировать prekey-bundle текущего устройства. Крипта libsignal не реализована:
        /// ключи должны быть сгенерированы приложением и передаются как есть.
        /// </summary>
        public async Task<ErrorReturner> RegisterPrekeyBundle(uint registrationId, byte[] identityPubkey,
            Proto.Users.SignedPreKey signedPrekey, List<Proto.Users.OneTimePreKey> oneTimePrekeys, GlobalParam globalParam)
            => await UserManager.RegisterPrekeyBundle(registrationId, identityPubkey, signedPrekey, oneTimePrekeys, globalParam);

        /// <summary>
        /// Получить prekey-bundle устройства собеседника. Крипта libsignal не реализована:
        /// полученный bundle передаётся приложению как есть.
        /// </summary>
        public async Task<(ErrorReturner error, Proto.Users.FetchPrekeyBundleResponse? response)> FetchPrekeyBundle(
            long userId, string deviceId, GlobalParam globalParam)
            => await UserManager.FetchPrekeyBundle(userId, deviceId, globalParam);

        /// <summary>
        /// Получить устройства собеседника, готовые к секретному чату. Крипта libsignal не реализована.
        /// </summary>
        public async Task<(ErrorReturner error, List<Proto.Users.PeerDeviceInfo>? devices)> ListPeerDevices(long userId, GlobalParam globalParam)
            => await UserManager.ListPeerDevices(userId, globalParam);

        /// <summary>
        /// Пополнить пул разовых prekey текущего устройства. Крипта libsignal не реализована:
        /// новые ключи передаются как есть.
        /// </summary>
        public async Task<(ErrorReturner error, int totalOneTimePrekeys)> ReplenishOneTimePrekeys(
            List<Proto.Users.OneTimePreKey> prekeys, GlobalParam globalParam)
            => await UserManager.ReplenishOneTimePrekeys(prekeys, globalParam);

        /// <summary>
        /// Ротировать signed prekey текущего устройства. Крипта libsignal не реализована:
        /// ключ должен быть сгенерирован приложением и передаётся как есть.
        /// </summary>
        public async Task<ErrorReturner> RotateSignedPrekey(Proto.Users.SignedPreKey signedPrekey, GlobalParam globalParam)
            => await UserManager.RotateSignedPrekey(signedPrekey, globalParam);
        #endregion

        #region Папки чатов (делегирование к ChatFolderManager)
        public async Task<(ErrorReturner error, List<Proto.Users.ChatFolderData>? folders)> GetChatFolders(GlobalParam globalParam) => await ChatFolderManager.GetChatFolders(globalParam);
        public async Task<(ErrorReturner error, Proto.Users.ChatFolderData? folder)> CreateChatFolder(string folderName, GlobalParam globalParam, string folderIcon = "") => await ChatFolderManager.CreateChatFolder(folderName, globalParam, folderIcon);
        public async Task<(ErrorReturner error, Proto.Users.ChatFolderData? folder)> UpdateChatFolder(string folderId, GlobalParam globalParam, string? folderName = null, string? folderIcon = null, List<string>? chatList = null) => await ChatFolderManager.UpdateChatFolder(folderId, globalParam, folderName, folderIcon, chatList);
        public async Task<ErrorReturner> DeleteChatFolder(string folderId, GlobalParam globalParam) => await ChatFolderManager.DeleteChatFolder(folderId, globalParam);
        public async Task<(ErrorReturner error, Proto.Users.ChatFolderData? folder)> AddChatToFolder(string folderId, string chatId, GlobalParam globalParam) => await ChatFolderManager.AddChatToFolder(folderId, chatId, globalParam);
        public async Task<(ErrorReturner error, Proto.Users.ChatFolderData? folder)> RemoveChatFromFolder(string folderId, string chatId, GlobalParam globalParam) => await ChatFolderManager.RemoveChatFromFolder(folderId, chatId, globalParam);
        public async Task<ErrorReturner> ReorderChatFolders(Dictionary<string, int> orders, GlobalParam globalParam) => await ChatFolderManager.ReorderChatFolders(orders, globalParam);
        #endregion

        #region Персонализация (делегирование к UserManager)
        public async Task<(ErrorReturner error, Proto.Users.UserPersonalizationData? data)> GetPersonalization(GlobalParam globalParam)
            => await UserManager.GetPersonalization(globalParam);
        public async Task<ErrorReturner> UpdatePersonalization(Proto.Users.UserPersonalizationData data, GlobalParam globalParam)
            => await UserManager.UpdatePersonalization(data, globalParam);
        public async Task<(ErrorReturner error, string fileId)> GetProfilePoster(GlobalParam globalParam)
            => await UserManager.GetProfilePoster(globalParam);
        public async Task<ErrorReturner> SetProfilePoster(string fileId, GlobalParam globalParam)
            => await UserManager.SetProfilePoster(fileId, globalParam);
        #endregion

        #region Настройка двухфакторной аутентификации (делегирование к AuthManager)
        public async Task<(ErrorReturner error, string? qrBase64, string? justCode)> OtpReceipt(GlobalParam globalParam, Proto.Identity.OtpTypeId otpType = Proto.Identity.OtpTypeId.Authenticator) => await AuthManager.OtpReceipt(globalParam, otpType);
        public async Task<ErrorReturner> OtpAccept(GlobalParam globalParam, string code) => await AuthManager.OtpAccept(globalParam, code);
        public async Task<ErrorReturner> OtpDisable(GlobalParam globalParam, Proto.Identity.OtpTypeId otpType = Proto.Identity.OtpTypeId.Authenticator, string otpCode = "") => await AuthManager.OtpDisable(globalParam, otpType, otpCode);
        public async Task<(ErrorReturner error, bool authenticatorEnabled, bool emailEnabled)> OtpStatus(GlobalParam globalParam) => await AuthManager.OtpStatus(globalParam);
        #endregion

        #region Регистрация (делегирование к RegistrationManager)
        public async Task<(ErrorReturner error, string? userid)> CreateAccount(string firstName, string lastName, string email, string login, GlobalParam global) => await RegistrationManager.CreateAccount(firstName, lastName, email, login, global);
        public async Task<(ErrorReturner error, BarkFluff.Proto.Identity.Token? RefreshToken)> ConfirmAccount(string code, string verifyCode, GlobalParam global) => await RegistrationManager.ConfirmAccount(code, verifyCode, global);
        #endregion

        #region Сброс пароля (делегирование к PasswordManager)
        public async Task<ErrorReturner> SetPassword(string newPassword, GlobalParam globalParam, string oldPassword = "") => await PasswordManager.SetPassword(newPassword, globalParam, oldPassword);
        public async Task<(ErrorReturner error, string? resetId)> ResetPassword(string email, string username, GlobalParam globalParam) => await PasswordManager.ResetPassword(email, username, globalParam);
        public async Task<(ErrorReturner error, BarkFluff.Proto.Identity.Token? refreshToken)> ConfirmResetCode(string resetId, string otpCode, GlobalParam globalParam) => await PasswordManager.ConfirmResetCode(resetId, otpCode, globalParam);
        #endregion

        #region Работа с сообщениями (делегирование к MessageManager)
        public async Task<(ErrorReturner error, List<Proto.Messages.Chat>? chats)> GetChats(GlobalParam globalParam) => await MessageManager.GetChats(globalParam);
        public async Task<(ErrorReturner error, ChatInfo chatInfo)> GetChatInfo(GlobalParam globalParam, string chatId) => await MessageManager.GetChatInfo(globalParam, chatId);
        public async Task<(ErrorReturner error, MessageModel? message)> SendMessage(GlobalParam globalParam, (bool isUserId, string recipient) options, ForwardingLetter letter) => await MessageManager.SendMessage(globalParam, options, letter);
        public async Task<(bool, string?)> CreateGroupChat(GlobalParam globalParam, string chatName, List<long> userIds) => await MessageManager.CreateGroupChat(globalParam, chatName, userIds); // исправить не использовать в таком виде
        public async Task<(bool, string?)> CreateChat(GlobalParam globalParam, string userId) => await MessageManager.CreateChat(globalParam, userId); // исправить не использовать в таком виде
        public async Task<(ErrorReturner error, List<MessageModel>? messages)> GetMessages(GlobalParam globalParam, string chatId, long fromMessageId) => await MessageManager.GetMessages(globalParam, chatId, fromMessageId);
        public async Task<(ErrorReturner error, List<MessageModel>? messages)> GetMessagesWithOffset(GlobalParam globalParam, string chatId, long fromMessageId, int offsetBefore, int offsetAfter) => await MessageManager.GetMessagesWithOffset(globalParam, chatId, fromMessageId, offsetBefore, offsetAfter);
        public async Task<ErrorReturner> MarkMessageAsRead(GlobalParam globalParam, List<long> messageId) => await MessageManager.MarkMessageAsRead(globalParam, messageId);
        public async Task<(ErrorReturner error, string chatId)> GetPersonChatId(GlobalParam globalParam, long userId) => await MessageManager.GetPersonChatId(globalParam, userId);
        public async Task<(ErrorReturner error, List<Proto.Messages.ListChatMembersResponse.Types.DetailedChatMemberInfo>? members, int totalCount)> ListChatMembers(GlobalParam globalParam, string chatId, int offset = 0, int size = 50) => await MessageManager.ListChatMembers(globalParam, chatId, offset, size);
        public async Task<(ErrorReturner error, List<Proto.Messages.ChatAttachmentInfo>? attachments, int totalCount)> ListChatAttachments(GlobalParam globalParam, string chatId, Proto.Shared.MessageAttachmentType attachmentType = Proto.Shared.MessageAttachmentType.Unknown, bool sortDescending = true, int offset = 0, int size = 50) => await MessageManager.ListChatAttachments(globalParam, chatId, attachmentType, sortDescending, offset, size);
        public async Task<ErrorReturner> KickUser(GlobalParam globalParam, string chatId, long userId) => await MessageManager.KickUser(globalParam, chatId, userId);
        public async Task<ErrorReturner> AddUser(GlobalParam globalParam, string chatId, long userId) => await MessageManager.AddUser(globalParam, chatId, userId);
        public async Task<(ErrorReturner error, Proto.Messages.Chat? chat)> UpdateGroupChat(GlobalParam globalParam, string chatId, string title = "", string pictureFileId = "") => await MessageManager.UpdateGroupChat(globalParam, chatId, title, pictureFileId);
        public async Task<(ErrorReturner error, MessageModel? message)> EditMessage(GlobalParam globalParam, string chatId, long messageId, string text, List<string>? fileIds = null) => await MessageManager.EditMessage(globalParam, chatId, messageId, text, fileIds);
        public async Task<ErrorReturner> DeleteMessage(GlobalParam globalParam, long messageId) => await MessageManager.DeleteMessage(globalParam, messageId);
        #endregion

        #region Закреплённые сообщения (делегирование к MessageManager)
        public async Task<(ErrorReturner error, Proto.Shared.PinnedMessageInfo? pinned)> PinMessage(GlobalParam globalParam, string chatId, long messageId) => await MessageManager.PinMessage(globalParam, chatId, messageId);
        public async Task<ErrorReturner> UnpinMessage(GlobalParam globalParam, string chatId, long messageId) => await MessageManager.UnpinMessage(globalParam, chatId, messageId);
        public async Task<(ErrorReturner error, List<Proto.Shared.PinnedMessageInfo>? pinned, int totalCount)> ListPinnedMessages(GlobalParam globalParam, string chatId, int offset = 0, int size = 50) => await MessageManager.ListPinnedMessages(globalParam, chatId, offset, size);
        public async Task<(ErrorReturner error, int unpinnedCount)> UnpinAll(GlobalParam globalParam, string chatId) => await MessageManager.UnpinAll(globalParam, chatId);
        #endregion

        #region Приватные чаты E2E (делегирование к PrivateChatManager)
        /// <summary>
        /// Создать приватный чат: библиотека сама выводит ключ из кодовой фразы (Argon2id)
        /// и считает verifier — сама фраза на сервер не уходит. Ключ нужно сохранить самостоятельно
        /// (библиотека его не хранит между вызовами) для последующих Send/List/Edit.
        /// </summary>
        public async Task<(ErrorReturner error, Proto.Messages.Chat? chat, bool created, byte[]? key)> CreatePrivateChat(long peerUserId, string passphrase, GlobalParam globalParam)
            => await PrivateChatManager.CreatePrivateChat(peerUserId, passphrase, globalParam);

        /// <summary>
        /// Принять приглашение: kdfSalt/passphraseVerifier берутся из события PrivateChatInviteEvent
        /// или из Chat (ListChats). Кодовая фраза проверяется локально до обращения к серверу.
        /// </summary>
        public async Task<(ErrorReturner error, Proto.Messages.Chat? chat, byte[]? key)> AcceptPrivateChat(string chatId, string passphrase, byte[] kdfSalt, byte[] passphraseVerifier, GlobalParam globalParam)
            => await PrivateChatManager.AcceptPrivateChat(chatId, passphrase, kdfSalt, passphraseVerifier, globalParam);

        public async Task<ErrorReturner> RejectPrivateChat(string chatId, GlobalParam globalParam)
            => await PrivateChatManager.RejectPrivateChat(chatId, globalParam);

        /// <summary>
        /// Восстановить ключ уже принятого чата по кодовой фразе (без сетевых вызовов),
        /// например после перезапуска приложения. Null означает неверную фразу.
        /// </summary>
        public static byte[]? UnlockPrivateChat(Proto.Messages.Chat chat, string passphrase) => WebApiPrivateChatManager.UnlockChat(chat, passphrase);

        public async Task<(ErrorReturner error, PrivateMessageModel? message)> SendPrivateMessage(string chatId, string text, byte[] key, GlobalParam globalParam)
            => await PrivateChatManager.SendPrivateMessage(chatId, text, key, globalParam);

        public async Task<(ErrorReturner error, List<PrivateMessageModel>? messages)> ListPrivateMessages(string chatId, byte[] key, GlobalParam globalParam, long fromMessageId = 0, int offsetBefore = 50, int offsetAfter = 0)
            => await PrivateChatManager.ListPrivateMessages(chatId, key, globalParam, fromMessageId, offsetBefore, offsetAfter);

        public async Task<(ErrorReturner error, PrivateMessageModel? message)> EditPrivateMessage(string chatId, long messageId, string text, byte[] key, GlobalParam globalParam)
            => await PrivateChatManager.EditPrivateMessage(chatId, messageId, text, key, globalParam);

        public async Task<ErrorReturner> DeletePrivateMessage(long messageId, GlobalParam globalParam)
            => await PrivateChatManager.DeletePrivateMessage(messageId, globalParam);

        public async Task<ErrorReturner> MarkPrivateMessagesAsRead(string chatId, long lastReadMessageId, GlobalParam globalParam)
            => await PrivateChatManager.MarkPrivateMessagesAsRead(chatId, lastReadMessageId, globalParam);

        /// <summary>
        /// Расшифровать одно сообщение приватного чата, например пришедшее из
        /// <see cref="SubscribeToPrivateMessages"/>/<see cref="SubscribeToPrivateMessageEdits"/>.
        /// </summary>
        public static PrivateMessageModel DecryptPrivateMessage(Proto.Shared.EncryptedMessage message, byte[] key) => WebApiPrivateChatManager.DecryptMessage(message, key);
        #endregion

        #region Реалтайм обновления приватных чатов (делегирование к UpdateManager)
        public async Task<(ErrorReturner error, IAsyncEnumerable<Proto.Updates.NewEncryptedMessageEvent>? stream)> SubscribeToPrivateMessages(GlobalParam globalParam, CancellationToken ct = default)
            => await UpdateManager.SubscribeToPrivateMessages(globalParam, ct);

        public async Task<(ErrorReturner error, IAsyncEnumerable<Proto.Updates.EncryptedMessageEditedEvent>? stream)> SubscribeToPrivateMessageEdits(GlobalParam globalParam, CancellationToken ct = default)
            => await UpdateManager.SubscribeToPrivateMessageEdits(globalParam, ct);

        public async Task<(ErrorReturner error, IAsyncEnumerable<Proto.Updates.EncryptedMessageDeletedEvent>? stream)> SubscribeToPrivateMessageDeletes(GlobalParam globalParam, CancellationToken ct = default)
            => await UpdateManager.SubscribeToPrivateMessageDeletes(globalParam, ct);

        public async Task<(ErrorReturner error, IAsyncEnumerable<Proto.Updates.PrivateMessagesReadEvent>? stream)> SubscribeToPrivateMessagesRead(GlobalParam globalParam, CancellationToken ct = default)
            => await UpdateManager.SubscribeToPrivateMessagesRead(globalParam, ct);

        public async Task<(ErrorReturner error, IAsyncEnumerable<Proto.Updates.PrivateChatInviteEvent>? stream)> SubscribeToPrivateChatInvites(GlobalParam globalParam, CancellationToken ct = default)
            => await UpdateManager.SubscribeToPrivateChatInvites(globalParam, ct);

        public async Task<(ErrorReturner error, IAsyncEnumerable<Proto.Updates.PrivateChatInviteResolutionEvent>? stream)> SubscribeToPrivateChatInviteResolutions(GlobalParam globalParam, CancellationToken ct = default)
            => await UpdateManager.SubscribeToPrivateChatInviteResolutions(globalParam, ct);
        #endregion

        #region Секретные чаты (транспорт без libsignal)
        /// <summary>
        /// Отправить инвайт секретного чата. Крипта libsignal не реализована,
        /// initialEnvelope прокидывается как есть.
        /// </summary>
        public async Task<(ErrorReturner error, Proto.Messages.SendSecretChatInviteResponse? response)> SendSecretChatInvite(
            long recipientUserId, string recipientDeviceId, byte[] initialEnvelope, GlobalParam globalParam)
            => await SecretChatManager.SendSecretChatInvite(recipientUserId, recipientDeviceId, initialEnvelope, globalParam);

        /// <summary>
        /// Принять инвайт секретного чата. Крипта libsignal не реализована,
        /// responseEnvelope прокидывается как есть.
        /// </summary>
        public async Task<ErrorReturner> AcceptSecretChatInvite(string inviteId, byte[] responseEnvelope, GlobalParam globalParam)
            => await SecretChatManager.AcceptSecretChatInvite(inviteId, responseEnvelope, globalParam);

        /// <summary>
        /// Отклонить инвайт секретного чата. Крипта libsignal не реализована.
        /// </summary>
        public async Task<ErrorReturner> RejectSecretChatInvite(string inviteId, GlobalParam globalParam)
            => await SecretChatManager.RejectSecretChatInvite(inviteId, globalParam);

        /// <summary>
        /// Отправить секретное сообщение. Крипта libsignal не реализована,
        /// envelope прокидывается как есть.
        /// </summary>
        public async Task<(ErrorReturner error, Proto.Messages.SendSecretMessageResponse? response)> SendSecretMessage(
            long recipientUserId, string recipientDeviceId, byte[] envelope, GlobalParam globalParam)
            => await SecretChatManager.SendSecretMessage(recipientUserId, recipientDeviceId, envelope, globalParam);

        /// <summary>
        /// Подтвердить доставку секретного сообщения. Крипта libsignal не реализована.
        /// </summary>
        public async Task<ErrorReturner> AckSecretMessage(string messageId, GlobalParam globalParam)
            => await SecretChatManager.AckSecretMessage(messageId, globalParam);
        #endregion

        #region Реалтайм обновления секретных чатов (транспорт без libsignal)
        /// <summary>
        /// Приглашения секретных чатов. Крипта libsignal не реализована,
        /// initialEnvelope в событии прокидывается как есть.
        /// </summary>
        public async Task<(ErrorReturner error, IAsyncEnumerable<Proto.Updates.SecretChatInviteEvent>? stream)> SubscribeToSecretChatInvites(GlobalParam globalParam, CancellationToken ct = default)
            => await UpdateManager.SubscribeToSecretChatInvites(globalParam, ct);

        /// <summary>
        /// Ответы на приглашения секретных чатов. Крипта libsignal не реализована,
        /// responseEnvelope в событии прокидывается как есть.
        /// </summary>
        public async Task<(ErrorReturner error, IAsyncEnumerable<Proto.Updates.SecretChatInviteResolutionEvent>? stream)> SubscribeToSecretChatResolutions(GlobalParam globalParam, CancellationToken ct = default)
            => await UpdateManager.SubscribeToSecretChatResolutions(globalParam, ct);

        /// <summary>
        /// Секретные сообщения текущего устройства. Крипта libsignal не реализована,
        /// envelope в событии прокидывается как есть.
        /// </summary>
        public async Task<(ErrorReturner error, IAsyncEnumerable<Proto.Updates.NewSecretMessageEvent>? stream)> SubscribeToSecretMessages(GlobalParam globalParam, CancellationToken ct = default)
            => await UpdateManager.SubscribeToSecretMessages(globalParam, ct);
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
        public async Task<(ErrorReturner error, IAsyncEnumerable<NewMessageEvent>? stream)> JustUpdate(GlobalParam globalParam, CancellationToken ct = default) => await UpdateManager.JustUpdate(globalParam, ct);
        public async Task<(ErrorReturner error, IAsyncEnumerable<MessageReadEvent>? stream)> SubscribeToReadReceipts(GlobalParam globalParam, CancellationToken ct = default) => await UpdateManager.SubscribeToReadReceipts(globalParam, ct);
        public async Task<(ErrorReturner error, IAsyncEnumerable<MessageEditedEvent>? stream)> SubscribeToMessagesEdited(GlobalParam globalParam, CancellationToken ct = default) => await UpdateManager.SubscribeToMessagesEdited(globalParam, ct);
        public async Task<(ErrorReturner error, IAsyncEnumerable<MessageDeletedEvent>? stream)> SubscribeToMessagesDeleted(GlobalParam globalParam, CancellationToken ct = default) => await UpdateManager.SubscribeToMessagesDeleted(globalParam, ct);
        public async Task<(ErrorReturner error, IAsyncEnumerable<MessagePinnedEvent>? stream)> SubscribeToMessagesPinned(GlobalParam globalParam, CancellationToken ct = default) => await UpdateManager.SubscribeToMessagesPinned(globalParam, ct);
        public async Task<(ErrorReturner error, IAsyncEnumerable<MessageUnpinnedEvent>? stream)> SubscribeToMessagesUnpinned(GlobalParam globalParam, CancellationToken ct = default) => await UpdateManager.SubscribeToMessagesUnpinned(globalParam, ct);
        public async Task<(ErrorReturner error, IAsyncEnumerable<AllMessagesUnpinnedEvent>? stream)> SubscribeToAllMessagesUnpinned(GlobalParam globalParam, CancellationToken ct = default) => await UpdateManager.SubscribeToAllMessagesUnpinned(globalParam, ct);

        /// <summary>
        /// События обновлений несут сырое proto-сообщение, а остальные методы отдают наружу
        /// <see cref="MessageModel"/>. Маппер живёт во внутреннем менеджере, поэтому подписчикам
        /// стримов он доступен только через этот проброс.
        /// </summary>
        public static MessageModel MapEventMessage(Proto.Shared.Message message, string chatId) => WebApiMessageManager.MapMessage(message, chatId);
        #endregion

        #region FastAuth (делегирование к FastAuthManager)
        public ErrorReturner CreateFastAuthClient(MessengerData.GlobalParam gParam, string deviceName, string os, string appName, string appVersion, string ip)
            => ClientManager.CreateFastAuthClient(gParam, deviceName, os, appName, appVersion, ip);

        public void DisposeFastAuthClient()
            => ClientManager.DisposeFastAuthClient();

        public async Task<(ErrorReturner, Proto.FastAuth.GenerateFastAuthTokenResponse?)> GenerateFastAuthToken(Proto.FastAuth.TokenFormat format)
            => await FastAuthManager.GenerateFastAuthToken(format);

        public async Task<(ErrorReturner, IAsyncEnumerable<Proto.FastAuth.FastAuthResult>?)> SubscribeFastAuthResult(string fastAuthId, CancellationToken ct)
            => await FastAuthManager.SubscribeFastAuthResult(fastAuthId, ct);

        /// <summary>
        /// Подтверждение входа нового устройства с этого (уже авторизованного) клиента:
        /// Scan → показать пользователю данные устройства → Accept или Reject.
        /// </summary>
        public async Task<(ErrorReturner error, Proto.FastAuth.ScanFastAuthResponse? info)> ScanFastAuth(string fastAuthId, GlobalParam globalParam)
            => await FastAuthManager.ScanFastAuth(fastAuthId, globalParam);

        public async Task<ErrorReturner> AcceptFastAuth(string fastAuthId, string confirmationCode, GlobalParam globalParam)
            => await FastAuthManager.AcceptFastAuth(fastAuthId, confirmationCode, globalParam);

        public async Task<ErrorReturner> RejectFastAuth(string fastAuthId, string confirmationCode, GlobalParam globalParam)
            => await FastAuthManager.RejectFastAuth(fastAuthId, confirmationCode, globalParam);
        #endregion

        #region Работа с онлайн-статусами (делегирование к OnlinerManager)
        public async Task<(ErrorReturner error, IAsyncEnumerable<Proto.Onliner.UserOnlineStatus>? stream)> SubscribeToOnlineStatus(List<long> userIds, GlobalParam globalParam, CancellationToken ct = default)
            => await OnlinerManager.SubscribeToOnlineStatus(userIds, globalParam, ct);

        public async Task<ErrorReturner> SetOnlineStatus(GlobalParam globalParam)
            => await OnlinerManager.SetOnlineStatus(globalParam);

        public async Task<(ErrorReturner error, Proto.Onliner.GetOnlineStatusResponse? response)> GetOnlineStatus(List<long> userIds, GlobalParam globalParam)
            => await OnlinerManager.GetOnlineStatus(userIds, globalParam);

        public async Task<ErrorReturner> ChangeUsersInSubscription(List<long> userIds, GlobalParam globalParam)
            => await OnlinerManager.ChangeUsersInSubscription(userIds, globalParam);
        #endregion

        #region Звонки (делегирование к CallsManager)
        /// <summary>
        /// Сигнализация звонков. Медиа библиотека не ведёт: в ответах приходят
        /// livekit_url и access_token, подключение к LiveKit SFU — на стороне приложения.
        /// Проверять доступность звонков на сервере — через <see cref="CallsAvailable"/>.
        /// </summary>
        public async Task<(ErrorReturner error, Proto.Calls.InitiateCallResponse? call)> InitiateCallToUser(long calleeUserId, Proto.Calls.CallMediaType mediaType, GlobalParam globalParam)
            => await CallsManager.InitiateCallToUser(calleeUserId, mediaType, globalParam);

        public async Task<(ErrorReturner error, Proto.Calls.InitiateCallResponse? call)> InitiateCallInChat(string chatId, Proto.Calls.CallMediaType mediaType, GlobalParam globalParam)
            => await CallsManager.InitiateCallInChat(chatId, mediaType, globalParam);

        public async Task<(ErrorReturner error, Proto.Calls.JoinCallResponse? call)> JoinCall(string callId, GlobalParam globalParam)
            => await CallsManager.JoinCall(callId, globalParam);

        public async Task<(ErrorReturner error, Proto.Calls.AcceptCallResponse? call)> AcceptCall(string callId, GlobalParam globalParam)
            => await CallsManager.AcceptCall(callId, globalParam);

        public async Task<ErrorReturner> RejectCall(string callId, GlobalParam globalParam)
            => await CallsManager.RejectCall(callId, globalParam);

        public async Task<ErrorReturner> EndCall(string callId, GlobalParam globalParam)
            => await CallsManager.EndCall(callId, globalParam);

        public async Task<ErrorReturner> SetCallAudioQuality(string callId, Proto.Calls.CallAudioQuality quality, GlobalParam globalParam)
            => await CallsManager.SetCallAudioQuality(callId, quality, globalParam);

        public async Task<(ErrorReturner error, IAsyncEnumerable<Proto.Calls.CallEvent>? stream)> SubscribeCallEvents(GlobalParam globalParam, CancellationToken ct = default)
            => await CallsManager.SubscribeCallEvents(globalParam, ct);

        public async Task<(ErrorReturner error, List<Proto.Calls.CallHistoryItem>? items, bool hasMore)> ListCallHistory(GlobalParam globalParam, Proto.Calls.CallHistoryFilter filter = Proto.Calls.CallHistoryFilter.CallHistoryAll, int limit = 50, DateTime? beforeStartedAt = null)
            => await CallsManager.ListCallHistory(globalParam, filter, limit, beforeStartedAt);

        public async Task<(ErrorReturner error, List<Proto.Calls.ActiveCallItem>? calls)> GetActiveCalls(List<string> chatIds, GlobalParam globalParam)
            => await CallsManager.GetActiveCalls(chatIds, globalParam);
        #endregion

        #region Индикаторы набора текста (делегирование к OnlinerManager)
        public async Task<ErrorReturner> SetTypingStatus(string chatId, Proto.Onliner.TypingAction action, GlobalParam globalParam)
            => await OnlinerManager.SetTypingStatus(chatId, action, globalParam);

        public async Task<(ErrorReturner error, IAsyncEnumerable<Proto.Onliner.TypingEvent>? stream)> SubscribeToTyping(List<string> chatIds, GlobalParam globalParam, CancellationToken ct = default)
            => await OnlinerManager.SubscribeToTyping(chatIds, globalParam, ct);

        public async Task<ErrorReturner> ChangeChatsInTypingSubscription(List<string> chatIds, GlobalParam globalParam)
            => await OnlinerManager.ChangeChatsInTypingSubscription(chatIds, globalParam);
        #endregion
    }
}
