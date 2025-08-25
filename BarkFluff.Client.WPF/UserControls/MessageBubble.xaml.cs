using BarkFluff.WebApi.Core.MessengerData.NonSavedData;

using System.Windows.Controls;
using System.Windows.Data;

namespace BarkFluff.Client.WPF.UserControls
{
    
    public partial class MessageBubble : UserControl
    {
        public MessageBubble(MessageOwner owner, MessageType messageTypes, MessageModel message, bool IsGroup)
        {
            InitializeComponent();
            if (IsGroup)
            {
                SenderName.Visibility = System.Windows.Visibility.Visible;
            }
            else
            {
                SenderName.Visibility = System.Windows.Visibility.Collapsed;
            }
            MessageText.Text = message.Text;
            MessageTime.Text = message.SentAt.ToDateTime().ToString("HH:mm");
            if (owner == MessageOwner.Me)
            {
                MainGrid.HorizontalAlignment = System.Windows.HorizontalAlignment.Right;
            }
            else
            {
                MainGrid.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
            }
            var sizeMessageWidth = CalculateLongestLineWidth(message.Text);
            this.MinWidth = sizeMessageWidth + 50;
        }
        public MessageBubble(string textMessage, (bool sendingRequired , bool isUserId, string recipient) options, List<string> filesId)
        {
            InitializeComponent();
            var sizeMessageWidth = CalculateLongestLineWidth(textMessage);
            this.MinWidth = sizeMessageWidth + 50;
            SenderName.Visibility = System.Windows.Visibility.Collapsed;
            MessageText.Text = textMessage;
            MessageTime.Text = System.DateTime.Now.ToString("HH:mm");
            MainGrid.HorizontalAlignment = System.Windows.HorizontalAlignment.Right;

            if (options.sendingRequired)
            {
                SendMessage(options, textMessage, filesId);
            }
        }
        private async void SendMessage((bool sendingRequired, bool isUserId, string recipient) options, string textMessage, List<string> filesId)
        {
            (bool, string) type = new(options.isUserId, options.recipient);
            var message = new ForwardingLetter { Text = textMessage, FilesId = filesId };
            var response = await App.ServerCommunication.SendMessage(App.GParam, type, message);
            if (!response.IsSuccess)
            {
                App.ErideMessage.AddMessage(response.ErrorMessage, new Services.Erida.MessageType { Type = Services.Erida.MessageType.MessageTypeEnum.Error });
            }
        }
        private int CalculateLongestLineWidth(string textPart)
        {
            if (string.IsNullOrEmpty(textPart)) return 0;

            string[] lines = textPart.Split(new[] { '\n' }, StringSplitOptions.None);

            int longestLineLength = lines.Max(line => line.Length);

            return (longestLineLength > 45 ? 45 : longestLineLength) * 12;
        }
        public enum MessageType
        {
            Text,
            Image,
            Video,
            Gif,
            Document,
        }
        public enum MessageOwner
        {
            Me,
            Interlocutor
        }

        public enum MessageContentType
        {
            Unknown,
            Generic,
            System,
        }
    }
}
