using BarkFluff.Client.WPF.Services.App.Caching;
using BarkFluff.Client.WPF.UserControls.MessageContent;
using BarkFluff.WebApi.Core.MessengerData.NonSavedData;

using Google.Protobuf.WellKnownTypes;

using System.Windows;
using System.Windows.Controls;

namespace BarkFluff.Client.WPF.UserControls
{
    public partial class MessageBubble : UserControl
    {
        #region Constants
        private const int MAX_BUBBLE_WIDTH = 600;
        private const int MAX_CHARS_PER_LINE = 45;
        private const int CHAR_WIDTH_APPROX = 12;
        private const int MESSAGE_LIMIT = 4096;
        private const int MIN_WIDTH_PADDING = 50;
        private const int IMAGE_MAX_WIDTH = 400;
        private const int IMAGE_MAX_HEIGHT = 300;
        private const string DEFAULT_SEND_ERROR = "Ошибка отправки сообщения";
        #endregion Constants

        public string MessageId { get; set; } = string.Empty;
        public Timestamp SentAt { get; set; } = new Timestamp();
        private MessageType _messageType = MessageType.Text;
        private TextMessageContent? _textContent;

        public MessageBubble(MessageOwner owner, MessageType messageType, MessageModel message, bool isGroup)
        {
            InitializeComponent();

            if (message == null)
            {
                return;
            }

            _messageType = messageType;

            if (isGroup && owner == MessageOwner.Interlocutor)
            {
                SenderName.Visibility = Visibility.Visible;
            }
            else
            {
                SenderName.Visibility = Visibility.Collapsed;
            }

            SetupContent(message, messageType);

            if (message.SentAt != null)
            {
                MessageTime.Text = message.SentAt.ToDateTime().ToString("HH:mm");
                SentAt = message.SentAt;
            }
            else
            {
                MessageTime.Text = DateTime.Now.ToString("HH:mm");
            }

            MessageId = message.MessageId.ToString();
            ThemedConfirm(owner);
        }

        public MessageBubble(string textMessage, (bool sendingRequired, bool isUserId, string recipient) options, List<string> filesId)
        {
            InitializeComponent();

            var sizeMessageWidth = CalculateLongestLineWidth(textMessage);
            this.MinWidth = sizeMessageWidth + MIN_WIDTH_PADDING;
            SenderName.Visibility = Visibility.Collapsed;

            // Create text content control
            _textContent = new TextMessageContent(textMessage);
            TextContentPresenter.Content = _textContent;

            MessageTime.Text = DateTime.Now.ToString("HH:mm");
            ThemedConfirm(MessageOwner.Me);

            if (options.sendingRequired)
            {
                SendMessage(options, textMessage ?? string.Empty, filesId ?? new List<string>());
            }
        }

        private void SetupContent(MessageModel message, MessageType messageType)
        {
            switch (messageType)
            {
                case MessageType.Image:
                case MessageType.Gif:
                    SetupImageContent(message, messageType);
                    break;
                case MessageType.Document:
                    SetupDocumentContent(message);
                    break;
                case MessageType.Video:
                    SetupVideoContent(message);
                    break;
                case MessageType.Text:
                default:
                    SetupTextContent(message);
                    break;
            }
        }

        private void SetupTextContent(MessageModel message)
        {
            _textContent = new TextMessageContent(message.Text);
            TextContentPresenter.Content = _textContent;
            MediaContentPresenter.Content = null;

            var sizeMessageWidth = CalculateLongestLineWidth(message.Text);
            this.MinWidth = sizeMessageWidth + MIN_WIDTH_PADDING;
        }

        private void SetupImageContent(MessageModel message, MessageType messageType)
        {
            var attachment = message.Attachments?.FirstOrDefault();
            if (attachment == null || string.IsNullOrEmpty(attachment.FileId))
            {
                SetupTextContent(message);
                return;
            }

            // Set up text content if present
            if (!string.IsNullOrEmpty(message.Text))
            {
                _textContent = new TextMessageContent(message.Text);
                TextContentPresenter.Content = _textContent;
            }
            else
            {
                TextContentPresenter.Content = null;
            }

            // Set up image content
            var fileType = messageType == MessageType.Gif ? FileType.Gif : FileType.Image;
            var imageContent = new ImageMessageContent(attachment.FileId, attachment.PreviewUrl, fileType);
            MediaContentPresenter.Content = imageContent;

            this.MinWidth = IMAGE_MAX_WIDTH;
        }

        private void SetupDocumentContent(MessageModel message)
        {
            var attachment = message.Attachments?.FirstOrDefault();
            if (attachment == null || string.IsNullOrEmpty(attachment.FileId))
            {
                SetupTextContent(message);
                return;
            }

            // Set up text content if present
            if (!string.IsNullOrEmpty(message.Text))
            {
                _textContent = new TextMessageContent(message.Text);
                TextContentPresenter.Content = _textContent;
            }
            else
            {
                TextContentPresenter.Content = null;
            }

            // Set up document content
            var documentContent = new DocumentMessageContent(attachment.FileId, attachment.PreviewUrl, attachment.Size);
            MediaContentPresenter.Content = documentContent;

            var sizeMessageWidth = CalculateLongestLineWidth(message.Text);
            this.MinWidth = Math.Max(sizeMessageWidth + MIN_WIDTH_PADDING, 200);
        }

        private void SetupVideoContent(MessageModel message)
        {
            var attachment = message.Attachments?.FirstOrDefault();
            if (attachment == null || string.IsNullOrEmpty(attachment.FileId))
            {
                SetupTextContent(message);
                return;
            }

            // Set up text content if present
            if (!string.IsNullOrEmpty(message.Text))
            {
                _textContent = new TextMessageContent(message.Text);
                TextContentPresenter.Content = _textContent;
            }
            else
            {
                TextContentPresenter.Content = null;
            }

            // Set up video content
            var videoContent = new VideoMessageContent(attachment.FileId, attachment.PreviewUrl);
            MediaContentPresenter.Content = videoContent;

            this.MinWidth = IMAGE_MAX_WIDTH;
        }

        private async void SendMessage((bool sendingRequired, bool isUserId, string recipient) options, string textMessage, List<string> filesId)
        {
            if (string.IsNullOrEmpty(textMessage) && (filesId == null || filesId.Count == 0))
            {
                return;
            }

            (bool, string) type = new(options.isUserId, options.recipient);
            var letter = new ForwardingLetter { Text = textMessage, FilesId = filesId ?? new List<string>() };
            var response = await App.ServerCommunication.SendMessage(App.GParam, type, letter);
            if (!response.error.IsSuccess)
            {
                var errorMsg = response.error.ErrorMessage ?? DEFAULT_SEND_ERROR;
                var msgType = new Services.Erida.MessageType
                {
                    Type = Services.Erida.MessageType.MessageTypeEnum.Error
                };
                App.ErideMessage.AddMessage(errorMsg, msgType);
            }
            else if (response.message != null)
            {
                MessageId = response.message.MessageId.ToString();
                App.CacheManager.SaveMessage(
                    response.message.ChatId,
                    string.Empty,
                    response.message,
                    MessageOperation.Added);
            }
        }

        private void ThemedConfirm(MessageOwner owner)
        {
            if (owner == MessageOwner.Me)
            {
                this.HorizontalAlignment = HorizontalAlignment.Right;
                MainGrid.HorizontalAlignment = HorizontalAlignment.Right;
                MessageBorder.Style = (Style)FindResource("OutgoingMessageStyle");
            }
            else
            {
                this.HorizontalAlignment = HorizontalAlignment.Left;
                MainGrid.HorizontalAlignment = HorizontalAlignment.Left;
                MessageBorder.Style = (Style)FindResource("IncomingMessageStyle");
            }
        }

        private int CalculateLongestLineWidth(string? textPart)
        {
            if (string.IsNullOrEmpty(textPart))
            {
                return 0;
            }

            string[] lines = textPart.Split(new[] { '\n' }, StringSplitOptions.None);

            int longestLineLength = lines.Max(line => line.Length);

            return Math.Min(longestLineLength, MAX_CHARS_PER_LINE) * CHAR_WIDTH_APPROX;
        }

        #region Context Menu Handlers

        private void OnReplyClick(object sender, RoutedEventArgs e)
        {
            // TODO: Implement
        }

        private void OnForwardClick(object sender, RoutedEventArgs e)
        {
            // TODO: Implement
        }

        private void OnCopyClick(object sender, RoutedEventArgs e)
        {
            // TODO: Implement
            if (_textContent != null)
            {
                _textContent.CopySelectedText();
            }
        }

        private void OnSelectAllTextClick(object sender, RoutedEventArgs e)
        {
            // TODO: Implement
            if (_textContent != null)
            {
                _textContent.SelectAll();
            }
        }

        private void OnPinClick(object sender, RoutedEventArgs e)
        {
            // TODO: Implement
        }

        private void OnAddToFavoritesClick(object sender, RoutedEventArgs e)
        {
            // TODO: Implement
        }

        private void OnEditClick(object sender, RoutedEventArgs e)
        {
            // TODO: Implement
        }

        private void OnDeleteClick(object sender, RoutedEventArgs e)
        {
            // TODO: Implement
        }

        #endregion Context Menu Handlers

        #region Enums
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
        #endregion Enums
    }
}
