namespace BarkFluff.Files.Domain;

public enum UploadFileType
{
    Unknown = 0,

    UserAvatar = 1,

    MessageAttachmentImage = 2,

    MessageAttachmentVideo = 3,

    MessageAttachmentGif = 4,

    MessageAttachmentDocument = 5,

    ChatPicture = 6,

    MessageAttachmentAudio = 7,

    MessageAttachmentVoice = 8,

    MessageAttachmentSticker = 9,
}