using BarkFluff.Client.WPF.Services.App.Caching;
using BarkFluff.WebApi.Core.MessengerData.NonSavedData;

using System.Windows.Controls;

namespace BarkFluff.Client.WPF.UserControls.MessageContent
{
    public partial class StickerMessageContent : UserControl
    {
        public StickerMessageContent()
        {
            InitializeComponent();
        }

        public StickerMessageContent(AttachmentsModel attachment) : this()
        {
            if (string.IsNullOrEmpty(attachment.FileId))
                return;

            // Используем только FileId — превью-файлы не зарегистрированы в БД
            // и недоступны по прямой ссылке. FileCacheService сам получит
            // временную ссылку через GetTempDownloadUrl gRPC.
            StickerImage.FileId = attachment.FileId;
            StickerImage.FileType = FileType.Image;
        }
    }
}
