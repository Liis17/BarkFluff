namespace BarkFluff.WebApi.Core.MessengerData.NonSavedData
{
    public class ChatInfo
    {
        public ChatInfo() { }

        public string ChatId = string.Empty;
        public List<long> Members = new List<long>();
        public string Title = string.Empty;
        public long CountUnread = 0;
        public long FirstUnreadId = 0;
        public bool IsGroup = false;
        public long LastMessageId = 0;
        public string Picture = string.Empty;
    }
}
