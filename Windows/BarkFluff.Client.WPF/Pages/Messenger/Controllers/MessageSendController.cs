using BarkFluff.Client.WPF.Services.App.Caching;
using BarkFluff.Client.WPF.UserControls;
using BarkFluff.WebApi.Core.MessengerData.NonSavedData;

using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

using Erida = BarkFluff.Client.WPF.Services.Erida.MessageType;
using MessageAttachmentType = BarkFluff.Proto.Shared.MessageAttachmentType;
using MType = BarkFluff.Client.WPF.Services.Erida.MessageType.MessageTypeEnum;

namespace BarkFluff.Client.WPF.Pages.Messenger.Controllers
{
    /// <summary>
    /// Отвечает за отправку текстовых сообщений, стикеров,
    /// а также за ввод текста и клавиатурные шорткаты.
    /// </summary>
    public sealed class MessageSendController
    {
        public const int MESSAGE_LIMIT = 4096; // лимит символов в одном сообщении

        private readonly MessengerPage _page;
        private readonly TextBox _textForMessage;
        private readonly Popup _stickerPopup;
        private readonly StickerPicker _stickerPickerControl;
        private readonly ChatHistoryController _history;
        private readonly ChatListController _chatListCtrl;

        private string? _tempMessage;

        public MessageSendController(
            MessengerPage page,
            TextBox textForMessage,
            Popup stickerPopup,
            StickerPicker stickerPickerControl,
            ChatHistoryController history,
            ChatListController chatListCtrl)
        {
            _page = page;
            _textForMessage = textForMessage;
            _stickerPopup = stickerPopup;
            _stickerPickerControl = stickerPickerControl;
            _history = history;
            _chatListCtrl = chatListCtrl;
        }

        /// <summary>
        /// Отправка текстового сообщения. Разбивает длинный текст на части по <see cref="MESSAGE_LIMIT"/>.
        /// </summary>
        public void SendTextMessage()
        {
            _tempMessage = _textForMessage.Text;

            if (string.IsNullOrEmpty(_tempMessage)) return;

            List<string> messageParts = SplitMessage(_tempMessage, MESSAGE_LIMIT);
            _textForMessage.Text = string.Empty;
            foreach (var part in messageParts)
            {
                string resipientId;
                bool isUserId;
                if (_page.IsOpenChatEmpty)
                {
                    resipientId = _page.ChatIdbyUserId.Value.ToString();
                    isUserId = true;
                }
                else
                {
                    resipientId = _page.ChatId.Value;
                    isUserId = false;
                }

                (bool, bool, string) options = new(true, isUserId, resipientId);
                var messageControl = new MessageBubble(part, options, new List<string>());
                var message = new MessageModel
                {
                    Text = part,
                    ChatId = _page.ChatId.Value,
                    SenderId = App.GParam.UserId,
                    SentAt = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UtcNow)
                };

                // Проверяем и добавляем разделитель даты перед добавлением сообщения
                _history.AddDateSeparatorIfNeeded(DateTime.Now);

                _history.AddMessage(messageControl);
                _tempMessage = string.Empty;
                _chatListCtrl.UpdateChatWithMessage(message);
            }
        }

        public void OnTextBoxKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.LeftShift || e.Key == Key.LeftCtrl)
            {
                var textBox = sender as TextBox;
                if (textBox != null) textBox.AcceptsReturn = true;
            }
            if (e.Key == Key.Enter)
            {
                var textBox = sender as TextBox;
                if (textBox == null) return;
                _tempMessage = textBox.Text;
                var a = _tempMessage.Replace(" ", "");
                if (a == "")
                {
                    return;
                }
                SendTextMessage();
                textBox.Text = string.Empty;
                textBox.AcceptsReturn = false;
            }
        }

        public void OnTextBoxKeyUp(object sender, KeyEventArgs e)
        {
            var textBox = sender as TextBox;
            if (textBox == null) return;
            if (e.Key == Key.LeftShift || e.Key == Key.LeftCtrl)
            {
                textBox.AcceptsReturn = false;
            }
        }

        /// <summary>
        /// Переключает видимость popup'а выбора стикеров и лениво подписывает обработчик выбора.
        /// </summary>
        public void ToggleStickerPopup()
        {
            if (!_stickerPickerControl.IsEventSubscribed)
            {
                _stickerPickerControl.StickerSelected += OnStickerSelected;
                _stickerPickerControl.MarkEventSubscribed();
            }

            _stickerPopup.IsOpen = !_stickerPopup.IsOpen;

            if (_stickerPopup.IsOpen)
            {
                _ = _stickerPickerControl.LoadAsync(App.GParam);
            }
        }

        private async void OnStickerSelected(object? sender, StickerSelectedEventArgs e)
        {
            _stickerPopup.IsOpen = false;
            await SendStickerAsync(e);
        }

        public async Task SendStickerAsync(StickerSelectedEventArgs sticker)
        {
            if (string.IsNullOrEmpty(_page.ChatId.Value) || string.IsNullOrEmpty(sticker.FileId))
                return;

            try
            {
                string recipientId;
                bool isUserId;
                if (_page.IsOpenChatEmpty)
                {
                    recipientId = _page.ChatIdbyUserId.Value.ToString();
                    isUserId = true;
                }
                else
                {
                    recipientId = _page.ChatId.Value;
                    isUserId = false;
                }

                // Build a pending message for immediate UI display
                var pendingMessage = new MessageModel
                {
                    ChatId = _page.ChatId.Value,
                    SenderId = App.GParam.UserId,
                    SentAt = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UtcNow),
                    Attachments = new List<AttachmentsModel>
                    {
                        new AttachmentsModel
                        {
                            Type = MessageAttachmentType.Sticker,
                            FileId = sticker.FileId,
                            PreviewUrl = !string.IsNullOrEmpty(sticker.PreviewUrl)
                                ? sticker.PreviewUrl
                                : sticker.FileUrl
                        }
                    }
                };

                _history.AddDateSeparatorIfNeeded(DateTime.Now);
                var messageControl = new MessageBubble(
                    MessageBubble.MessageOwner.Me,
                    MessageBubble.MessageType.Sticker,
                    pendingMessage,
                    _page.IsGroup);
                _history.AddMessage(messageControl);

                var letter = new ForwardingLetter { FilesId = new List<string> { sticker.FileId } };
                (bool, string) type = new(isUserId, recipientId);
                var response = await App.ServerCommunication.SendMessage(App.GParam, type, letter);

                if (!response.error.IsSuccess)
                {
                    App.ErideMessage.AddMessage(
                        $"Ошибка отправки стикера: {response.error.ErrorMessage}",
                        new Erida { Type = MType.Error });
                }
                else if (response.message != null)
                {
                    messageControl.MessageId = response.message.MessageId.ToString();
                    messageControl.MarkAsSent();

                    App.CacheManager.SaveMessage(
                        response.message.ChatId,
                        _page.TitleChat,
                        response.message,
                        MessageOperation.Added);

                    _chatListCtrl.UpdateChatWithMessage(response.message);
                }
            }
            catch (Exception ex)
            {
                App.ErideMessage.AddMessage($"Ошибка отправки стикера: {ex.Message}", new Erida { Type = MType.Error });
            }
        }

        private List<string> SplitMessage(string message, int maxLength)
        {
            List<string> parts = new List<string>();
            int currentIndex = 0;

            while (currentIndex < message.Length)
            {
                int length = Math.Min(maxLength, message.Length - currentIndex);
                if (length == 0) break;

                // Проверяем, чтобы не разрывать слово
                if (currentIndex + length < message.Length)
                {
                    int lastSpace = message.LastIndexOf(' ', currentIndex + length - 1, length);
                    if (lastSpace > currentIndex)
                    {
                        length = lastSpace - currentIndex;
                    }
                }

                string part = message.Substring(currentIndex, length);
                parts.Add(part.TrimEnd());
                currentIndex += length;
            }

            return parts;
        }
    }
}
