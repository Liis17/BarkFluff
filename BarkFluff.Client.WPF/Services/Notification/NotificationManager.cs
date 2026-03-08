using BarkFluff.Client.WPF.Services.App;
using BarkFluff.Client.WPF.Services.App.Caching;
using BarkFluff.Proto.Shared;
using BarkFluff.WebApi.Core.MessengerData;
using BarkFluff.WebApi.Core.MessengerData.NonSavedData;

using Microsoft.Toolkit.Uwp.Notifications;

using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;

using Windows.UI.Notifications;

using WpfApplication = System.Windows.Application;
using WpfWindowState = System.Windows.WindowState;

namespace BarkFluff.Client.WPF.Services.Notification
{
    /// <summary>
    /// Менеджер уведомлений с интеграцией WindowStateService и кеширования
    /// </summary>
    public class NotificationManager : IDisposable
    {
        private static NotificationManager? _instance;
        private static readonly object _lock = new object();

        private WindowStateService? _windowStateService;
        private FileCacheService? _fileCacheService;
        private bool _disposed;

        /// <summary>
        /// Группа для toast-уведомлений сообщений (используется для очистки)
        /// </summary>
        private const string ToastGroup = "messages";

        /// <summary>
        /// Интервал антиспама: не показывать больше одного уведомления на чат за этот период
        /// </summary>
        private static readonly TimeSpan ThrottleInterval = TimeSpan.FromSeconds(15);

        /// <summary>
        /// Время последнего показанного уведомления для каждого чата
        /// </summary>
        private readonly ConcurrentDictionary<string, DateTime> _lastNotificationTime = new();

        /// <summary>
        /// Событие при клике на уведомление
        /// </summary>
        public event Action<NotificationData>? NotificationClicked;

        /// <summary>
        /// Получить экземпляр менеджера уведомлений (Singleton)
        /// </summary>
        public static NotificationManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        _instance ??= new NotificationManager();
                    }
                }
                return _instance;
            }
        }

        private NotificationManager()
        {
            // Подписываемся на события нажатия на уведомления
            ToastNotificationManagerCompat.OnActivated += OnToastActivated;
        }

        /// <summary>
        /// Инициализирует менеджер уведомлений
        /// </summary>
        public void Initialize(WindowStateService windowStateService, FileCacheService fileCacheService)
        {
            _windowStateService = windowStateService;
            _fileCacheService = fileCacheService;
        }

        /// <summary>
        /// Получает текущий режим отображения уведомлений из настроек
        /// </summary>
        private static NotificationDisplayMode GetCurrentDisplayMode()
        {
            return WPF.App.GParam?.NotificationMode ?? NotificationDisplayMode.FullWithPreview;
        }

        /// <summary>
        /// Проверяет, нужно ли показывать уведомление для данного чата
        /// </summary>
        /// <param name="chatId">ID чата</param>
        /// <returns>true, если нужно показать уведомление</returns>
        public bool ShouldShowNotification(string chatId)
        {
            // Проверяем настройку: если уведомления отключены - сразу false
            if (GetCurrentDisplayMode() == NotificationDisplayMode.Disabled)
                return false;

            if (_windowStateService == null)
                return true;

            // Если окно не активно (свернуто или не в фокусе) - всегда показываем уведомление
            if (!_windowStateService.IsApplicationActive.Value)
            {
                return true;
            }

            // Если окно активно - проверяем, открыт ли этот чат
            var messenger = WPF.App.Messenger;
            if (messenger == null)
                return true;

            // Если чат не открыт - показываем уведомление
            if (string.IsNullOrEmpty(messenger.ChatId.Value))
                return true;

            // Если открыт другой чат - показываем уведомление
            if (messenger.ChatId.Value != chatId)
                return true;

            // Чат открыт и окно активно - не показываем уведомление
            return false;
        }

        /// <summary>
        /// Проверяет throttling для чата (не чаще одного уведомления за ThrottleInterval)
        /// </summary>
        private bool IsThrottled(string chatId)
        {
            var now = DateTime.UtcNow;

            if (_lastNotificationTime.TryGetValue(chatId, out var lastTime))
            {
                if (now - lastTime < ThrottleInterval)
                    return true;
            }

            _lastNotificationTime[chatId] = now;
            return false;
        }

        /// <summary>
        /// Показать уведомление о новом сообщении
        /// </summary>
        public async Task ShowMessageNotificationAsync(MessageModel message, string senderName, string? avatarFileId = null, string? avatarUrl = null)
        {
            if (!ShouldShowNotification(message.ChatId))
                return;

            // Антиспам: не показываем больше одного уведомления на чат за 15 секунд
            if (IsThrottled(message.ChatId))
                return;

            var data = CreateNotificationData(message, senderName, avatarFileId, avatarUrl);
            await ShowNotificationAsync(data);
        }

        /// <summary>
        /// Создаёт данные уведомления из сообщения
        /// </summary>
        private NotificationData CreateNotificationData(MessageModel message, string senderName, string? avatarFileId, string? avatarUrl)
        {
            var data = new NotificationData
            {
                ChatId = message.ChatId,
                Title = senderName,
                AvatarFileId = avatarFileId,
                AvatarUrl = avatarUrl,
                LastMessageId = message.MessageId
            };

            // Определяем тип уведомления и формируем текст
            if (message.Attachments != null && message.Attachments.Count > 0)
            {
                var firstAttachment = message.Attachments.First();
                data.FileCount = message.Attachments.Count;

                switch (firstAttachment.Type)
                {
                    case MessageAttachmentType.Image:
                        data.Type = NotificationType.ImageMessage;
                        data.ImageFileId = firstAttachment.FileId;
                        data.ImageUrl = firstAttachment.PreviewUrl;

                        if (!string.IsNullOrEmpty(message.Text))
                            data.Message = message.Text;
                        else if (message.Attachments.Count == 1)
                            data.Message = "📷 Фото";
                        else
                            data.Message = $"📷 {message.Attachments.Count} {GetFilesWordForm(message.Attachments.Count, "фото")}";
                        break;

                    case MessageAttachmentType.Video:
                        data.Type = NotificationType.VideoMessage;
                        if (!string.IsNullOrEmpty(message.Text))
                            data.Message = message.Text;
                        else if (message.Attachments.Count == 1)
                            data.Message = "🎬 Видео";
                        else
                            data.Message = $"🎬 {message.Attachments.Count} видео";
                        break;

                    case MessageAttachmentType.Gif:
                        data.Type = NotificationType.GifMessage;
                        data.ImageFileId = firstAttachment.FileId;
                        data.ImageUrl = firstAttachment.PreviewUrl;
                        if (!string.IsNullOrEmpty(message.Text))
                            data.Message = message.Text;
                        else
                            data.Message = "GIF";
                        break;

                    case MessageAttachmentType.Document:
                    default:
                        data.Type = NotificationType.FileMessage;
                        if (!string.IsNullOrEmpty(message.Text))
                            data.Message = message.Text;
                        else if (message.Attachments.Count == 1)
                            data.Message = "📎 Файл";
                        else
                            data.Message = $"📎 {message.Attachments.Count} {GetFilesWordForm(message.Attachments.Count, "файл")}";
                        break;
                }
            }
            else
            {
                data.Type = NotificationType.TextMessage;
                data.Message = message.Text;
            }

            return data;
        }

        /// <summary>
        /// Получить правильную форму слова для количества файлов
        /// </summary>
        private static string GetFilesWordForm(int count, string baseWord)
        {
            if (baseWord == "фото")
                return "фото"; // "фото" не склоняется

            var lastDigit = count % 10;
            var lastTwoDigits = count % 100;

            if (lastTwoDigits >= 11 && lastTwoDigits <= 19)
                return baseWord + "ов"; // файлов

            return lastDigit switch
            {
                1 => baseWord, // файл
                2 or 3 or 4 => baseWord + "а", // файла
                _ => baseWord + "ов" // файлов
            };
        }

        /// <summary>
        /// Таймаут загрузки изображений для уведомлений (10 секунд)
        /// </summary>
        private static readonly TimeSpan ImageLoadTimeout = TimeSpan.FromSeconds(10);

        /// <summary>
        /// Генерирует Tag для toast-уведомления (макс 64 символа)
        /// </summary>
        private static string GetToastTag(string chatId)
        {
            // Tag ограничен 64 символами в Windows Toast API
            if (chatId.Length <= 64)
                return chatId;
            return chatId[..64];
        }

        /// <summary>
        /// Показать уведомление с учётом текущего режима отображения
        /// </summary>
        public async Task ShowNotificationAsync(NotificationData data)
        {
            try
            {
                var displayMode = GetCurrentDisplayMode();

                // Повторная проверка на случай изменения настройки между вызовами
                if (displayMode == NotificationDisplayMode.Disabled)
                    return;

                var builder = new ToastContentBuilder();

                // Добавляем аргументы для обработки клика
                builder.AddArgument("action", "openChat");
                builder.AddArgument("chatId", data.ChatId);
                builder.AddArgument("messageId", data.LastMessageId.ToString());

                // === Формируем содержимое toast в зависимости от режима ===

                switch (displayMode)
                {
                    case NotificationDisplayMode.HiddenContent:
                        // Скрываем всё: нет аватара, нет имени, нет текста
                        builder.AddText("BarkFluff");
                        builder.AddText("Вам пришло новое сообщение");
                        break;

                    case NotificationDisplayMode.SenderOnly:
                        // Показываем отправителя и аватар, но скрываем текст
                        await AddAvatarToBuilder(builder, data);
                        builder.AddText(data.Title);
                        builder.AddText("Новое сообщение");
                        break;

                    case NotificationDisplayMode.FullTextNoPreview:
                        // Показываем отправителя, аватар и текст, но без превью медиа
                        await AddAvatarToBuilder(builder, data);
                        builder.AddText(data.Title);
                        builder.AddText(data.Message);
                        break;

                    case NotificationDisplayMode.FullWithPreview:
                    default:
                        // Полное отображение: отправитель, аватар, текст и превью медиа
                        await AddAvatarToBuilder(builder, data);
                        builder.AddText(data.Title);
                        await AddMessageContentToBuilder(builder, data);
                        break;
                }

                var toastContent = builder.GetToastContent();
                var toast = new ToastNotification(toastContent.GetXml());

                // Устанавливаем Tag и Group для возможности очистки уведомлений по чату
                toast.Tag = GetToastTag(data.ChatId);
                toast.Group = ToastGroup;

                ToastNotificationManager.CreateToastNotifier(WPF.App.AppUserModelIdPublic).Show(toast);

                // Воспроизводим звук уведомления
                SystemNotificationHelper.PlayNotificationSound();
            }
            catch (Exception ex)
            {
                WPF.App.ErideMessage?.AddMessage(
                    $"Ошибка при показе уведомления: {ex.Message}",
                    new Erida.MessageType { Type = Erida.MessageType.MessageTypeEnum.Error });
            }
        }

        /// <summary>
        /// Добавляет аватар отправителя в toast builder
        /// </summary>
        private async Task AddAvatarToBuilder(ToastContentBuilder builder, NotificationData data)
        {
            string? avatarUri = await GetImageUriForToastAsync(data.AvatarFileId, data.AvatarUrl, FileType.Avatar);
            if (!string.IsNullOrEmpty(avatarUri))
            {
                builder.AddAppLogoOverride(new Uri(avatarUri), ToastGenericAppLogoCrop.Circle);
            }
        }

        /// <summary>
        /// Добавляет текст сообщения и превью медиа в toast builder (полный режим)
        /// </summary>
        private async Task AddMessageContentToBuilder(ToastContentBuilder builder, NotificationData data)
        {
            // Получаем путь к изображению из кеша или скачиваем (если есть)
            string? imageUri = null;
            if ((data.Type == NotificationType.ImageMessage || data.Type == NotificationType.GifMessage)
                && (!string.IsNullOrEmpty(data.ImageFileId) || !string.IsNullOrEmpty(data.ImageUrl)))
            {
                imageUri = await GetImageUriForToastAsync(data.ImageFileId, data.ImageUrl, FileType.Image);
            }

            // Формируем текст сообщения
            string messageText = data.Message;

            // Если изображение не загрузилось, но тип сообщения - картинка, добавляем текстовое обозначение
            if (string.IsNullOrEmpty(imageUri) && (data.Type == NotificationType.ImageMessage || data.Type == NotificationType.GifMessage))
            {
                if (!string.IsNullOrEmpty(data.Message) && !data.Message.StartsWith("📷"))
                {
                    if (data.FileCount > 1)
                        messageText = $"{data.Message} [📷 {data.FileCount} {GetFilesWordForm(data.FileCount, "фото")}]";
                    else
                        messageText = $"{data.Message} [📷 Фото]";
                }
            }

            builder.AddText(messageText);

            // Добавляем превью изображения если получилось загрузить
            if (!string.IsNullOrEmpty(imageUri))
            {
                builder.AddInlineImage(new Uri(imageUri));
            }
        }

        /// <summary>
        /// Очищает все уведомления для указанного чата из Action Center
        /// </summary>
        public void ClearNotificationsForChat(string chatId)
        {
            try
            {
                var tag = GetToastTag(chatId);
                ToastNotificationManager.History.Remove(tag, ToastGroup, WPF.App.AppUserModelIdPublic);

                // Сбрасываем throttle для этого чата
                _lastNotificationTime.TryRemove(chatId, out _);
            }
            catch (Exception ex)
            {
                WPF.App.ErideMessage?.AddMessage(
                    $"Ошибка очистки уведомлений: {ex.Message}",
                    new Erida.MessageType { Type = Erida.MessageType.MessageTypeEnum.Error });
            }
        }

        /// <summary>
        /// Получает URI изображения для использования в Toast уведомлении.
        /// Сначала проверяет кеш, если файл не в кеше - скачивает с таймаутом.
        /// </summary>
        private async Task<string?> GetImageUriForToastAsync(string? fileId, string? url, FileType fileType)
        {
            // Шаг 1: Пробуем получить из кеша по fileId
            if (!string.IsNullOrEmpty(fileId) && _fileCacheService != null)
            {
                if (_fileCacheService.IsFileCached(fileId))
                {
                    var cachedPath = _fileCacheService.GetCachedFilePath(fileId, fileType, url);
                    if (!string.IsNullOrEmpty(cachedPath) && !FileCacheService.IsPlaceholder(cachedPath) && File.Exists(cachedPath))
                    {
                        // Преобразуем относительный путь в абсолютный, затем в file:// URI
                        var absolutePath = Path.GetFullPath(cachedPath);
                        return new Uri(absolutePath).AbsoluteUri;
                    }
                }
            }

            // Шаг 2: Если нет fileId, пробуем извлечь из URL
            var effectiveFileId = fileId;
            if (string.IsNullOrEmpty(effectiveFileId) && !string.IsNullOrEmpty(url))
            {
                effectiveFileId = FileCacheService.ExtractFileIdFromUrl(url);
            }

            // Шаг 3: Пробуем получить из кеша по извлечённому fileId
            if (!string.IsNullOrEmpty(effectiveFileId) && _fileCacheService != null)
            {
                if (_fileCacheService.IsFileCached(effectiveFileId))
                {
                    var cachedPath = _fileCacheService.GetCachedFilePath(effectiveFileId, fileType, url);
                    if (!string.IsNullOrEmpty(cachedPath) && !FileCacheService.IsPlaceholder(cachedPath) && File.Exists(cachedPath))
                    {
                        // Преобразуем относительный путь в абсолютный, затем в file:// URI
                        var absolutePath = Path.GetFullPath(cachedPath);
                        return new Uri(absolutePath).AbsoluteUri;
                    }
                }
            }

            // Шаг 4: Файл не в кеше - скачиваем с таймаутом (как в ToastNotificationService)
            if (!string.IsNullOrEmpty(url) && !FileCacheService.IsPlaceholder(url))
            {
                try
                {
                    using var cts = new CancellationTokenSource(ImageLoadTimeout);
                    var tempUri = await DownloadToTempAsync(url, fileType, cts.Token);
                    return tempUri;
                }
                catch (OperationCanceledException)
                {
                    // Таймаут - возвращаем null
                }
                catch
                {
                    // Другие ошибки - возвращаем null
                }
            }

            return null;
        }

        /// <summary>
        /// Скачивает изображение во временную папку (как fallback если нет в кеше)
        /// </summary>
        private static async Task<string?> DownloadToTempAsync(string url, FileType fileType, CancellationToken cancellationToken)
        {
            try
            {
                using var client = new HttpClient();
                client.Timeout = ImageLoadTimeout;

                var bytes = await client.GetByteArrayAsync(url, cancellationToken);

                var fileName = fileType switch
                {
                    FileType.Avatar => "notification_avatar.png",
                    FileType.Image => "notification_image.png",
                    _ => "notification_file.png"
                };

                var tempPath = Path.Combine(Path.GetTempPath(), "BarkFluff", fileName);
                Directory.CreateDirectory(Path.GetDirectoryName(tempPath)!);

                await File.WriteAllBytesAsync(tempPath, bytes, cancellationToken);

                return new Uri(tempPath).AbsoluteUri;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Обработчик нажатия на уведомление
        /// </summary>
        private void OnToastActivated(ToastNotificationActivatedEventArgsCompat e)
        {
            var args = ToastArguments.Parse(e.Argument);

            if (args.TryGetValue("action", out string? action) && action == "openChat")
            {
                if (args.TryGetValue("chatId", out string? chatId) && !string.IsNullOrEmpty(chatId))
                {
                    args.TryGetValue("messageId", out string? messageIdStr);
                    long.TryParse(messageIdStr, out long messageId);

                    // Очищаем уведомления этого чата из Action Center
                    ClearNotificationsForChat(chatId);

                    var data = new NotificationData
                    {
                        ChatId = chatId,
                        LastMessageId = messageId
                    };

                    // Вызываем событие в UI потоке
                    WpfApplication.Current.Dispatcher.Invoke(() =>
                    {
                        // Активируем окно приложения
                        ActivateMainWindow();

                        // Вызываем событие для открытия чата
                        NotificationClicked?.Invoke(data);
                    });
                }
            }
        }

        /// <summary>
        /// Активировать главное окно приложения
        /// </summary>
        private static void ActivateMainWindow()
        {
            var mainWindow = WPF.App.MessengerWindow;
            if (mainWindow != null)
            {
                if (mainWindow.WindowState == WpfWindowState.Minimized)
                {
                    mainWindow.WindowState = WpfWindowState.Normal;
                }
                mainWindow.Activate();
                mainWindow.Topmost = true;
                mainWindow.Topmost = false;
                mainWindow.Focus();
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            ToastNotificationManagerCompat.OnActivated -= OnToastActivated;
        }
    }
}
