using BarkFluff.Client.WPF.Services.App.Caching;
using BarkFluff.Client.WPF.Services.QR;

using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BarkFluff.Client.WPF.UserControls
{
    public partial class ProfileShare : UserControl
    {
        private string _barkflufffPrefix = "https://barkfluff.com/";

        public ProfileShare(string username)
        {
            InitializeComponent();
            Loaded += ProfileShare_Loaded;

            Username = username;
        }

        private async void ProfileShare_Loaded(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(Username))
                return;

            // Формируем URL для QR кода
            string qrUrl = _barkflufffPrefix + Username;

            // Получаем путь к аватару пользователя
            string logoPath = await GetUserAvatarPath();

            // Генерируем QR код
            try
            {
                var color1 = System.Drawing.Color.FromArgb(255, 63, 94, 251); // Пурпурный
                var color2 = System.Drawing.Color.FromArgb(255, 252, 70, 107); // Розовый
                var qrCodeBitmap = RoundedQrGenerator.GenerateRoundedQrBitmap(qrUrl, color1, color2, logoPath);
                QrCodeSource = qrCodeBitmap;
            }
            catch (Exception ex)
            {
                App.ErideMessage.AddMessage(
                    $"Ошибка генерации QR кода: {ex.Message}",
                    new Services.Erida.MessageType { Type = Services.Erida.MessageType.MessageTypeEnum.Error });
            }
        }

        /// <summary>
        /// Получает путь к аватару текущего пользователя из кеша
        /// </summary>
        private async Task<string> GetUserAvatarPath()
        {
            try
            {
                // Получаем URL аватара текущего пользователя
                string avatarUrl = App.GParam.PictureUrl;

                if (string.IsNullOrEmpty(avatarUrl))
                {
                    // Используем placeholder, если аватар не установлен
                    return GetPlaceholderPath();
                }

                // Извлекаем fileId из URL
                string fileId = FileCacheService.ExtractFileIdFromUrl(avatarUrl);

                if (string.IsNullOrEmpty(fileId))
                {
                    return GetPlaceholderPath();
                }

                // Получаем путь из кеша (если файла нет, он будет загружен асинхронно)
                string cachedPath = await App.FileCacheService.GetCachedFilePathAsync(fileId, FileType.Avatar, avatarUrl);

                // Проверяем, что это не placeholder
                if (FileCacheService.IsPlaceholder(cachedPath) || !File.Exists(cachedPath))
                {
                    return GetPlaceholderPath();
                }

                return cachedPath;
            }
            catch (Exception ex)
            {
                App.ErideMessage.AddMessage(
                    $"Ошибка получения аватара: {ex.Message}",
                    new Services.Erida.MessageType { Type = Services.Erida.MessageType.MessageTypeEnum.Warning });
                return GetPlaceholderPath();
            }
        }

        /// <summary>
        /// Возвращает путь к placeholder-аватару
        /// </summary>
        private string GetPlaceholderPath()
        {
            // Преобразуем pack:// URI в абсолютный путь
            var packUri = new Uri(FileCacheService.DefaultPlaceholder);
            var absolutePath = System.Windows.Application.GetResourceStream(packUri)?.Stream;

            if (absolutePath != null)
            {
                // Сохраняем во временный файл для использования в QR коде
                string tempPath = Path.Combine(Path.GetTempPath(), "placeholder_avatar.png");
                using (var fileStream = File.Create(tempPath))
                {
                    absolutePath.CopyTo(fileStream);
                }
                return tempPath;
            }

            return null;
        }

        #region Dependency Properties

        // 1. Свойство для имени пользователя (напр. @barkfluff)
        public static readonly DependencyProperty UsernameProperty =
            DependencyProperty.Register("Username", typeof(string), typeof(ProfileShare), new PropertyMetadata(string.Empty));

        public string Username
        {
            get { return (string)GetValue(UsernameProperty); }
            set { SetValue(UsernameProperty, value); }
        }

        // 3. Свойство для картинки Аватара
        public static readonly DependencyProperty AvatarSourceProperty =
            DependencyProperty.Register("AvatarSource", typeof(ImageSource), typeof(ProfileShare), new PropertyMetadata(null));

        public ImageSource AvatarSource
        {
            get { return (ImageSource)GetValue(AvatarSourceProperty); }
            set { SetValue(AvatarSourceProperty, value); }
        }

        // 4. Свойство для картинки QR кода
        // (Обычно QR генерируется в ViewModel и передается сюда как Bitmap/ImageSource)
        public static readonly DependencyProperty QrCodeSourceProperty =
            DependencyProperty.Register("QrCodeSource", typeof(ImageSource), typeof(ProfileShare), new PropertyMetadata(null));

        public ImageSource QrCodeSource
        {
            get { return (ImageSource)GetValue(QrCodeSourceProperty); }
            set { SetValue(QrCodeSourceProperty, value); }
        }

        #endregion
    }
}