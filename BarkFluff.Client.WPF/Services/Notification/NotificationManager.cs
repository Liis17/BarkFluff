using BarkFluff.Client.WPF.Services.App;
using BarkFluff.Client.WPF.Services.App.Caching;
using BarkFluff.Proto.Shared;
using BarkFluff.WebApi.Core.MessengerData.NonSavedData;

using Microsoft.Toolkit.Uwp.Notifications;

using System.IO;

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
        /// Проверяет, нужно ли показывать уведомление для данного чата
        /// </summary>
        /// <param name="chatId">ID чата</param>
        /// <returns>true, если нужно показать уведомление</returns>
        public bool ShouldShowNotification(string chatId)
        {
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
        /// Показать уведомление о новом сообщении
        /// </summary>
        public async Task ShowMessageNotificationAsync(MessageModel message, string senderName, string? avatarFileId = null, string? avatarUrl = null)
        {
            if (!ShouldShowNotification(message.ChatId))
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
        /// Показать уведомление
        /// </summary>
        public async Task ShowNotificationAsync(NotificationData data)
        {
            try
            {
                var builder = new ToastContentBuilder();

                // Добавляем аргументы для обработки клика
                builder.AddArgument("action", "openChat");
                builder.AddArgument("chatId", data.ChatId);
                builder.AddArgument("messageId", data.LastMessageId.ToString());

                // Добавляем аватар
                var avatarPath = await GetCachedImagePathAsync(data.AvatarFileId, data.AvatarUrl, FileType.Avatar);
                if (!string.IsNullOrEmpty(avatarPath))
                {
                    builder.AddAppLogoOverride(new Uri(avatarPath), ToastGenericAppLogoCrop.Circle);
                }

                // Добавляем заголовок и текст
                builder.AddText(data.Title);
                builder.AddText(data.Message);

                // Добавляем изображение если есть
                if ((data.Type == NotificationType.ImageMessage || data.Type == NotificationType.GifMessage)
                    && (!string.IsNullOrEmpty(data.ImageFileId) || !string.IsNullOrEmpty(data.ImageUrl)))
                {
                    var imagePath = await GetCachedImagePathAsync(data.ImageFileId, data.ImageUrl, FileType.Image);
                    if (!string.IsNullOrEmpty(imagePath) && !FileCacheService.IsPlaceholder(imagePath))
                    {
                        builder.AddInlineImage(new Uri(imagePath));
                    }
                }

                var toastContent = builder.GetToastContent();
                var toast = new ToastNotification(toastContent.GetXml());

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
        /// Получить путь к закешированному изображению
        /// </summary>
        private async Task<string?> GetCachedImagePathAsync(string? fileId, string? url, FileType fileType)
        {
            if (_fileCacheService == null)
                return null;

            // Если есть fileId - используем его
            if (!string.IsNullOrEmpty(fileId))
            {
                var path = await _fileCacheService.GetCachedFilePathAsync(fileId, fileType, url);
                if (!string.IsNullOrEmpty(path) && !FileCacheService.IsPlaceholder(path) && File.Exists(path))
                {
                    if (Uri.TryCreate(path, UriKind.Absolute, out var uri))
                    {
                        return uri.AbsoluteUri;
                    }
                }
            }

            // Пробуем извлечь fileId из URL
            if (!string.IsNullOrEmpty(url))
            {
                var extractedFileId = FileCacheService.ExtractFileIdFromUrl(url);
                if (!string.IsNullOrEmpty(extractedFileId))
                {
                    var path = await _fileCacheService.GetCachedFilePathAsync(extractedFileId, fileType, url);
                    if (!string.IsNullOrEmpty(path) && !FileCacheService.IsPlaceholder(path) && File.Exists(path))
                    {
                        if (Uri.TryCreate(path, UriKind.Absolute, out var uri))
                        {
                            return uri.AbsoluteUri;
                        }
                    }
                }
            }

            return null;
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
