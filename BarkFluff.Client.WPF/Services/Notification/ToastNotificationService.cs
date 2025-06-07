using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Windows;
using Windows.UI.Notifications;
using Microsoft.Toolkit.Uwp.Notifications;

using Windows.Data.Xml.Dom;
using System.IO;
using System.Net.Http;

namespace BarkFluff.Client.WPF.Services.Notification
{
    public class ToastNotificationService
    {
        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int SetCurrentProcessExplicitAppUserModelID(string AppID);

        private const string AppId = "com.barkfluff.messenger";

        public ToastNotificationService()
        {
            SetCurrentProcessExplicitAppUserModelID(AppId);
        }

        public async void ShowToast(string title, string message, string avatarUrl)
        {
            using var client = new HttpClient();
            var bytes = await client.GetByteArrayAsync(avatarUrl);
            var tempPath = Path.Combine(Path.GetTempPath(), "avatar_barkfluff_messenger.png");
            await File.WriteAllBytesAsync(tempPath, bytes);
            string localUri = new Uri(tempPath).AbsoluteUri;

            var toastContent = new ToastContentBuilder()
                 .AddAppLogoOverride(new Uri(localUri), ToastGenericAppLogoCrop.Circle) 
                .AddText(title)
                .AddText(message)
                .GetToastContent();

            var toast = new ToastNotification(toastContent.GetXml());

            var notifier = ToastNotificationManager.CreateToastNotifier(AppId);
            notifier.Show(toast);
        }

        public async void ShowToastWithImage(string userName, string message, string avatarUrl, string imageUrl)
        {
            using var client = new HttpClient();

            // Скачиваем аватар
            var avatarBytes = await client.GetByteArrayAsync(avatarUrl);
            var avatarPath = Path.Combine(Path.GetTempPath(), "avatar_temp.png");
            await File.WriteAllBytesAsync(avatarPath, avatarBytes);
            string avatarUri = new Uri(avatarPath).AbsoluteUri;

            // Скачиваем вложенное изображение (контент) 
            var imageBytes = await client.GetByteArrayAsync(imageUrl);
            var imagePath = Path.Combine(Path.GetTempPath(), "image_temp.png");
            await File.WriteAllBytesAsync(imagePath, imageBytes);
            string imageUri = new Uri(imagePath).AbsoluteUri;

            // Собираем уведомление
            var toastContent = new ToastContentBuilder()
                .AddAppLogoOverride(new Uri(avatarUri), ToastGenericAppLogoCrop.Circle)
                .AddText(userName)              
                .AddText(message)              
                .AddInlineImage(new Uri(imageUri))
                .GetToastContent();

            var toast = new ToastNotification(toastContent.GetXml());
            var notifier = ToastNotificationManager.CreateToastNotifier(AppId);
            notifier.Show(toast);
        }
    }
}
