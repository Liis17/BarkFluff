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
        private const int MIN_TEXT_WIDTH = 150;
        private const int IMAGE_MAX_WIDTH = 400;
        private const int IMAGE_MAX_HEIGHT = 300;
        private const string DEFAULT_SEND_ERROR = "Ошибка отправки сообщения";

        // Status indicator opacity values
        private const double STATUS_READ_OPACITY = 1.0;
        private const double STATUS_SENT_OPACITY = 0.7;
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
        private List<UploadingAttachmentItem> _uploadingItems = new List<UploadingAttachmentItem>();

        private enum AttachmentDisplayMode
        {
            PureImage,   // Все Image/Gif → MultiImageGrid
            PureVideo,   // Все Video → MultiVideoGrid
            PureAudio,   // Все Audio → список AudioMessageContent
            Mixed        // Смешанные типы или есть Document → MultiDocumentList
        }

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
                MessageTime.Text = message.SentAt.ToDateTime().ToLocalTime().ToString("HH:mm");
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
            var attachments = message.Attachments?.ToList() ?? new List<AttachmentsModel>();

            // Handle multiple attachments
            if (attachments.Count > 1)
            {
                SetupMultipleAttachments(message, attachments);
            }
            else
            {
                // Single attachment or text-only message - use existing logic
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
                    case MessageType.Audio:
                        SetupAudioContent(message);
                        break;
                    case MessageType.Voice:
                        SetupVoiceContent(message);
                        break;
                    case MessageType.Sticker:
                        SetupStickerContent(message);
                        break;
                    case MessageType.Text:
                    default:
                        SetupTextContent(message);
                        break;
                }
            }
        }

        private void SetupMultipleAttachments(MessageModel message, List<AttachmentsModel> attachments)
        {
            // Настроить текстовый контент если есть
            if (!string.IsNullOrEmpty(message.Text))
            {
                _textContent = new TextMessageContent(message.Text);
                TextContentPresenter.Content = _textContent;
            }
            else
            {
                TextContentPresenter.Content = null;
            }

            // Определить режим отображения на основе ВСЕХ вложений
            var displayMode = DetermineDisplayMode(attachments);

            switch (displayMode)
            {
                case AttachmentDisplayMode.PureImage:
                    // Все вложения - изображения/gif → сетка изображений
                    var imageGrid = new MultiImageGrid();
                    imageGrid.SetImages(attachments);  // Передаем ВСЕ вложения
                    MediaContentPresenter.Content = imageGrid;
                    SetMediaContentMargin(false);
                    this.MinWidth = IMAGE_MAX_WIDTH;
                    break;

                case AttachmentDisplayMode.PureVideo:
                    // Все вложения - видео → сетка видео
                    var videoGrid = new MultiVideoGrid();
                    videoGrid.SetVideos(attachments);  // Передаем ВСЕ вложения
                    MediaContentPresenter.Content = videoGrid;
                    SetMediaContentMargin(false);
                    this.MinWidth = IMAGE_MAX_WIDTH;
                    break;

                case AttachmentDisplayMode.PureAudio:
                    // Все вложения - аудио → список AudioMessageContent
                    var audioPanel = new StackPanel();
                    foreach (var att in attachments)
                    {
                        var audioItem = new AudioMessageContent(att);
                        audioItem.Margin = new Thickness(0, 0, 0, 4);
                        audioPanel.Children.Add(audioItem);
                    }
                    MediaContentPresenter.Content = audioPanel;
                    SetMediaContentMargin(true);
                    this.MinWidth = 320;
                    break;

                case AttachmentDisplayMode.Mixed:
                    // Смешанные типы или есть документы → список файлов
                    var list = new MultiDocumentList();
                    list.SetAttachments(attachments);  // ← НОВЫЙ метод (был SetDocuments)
                    MediaContentPresenter.Content = list;
                    SetMediaContentMargin(true);
                    var sizeMessageWidth = CalculateLongestLineWidth(message.Text);
                    this.MinWidth = Math.Max(sizeMessageWidth + MIN_WIDTH_PADDING, 250);
                    break;

                default:
                    // Fallback на текстовый контент
                    SetupTextContent(message);
                    break;
            }
        }

        private AttachmentDisplayMode DetermineDisplayMode(List<AttachmentsModel> attachments)
        {
            bool hasImage = false;
            bool hasVideo = false;
            bool hasDocument = false;
            bool hasAudio = false;
            bool hasVoice = false;

            foreach (var attachment in attachments)
            {
                if (IsImageType(attachment))
                    hasImage = true;
                else if (IsVideoType(attachment))
                    hasVideo = true;
                else if (IsAudioType(attachment))
                    hasAudio = true;
                else if (IsVoiceType(attachment))
                    hasVoice = true;
                else if (IsDocumentType(attachment))
                    hasDocument = true;
            }

            if (hasDocument || hasVoice)
                return AttachmentDisplayMode.Mixed;

            // Один тип контента
            int typeCount = (hasImage ? 1 : 0) + (hasVideo ? 1 : 0) + (hasAudio ? 1 : 0);
            if (typeCount > 1)
                return AttachmentDisplayMode.Mixed;

            if (hasImage)
                return AttachmentDisplayMode.PureImage;

            if (hasVideo)
                return AttachmentDisplayMode.PureVideo;

            if (hasAudio)
                return AttachmentDisplayMode.PureAudio;

            return AttachmentDisplayMode.Mixed;
        }

        private bool IsImageType(AttachmentsModel attachment)
        {
            return attachment.Type == Proto.Shared.MessageAttachmentType.Image ||
                   attachment.Type == Proto.Shared.MessageAttachmentType.Gif;
        }

        private bool IsDocumentType(AttachmentsModel attachment)
        {
            return attachment.Type == Proto.Shared.MessageAttachmentType.Document;
        }

        private bool IsVideoType(AttachmentsModel attachment)
        {
            return attachment.Type == Proto.Shared.MessageAttachmentType.Video;
        }

        private bool IsAudioType(AttachmentsModel attachment)
        {
            return attachment.Type == Proto.Shared.MessageAttachmentType.Audio;
        }

        /// <summary>
        /// Sets the MediaContentPresenter margin based on whether content is a document/file
        /// </summary>
        private void SetMediaContentMargin(bool isDocument)
        {
            MediaContentPresenter.Margin = isDocument
                ? new Thickness(8, 8, 8, 5)
                : new Thickness(0, 0, 0, 5);
        }

        private void SetupTextContent(MessageModel message)
        {
            _textContent = new TextMessageContent(message.Text);
            TextContentPresenter.Content = _textContent;
            MediaContentPresenter.Content = null;
            SetMediaContentMargin(false);

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

            // Set up image content - pass the full attachment model
            var imageContent = new ImageMessageContent(attachment);
            MediaContentPresenter.Content = imageContent;
            SetMediaContentMargin(false);

            this.MinWidth = IMAGE_MAX_WIDTH;
            this.MinHeight = IMAGE_MAX_HEIGHT;
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
            var documentContent = new DocumentMessageContent(attachment);
            MediaContentPresenter.Content = documentContent;
            SetMediaContentMargin(true);

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

            // Set up video content - передаем ПОЛНЫЙ attachment
            var videoContent = new VideoMessageContent(attachment);
            MediaContentPresenter.Content = videoContent;
            SetMediaContentMargin(false);

            this.MinWidth = IMAGE_MAX_WIDTH;
        }

        private void SetupAudioContent(MessageModel message)
        {
            var attachment = message.Attachments?.FirstOrDefault();
            if (attachment == null || string.IsNullOrEmpty(attachment.FileId))
            {
                SetupTextContent(message);
                return;
            }

            if (!string.IsNullOrEmpty(message.Text))
            {
                _textContent = new TextMessageContent(message.Text);
                TextContentPresenter.Content = _textContent;
            }
            else
            {
                TextContentPresenter.Content = null;
            }

            var audioContent = new AudioMessageContent(attachment);
            MediaContentPresenter.Content = audioContent;
            SetMediaContentMargin(true);

            this.MinWidth = 320;
        }

        private void SetupVoiceContent(MessageModel message)
        {
            if (!string.IsNullOrEmpty(message.Text))
            {
                _textContent = new TextMessageContent(message.Text);
                TextContentPresenter.Content = _textContent;
            }
            else
            {
                TextContentPresenter.Content = null;
            }

            var voicePlaceholder = new TextBlock
            {
                Text = "\U0001F3A4 Голосовое сообщение (скоро)",
                FontFamily = (System.Windows.Media.FontFamily)FindResource("AdwaitaSans"),
                FontSize = 13,
                Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromArgb(0xAA, 0xFF, 0xFF, 0xFF)),
                Margin = new Thickness(4, 8, 4, 4),
            };

            MediaContentPresenter.Content = voicePlaceholder;
            SetMediaContentMargin(true);

            this.MinWidth = 250;
        }

        private void SetupStickerContent(MessageModel message)
        {
            var attachment = message.Attachments?.FirstOrDefault();
            if (attachment == null || string.IsNullOrEmpty(attachment.FileId))
            {
                SetupTextContent(message);
                return;
            }

            TextContentPresenter.Content = null;

            var stickerContent = new StickerMessageContent(attachment);
            MediaContentPresenter.Content = stickerContent;
            SetMediaContentMargin(false);

            this.MinWidth = 300;

            // Make the timestamp label more subtle to blend with the transparent sticker background
            MessageTime.Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF));
        }

        private bool IsVoiceType(AttachmentsModel attachment)
        {
            return attachment.Type == Proto.Shared.MessageAttachmentType.Voice;
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
        /// Marks a specific attachment as uploaded
        /// </summary>
        public void MarkAttachmentUploaded(int index, string fileId)
        {
            if (index < 0 || index >= _uploadingItems.Count)
                return;

            _uploadingItems[index].MarkAsUploaded(fileId);
            // Note: Message sending happens in MessengerPage after all files are uploaded
        }

        /// <summary>
        /// Marks a specific attachment as failed
        /// </summary>
        public void MarkAttachmentFailed(int index, string errorMessage)
        {
            if (index < 0 || index >= _uploadingItems.Count)
                return;

            _uploadingItems[index].MarkAsFailed(errorMessage);
        }

        /// <summary>
        /// Replaces the uploading panel with the actual content after successful upload
        /// </summary>
        public void ReplaceUploadingWithContent(MessageModel message, MessageType messageType)
        {
            _uploadingItems.Clear();
            SetupContent(message, messageType);
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
                SingleCheckmark.Opacity = STATUS_READ_OPACITY;
                DoubleCheckmark.Opacity = STATUS_READ_OPACITY;
            }
            else if (!string.IsNullOrEmpty(MessageId))
            {
                // Single checkmark - message sent but not read
                SingleCheckmark.Visibility = Visibility.Visible;
                DoubleCheckmark.Visibility = Visibility.Collapsed;
                SingleCheckmark.Opacity = STATUS_SENT_OPACITY;
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

        /// <summary>
        /// Sets up uploading attachment items for pending uploads
        /// </summary>
        public void SetupUploadingAttachments(List<string> localFilePaths)
        {
            if (localFilePaths == null || localFilePaths.Count == 0)
                return;

            _uploadingItems.Clear();
            var uploadingPanel = new StackPanel();

            foreach (var filePath in localFilePaths)
            {
                var item = new UploadingAttachmentItem(filePath);
                _uploadingItems.Add(item);
                uploadingPanel.Children.Add(item);
            }

            MediaContentPresenter.Content = uploadingPanel;
            SetMediaContentMargin(true);
        }

        /// <summary>
        /// Updates the upload progress for a specific attachment by index
        /// </summary>
        public void UpdateAttachmentProgress(int index, double progress)
        {
            if (index < 0 || index >= _uploadingItems.Count)
                return;

            _uploadingItems[index].UpdateProgress(progress);
        }

        private void ThemedConfirm(MessageOwner owner)
        {
            if (_messageType == MessageType.Sticker)
            {
                // Stickers have no bubble background
                MessageBorder.Style = (Style)FindResource("StickerMessageStyle");
                // Remove drop shadow for stickers
                MessageBorder.Effect = null;
                this.HorizontalAlignment = owner == MessageOwner.Me ? HorizontalAlignment.Right : HorizontalAlignment.Left;
                MainGrid.HorizontalAlignment = owner == MessageOwner.Me ? HorizontalAlignment.Right : HorizontalAlignment.Left;
            }
            else if (owner == MessageOwner.Me)
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

            int calculatedWidth = Math.Min(longestLineLength, MAX_CHARS_PER_LINE) * CHAR_WIDTH_APPROX;

            // Применить минимальную ширину 150px для коротких сообщений
            return Math.Max(calculatedWidth, MIN_TEXT_WIDTH);
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
            Audio,
            Voice,
            Sticker,
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
