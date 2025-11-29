using Microsoft.Toolkit.Uwp.Notifications;

using System;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

using Windows.UI.Notifications;

namespace BarkFluff.Client.WPF.Services.Notification
{
    public class ToastNotificationService
    {
        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int SetCurrentProcessExplicitAppUserModelID(string AppID);

        public ToastNotificationService()
        {
            SetCurrentProcessExplicitAppUserModelID(BarkFluff.Client.WPF.App.AppUserModelIdPublic);
        }

        public async Task ShowToastAsync(string title, string message, string? avatarUrl = null)
        {
            try
            {
                var builder = new ToastContentBuilder();

                if (!string.IsNullOrWhiteSpace(avatarUrl))
                {
                    var localUri = await DownloadTempImageAsync(avatarUrl, "avatar_barkfluff_messenger.png");
                    if (localUri != null)
                        builder.AddAppLogoOverride(new Uri(localUri), ToastGenericAppLogoCrop.Circle);
                }

                builder.AddText(title).AddText(message);

                var toastContent = builder.GetToastContent();
                var toast = new ToastNotification(toastContent.GetXml());
                ToastNotificationManager.CreateToastNotifier(BarkFluff.Client.WPF.App.AppUserModelIdPublic).Show(toast);
            }
            catch { }
        }

        public async Task ShowToastWithImageAsync(string userName, string message, string avatarUrl, string imageUrl)
        {
            try
            {
                var builder = new ToastContentBuilder();
                var avatarLocal = await DownloadTempImageAsync(avatarUrl, "avatar_temp.png");
                if (avatarLocal != null)
                    builder.AddAppLogoOverride(new Uri(avatarLocal), ToastGenericAppLogoCrop.Circle);

                var imageLocal = await DownloadTempImageAsync(imageUrl, "image_temp.png");
                if (imageLocal != null)
                    builder.AddInlineImage(new Uri(imageLocal));

                builder.AddText(userName).AddText(message);

                var toastContent = builder.GetToastContent();
                var toast = new ToastNotification(toastContent.GetXml());
                ToastNotificationManager.CreateToastNotifier(BarkFluff.Client.WPF.App.AppUserModelIdPublic).Show(toast);
            }
            catch { }
        }

        private static async Task<string?> DownloadTempImageAsync(string url, string fileName)
        {
            try
            {
                using var client = new HttpClient();
                var bytes = await client.GetByteArrayAsync(url);
                var tempPath = Path.Combine(Path.GetTempPath(), fileName);
                await File.WriteAllBytesAsync(tempPath, bytes);
                return new Uri(tempPath).AbsoluteUri;
            }
            catch
            {
                return null;
            }
        }
    }
}
