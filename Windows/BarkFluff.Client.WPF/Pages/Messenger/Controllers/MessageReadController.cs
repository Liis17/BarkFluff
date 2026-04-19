using BarkFluff.Client.WPF.UserControls;

using System.Windows.Controls;
using System.Windows.Threading;

using Erida = BarkFluff.Client.WPF.Services.Erida.MessageType;
using MType = BarkFluff.Client.WPF.Services.Erida.MessageType.MessageTypeEnum;

namespace BarkFluff.Client.WPF.Pages.Messenger.Controllers
{
    /// <summary>
    /// Отвечает за отметку входящих сообщений как прочитанные с дебаунсом.
    /// </summary>
    public sealed class MessageReadController
    {
        public const int MARK_AS_READ_DEBOUNCE_MS = 1000;
        public const int INITIAL_MARK_DELAY_MS = 500;

        private readonly MessengerPage _page;
        private readonly StackPanel _messageArea;
        private readonly StackPanel _chatList;

        private DispatcherTimer? _markAsReadDebounceTimer;
        private readonly HashSet<long> _pendingMarkAsRead = new HashSet<long>();

        public MessageReadController(MessengerPage page, StackPanel messageArea, StackPanel chatList)
        {
            _page = page;
            _messageArea = messageArea;
            _chatList = chatList;
        }

        /// <summary>
        /// Отмечает видимые входящие сообщения как прочитанные с дебаунсом
        /// </summary>
        public void MarkVisibleMessagesAsRead()
        {
            if (string.IsNullOrEmpty(_page.ChatId.Value)) return;

            var messagesToMark = new List<long>();

            foreach (var child in _messageArea.Children)
            {
                if (child is MessageBubble bubble)
                {
                    // Отмечаем только входящие сообщения (не от текущего пользователя)
                    if (bubble.SenderId != App.GParam.UserId &&
                        !bubble.ReadBy.Contains(App.GParam.UserId) &&
                        long.TryParse(bubble.MessageId, out long messageId))
                    {
                        messagesToMark.Add(messageId);
                    }
                }
            }

            if (messagesToMark.Count > 0)
            {
                foreach (var id in messagesToMark)
                {
                    _pendingMarkAsRead.Add(id);
                }

                // Дебаунс вызова API
                if (_markAsReadDebounceTimer == null)
                {
                    _markAsReadDebounceTimer = new DispatcherTimer();
                    _markAsReadDebounceTimer.Interval = TimeSpan.FromMilliseconds(MARK_AS_READ_DEBOUNCE_MS);
                    _markAsReadDebounceTimer.Tick += async (s, args) =>
                    {
                        _markAsReadDebounceTimer.Stop();

                        if (_pendingMarkAsRead.Count > 0)
                        {
                            var idsToMark = _pendingMarkAsRead.ToList();
                            _pendingMarkAsRead.Clear();

                            // Вызвать API для отметки как прочитано
                            await ReadMessage(idsToMark);

                            // Обновить локальный UI
                            foreach (var child in _messageArea.Children)
                            {
                                if (child is MessageBubble bubble && long.TryParse(bubble.MessageId, out long mid) && idsToMark.Contains(mid))
                                {
                                    var newReadBy = bubble.ReadBy.ToList();
                                    if (!newReadBy.Contains(App.GParam.UserId))
                                    {
                                        newReadBy.Add(App.GParam.UserId);
                                        bubble.UpdateReadByList(newReadBy);
                                    }
                                }
                            }

                            // Сбросить счётчик непрочитанных для текущего чата
                            foreach (var child in _chatList.Children)
                            {
                                if (child is ChatItem chatItem && chatItem.ChatId == _page.ChatId.Value)
                                {
                                    chatItem.ResetUnreadCount();
                                    chatItem.ResetFirstUnreadId();
                                    break;
                                }
                            }
                        }
                    };
                }

                _markAsReadDebounceTimer.Stop();
                _markAsReadDebounceTimer.Start();
            }
        }

        private async Task ReadMessage(List<long> messageIds)
        {
            var response = await App.ServerCommunication.MarkMessageAsRead(App.GParam, messageIds);
            if (!response.IsSuccess)
            {
                App.ErideMessage.AddMessage("Ошибка при отметке сообщения как прочитанного: " + response.ErrorMessage, new Erida { Type = MType.Debug });
                return;
            }
        }

        /// <summary>
        /// Останавливает debounce-таймер чтобы предотвратить Tick после Unloaded.
        /// </summary>
        public void Dispose()
        {
            _markAsReadDebounceTimer?.Stop();
            _markAsReadDebounceTimer = null;
            _pendingMarkAsRead.Clear();
        }
    }
}
