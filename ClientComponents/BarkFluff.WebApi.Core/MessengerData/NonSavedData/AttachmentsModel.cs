using System;
using System.Collections.Generic;
using System.Text;

namespace BarkFluff.WebApi.Core.MessengerData.NonSavedData
{
    public class AttachmentsModel
    {
        public long Id { get; set; } = 0;
        public BarkFluff.Proto.Shared.MessageAttachmentType Type { get; set; } = BarkFluff.Proto.Shared.MessageAttachmentType.Unknown;
        public string PreviewUrl { get; set; } = string.Empty;
        public string FileId { get; set; } = string.Empty;
        public string PreviewFileId { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public long Size { get; set; } = 0;
        public AttachmentsModel() { }
    }
}
