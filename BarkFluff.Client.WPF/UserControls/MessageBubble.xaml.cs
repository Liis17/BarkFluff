using BarkFluff.WebApi.Core.MessengerData.NonSavedData;

using System.Windows.Controls;

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
        }
        public MessageBubble(string textMessage)
        {
            InitializeComponent();
            SenderName.Visibility = System.Windows.Visibility.Collapsed;
            MessageText.Text = textMessage;
            MessageTime.Text = System.DateTime.Now.ToString("HH:mm");
            MainGrid.HorizontalAlignment = System.Windows.HorizontalAlignment.Right;
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
