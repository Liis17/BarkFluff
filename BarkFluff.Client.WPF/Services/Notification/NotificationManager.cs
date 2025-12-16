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
        /// Таймаут загрузки изображений для уведомлений (10 секунд)
        /// </summary>
        private static readonly TimeSpan ImageLoadTimeout = TimeSpan.FromSeconds(10);

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

                // Загружаем аватар с таймаутом
                string? avatarPath = null;
                bool avatarLoaded = false;
                try
                {
                    using var avatarCts = new CancellationTokenSource(ImageLoadTimeout);
                    avatarPath = await GetCachedImagePathWithTimeoutAsync(
                        data.AvatarFileId, 
                        data.AvatarUrl, 
                        FileType.Avatar, 
                        avatarCts.Token);
                    avatarLoaded = !string.IsNullOrEmpty(avatarPath) && !FileCacheService.IsPlaceholder(avatarPath);
                }
                catch (OperationCanceledException)
                {
                    // Таймаут - используем заглушку
                    avatarLoaded = false;
                }

                // Добавляем аватар (заглушку если не загрузился)
                if (avatarLoaded && !string.IsNullOrEmpty(avatarPath))
                {
                    builder.AddAppLogoOverride(new Uri(avatarPath), ToastGenericAppLogoCrop.Circle);
                }
                else
                {
                    // Используем заглушку для аватара
                    var placeholderPath = GetLocalPlaceholderPath(FileType.Avatar);
                    if (!string.IsNullOrEmpty(placeholderPath))
                    {
                        builder.AddAppLogoOverride(new Uri(placeholderPath), ToastGenericAppLogoCrop.Circle);
                    }
                }

                // Добавляем заголовок и текст
                builder.AddText(data.Title);

                // Загружаем превью изображения с таймаутом (если есть)
                bool imageLoaded = false;
                string? imagePath = null;
                if ((data.Type == NotificationType.ImageMessage || data.Type == NotificationType.GifMessage)
                    && (!string.IsNullOrEmpty(data.ImageFileId) || !string.IsNullOrEmpty(data.ImageUrl)))
                {
                    try
                    {
                        using var imageCts = new CancellationTokenSource(ImageLoadTimeout);
                        imagePath = await GetCachedImagePathWithTimeoutAsync(
                            data.ImageFileId, 
                            data.ImageUrl, 
                            FileType.Image, 
                            imageCts.Token);
                        imageLoaded = !string.IsNullOrEmpty(imagePath) && !FileCacheService.IsPlaceholder(imagePath);
                    }
                    catch (OperationCanceledException)
                    {
                        // Таймаут - не показываем превью
                        imageLoaded = false;
                    }
                }

                // Формируем текст сообщения
                string messageText = data.Message;
                
                // Если изображение не загрузилось, но тип сообщения - картинка, обновляем текст
                if (!imageLoaded && (data.Type == NotificationType.ImageMessage || data.Type == NotificationType.GifMessage))
                {
                    // Если текст ещё не содержит информацию о картинке - добавляем её
                    if (!string.IsNullOrEmpty(data.Message) && !data.Message.StartsWith("📷"))
                    {
                        if (data.FileCount > 1)
                            messageText = $"{data.Message} [📷 {data.FileCount} {GetFilesWordForm(data.FileCount, "фото")}]";
                        else
                            messageText = $"{data.Message} [📷 Фото]";
                    }
                }

                builder.AddText(messageText);

                // Добавляем превью изображения если загрузилось
                if (imageLoaded && !string.IsNullOrEmpty(imagePath))
                {
                    builder.AddInlineImage(new Uri(imagePath));
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
        /// Получает локальный путь к placeholder-изображению для использования в уведомлениях
        /// </summary>
        private static string? GetLocalPlaceholderPath(FileType fileType)
        {
            try
            {
                // Для уведомлений нужен реальный файл, а не pack:// URI
                // Создаём временный файл из ресурса если нужно
                var tempDir = Path.Combine(Path.GetTempPath(), "BarkFluff", "placeholders");
                Directory.CreateDirectory(tempDir);

                var fileName = fileType switch
                {
                    FileType.Avatar => "avatar_placeholder.png",
                    FileType.Image => "image_placeholder.png",
                    _ => "default_placeholder.png"
                };

                var placeholderPath = Path.Combine(tempDir, fileName);

                // Если файл уже существует - возвращаем его
                if (File.Exists(placeholderPath))
                {
                    return new Uri(placeholderPath).AbsoluteUri;
                }

                // Создаём простой placeholder-файл
                // Это серый квадрат 64x64 пикселя
                CreateSimplePlaceholderImage(placeholderPath);

                if (File.Exists(placeholderPath))
                {
                    return new Uri(placeholderPath).AbsoluteUri;
                }
            }
            catch
            {
                // Игнорируем ошибки
            }

            return null;
        }

        /// <summary>
        /// Создаёт простое placeholder-изображение
        /// </summary>
        private static void CreateSimplePlaceholderImage(string path)
        {
            try
            {
                // Создаём простой PNG 1x1 серый пиксель (минимальный валидный PNG)
                byte[] pngData = new byte[]
                {
                    0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, // PNG signature
                    0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52, // IHDR chunk
                    0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, // 1x1
                    0x08, 0x02, 0x00, 0x00, 0x00, 0x90, 0x77, 0x53, 0xDE, // 8-bit RGB
                    0x00, 0x00, 0x00, 0x0C, 0x49, 0x44, 0x41, 0x54, // IDAT chunk
                    0x08, 0xD7, 0x63, 0x90, 0x90, 0x90, 0x00, 0x00, // compressed data (gray)
                    0x00, 0x04, 0x00, 0x01, 0x11, 0x3D, 0x7D, 0x3E,
                    0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, // IEND chunk
                    0xAE, 0x42, 0x60, 0x82
                };
                File.WriteAllBytes(path, pngData);
            }
            catch
            {
                // Игнорируем ошибки
            }
        }

        /// <summary>
        /// Получить путь к закешированному изображению с поддержкой отмены
        /// </summary>
        private async Task<string?> GetCachedImagePathWithTimeoutAsync(string? fileId, string? url, FileType fileType, CancellationToken cancellationToken)
        {
            if (_fileCacheService == null)
                return null;

            // Сначала проверяем, есть ли файл уже в кеше (синхронно)
            if (!string.IsNullOrEmpty(fileId) && _fileCacheService.IsFileCached(fileId))
            {
                var cachedPath = _fileCacheService.GetCachedFilePath(fileId, fileType, url);
                if (!string.IsNullOrEmpty(cachedPath) && !FileCacheService.IsPlaceholder(cachedPath) && File.Exists(cachedPath))
                {
                    if (Uri.TryCreate(cachedPath, UriKind.Absolute, out var cachedUri))
                    {
                        return cachedUri.AbsoluteUri;
                    }
                }
            }

            // Пробуем извлечь fileId из URL если его нет
            var effectiveFileId = fileId;
            if (string.IsNullOrEmpty(effectiveFileId) && !string.IsNullOrEmpty(url))
            {
                effectiveFileId = FileCacheService.ExtractFileIdFromUrl(url);
            }

            if (string.IsNullOrEmpty(effectiveFileId))
                return null;

            // Проверяем кеш для извлечённого fileId
            if (_fileCacheService.IsFileCached(effectiveFileId))
            {
                var cachedPath = _fileCacheService.GetCachedFilePath(effectiveFileId, fileType, url);
                if (!string.IsNullOrEmpty(cachedPath) && !FileCacheService.IsPlaceholder(cachedPath) && File.Exists(cachedPath))
                {
                    if (Uri.TryCreate(cachedPath, UriKind.Absolute, out var cachedUri))
                    {
                        return cachedUri.AbsoluteUri;
                    }
                }
            }

            // Файл не в кеше - запускаем загрузку с таймаутом
            try
            {
                var downloadTask = _fileCacheService.GetCachedFilePathAsync(effectiveFileId, fileType, url);
                var completedTask = await Task.WhenAny(downloadTask, Task.Delay(Timeout.Infinite, cancellationToken));

                if (completedTask == downloadTask)
                {
                    var path = await downloadTask;
                    if (!string.IsNullOrEmpty(path) && !FileCacheService.IsPlaceholder(path) && File.Exists(path))
                    {
                        if (Uri.TryCreate(path, UriKind.Absolute, out var uri))
                        {
                            return uri.AbsoluteUri;
                        }
                    }
                }
                else
                {
                    // Отмена по таймауту
                    cancellationToken.ThrowIfCancellationRequested();
                }
            }
            catch (OperationCanceledException)
            {
                throw; // Пробрасываем дальше
            }
            catch
            {
                // Игнорируем другие ошибки
            }

            return null;
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
