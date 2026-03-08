namespace BarkFluff.WebApi.Core.MessengerData.NonSavedData
{
    public class ChatCacheClass
    {
        public string ChatId { get; set; } = string.Empty;
        public string ChatName { get; set; } = string.Empty;
        public string AvatarFileId { get; set; } = string.Empty;
        public MessageModel? LastMessage { get; set; } = null;
    }
}
