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
            if (attachment == null || string.IsNullOrEmpty(attachment.FileId))
                return;

            StickerImage.FileId = attachment.FileId;
            StickerImage.FileUrl = attachment.PreviewUrl;
            StickerImage.FileType = FileType.Image;
        }
    }
}
