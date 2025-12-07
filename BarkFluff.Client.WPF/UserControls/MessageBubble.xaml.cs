using BarkFluff.Client.WPF.Services.App.Caching;
using BarkFluff.Client.WPF.UserControls.MessageContent;
using BarkFluff.WebApi.Core.MessengerData.NonSavedData;

using Google.Protobuf.WellKnownTypes;

using System.Linq;
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
        private MessageOwner _owner;
        public List<long> ReadBy { get; private set; } = new List<long>();
        public long SenderId { get; private set; }
        public bool IsPending { get; private set; } = false;
        private List<string> _pendingFileIds = new List<string>();

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
            SenderId = message.SenderId;
            ReadBy = message.ReadBy ?? new List<long>();
            _owner = owner;
            ThemedConfirm(owner);
            UpdateReadStatus();
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
            SenderId = App.GParam.UserId;
            ReadBy = new List<long>();
            _owner = MessageOwner.Me;
            ThemedConfirm(MessageOwner.Me);
            
            // Set pending state if files need to be uploaded
            IsPending = filesId != null && filesId.Count > 0;
            _pendingFileIds = filesId ?? new List<string>();
            
            UpdateReadStatus();

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
                
                // Mark as sent (no longer pending)
                MarkAsSent();
                
                App.CacheManager.SaveMessage(
                    response.message.ChatId,
                    string.Empty,
                    response.message,
                    MessageOperation.Added);
            }
        }

        /// <summary>
        /// Updates the read status indicator based on current ReadBy list
        /// </summary>
        private void UpdateReadStatus()
        {
            // Only show read status for own messages (outgoing)
            if (_owner != MessageOwner.Me)
            {
                ReadStatus.Visibility = Visibility.Collapsed;
                return;
            }

            ReadStatus.Visibility = Visibility.Visible;

            // Show pending indicator (clock icon) if message is still uploading
            if (IsPending)
            {
                // Show clock icon - message is being uploaded
                PendingIcon.Visibility = Visibility.Visible;
                SingleCheckmark.Visibility = Visibility.Collapsed;
                DoubleCheckmark.Visibility = Visibility.Collapsed;
                return;
            }

            // Hide pending icon once upload completes
            PendingIcon.Visibility = Visibility.Collapsed;

            // Check if message has been read by others (anyone besides the sender)
            var readByOthers = ReadBy.Any(id => id != SenderId);

            // Update checkmark style based on read status
            if (readByOthers)
            {
                // Double checkmark - message read
                SingleCheckmark.Visibility = Visibility.Visible;
                DoubleCheckmark.Visibility = Visibility.Visible;
                SingleCheckmark.Opacity = 1.0;
                DoubleCheckmark.Opacity = 1.0;
            }
            else if (!string.IsNullOrEmpty(MessageId))
            {
                // Single checkmark - message sent but not read
                SingleCheckmark.Visibility = Visibility.Visible;
                DoubleCheckmark.Visibility = Visibility.Collapsed;
                SingleCheckmark.Opacity = 0.7;
            }
            else
            {
                // No checkmark - message not sent yet
                SingleCheckmark.Visibility = Visibility.Collapsed;
                DoubleCheckmark.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>
        /// Marks message as sent (no longer pending) and updates UI
        /// </summary>
        public void MarkAsSent()
        {
            if (IsPending)
            {
                IsPending = false;
                Dispatcher.Invoke(() => UpdateReadStatus());
            }
        }

        /// <summary>
        /// Updates the ReadBy list and refreshes the UI
        /// </summary>
        public void UpdateReadByList(List<long> newReadBy)
        {
            if (newReadBy == null) return;
            
            ReadBy = newReadBy;
            Dispatcher.Invoke(() => UpdateReadStatus());
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
            // TODO: Implement reply functionality
        }

        private void OnForwardClick(object sender, RoutedEventArgs e)
        {
            // TODO: Implement forward functionality
        }

        private void OnCopyClick(object sender, RoutedEventArgs e)
        {
            // TODO: Implement copy for other content types (images, documents, videos)
            if (_textContent != null)
            {
                _textContent.CopySelectedText();
            }
        }

        private void OnSelectAllTextClick(object sender, RoutedEventArgs e)
        {
            // TODO: Implement select all for other content types or disable for non-text messages
            if (_textContent != null)
            {
                _textContent.SelectAll();
            }
        }

        private void OnPinClick(object sender, RoutedEventArgs e)
        {
            // TODO: Implement pin functionality
        }

        private void OnAddToFavoritesClick(object sender, RoutedEventArgs e)
        {
            // TODO: Implement add to favorites functionality
        }

        private void OnEditClick(object sender, RoutedEventArgs e)
        {
            // TODO: Implement edit functionality
        }

        private void OnDeleteClick(object sender, RoutedEventArgs e)
        {
            // TODO: Implement delete functionality
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
