using BarkFluff.Client.WPF.Services.App.Caching;
using BarkFluff.Client.WPF.UserControls;
using BarkFluff.WebApi.Core.MessengerData;
using BarkFluff.WebApi.Core.MessengerData.NonSavedData;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Threading;

using Erida = BarkFluff.Client.WPF.Services.Erida.MessageType;
using MType = BarkFluff.Client.WPF.Services.Erida.MessageType.MessageTypeEnum;

namespace BarkFluff.Client.WPF.Pages.Messenger.Controllers
{
    /// <summary>
    /// Отвечает за загрузку истории, отображение сообщений, разделителей дат и скролл.
    /// Владеет <see cref="MessageArea"/> и координирует с <see cref="MessageReadController"/>.
    /// </summary>
    public sealed class ChatHistoryController
    {
        public const int HISTORY_PAGE_SIZE = 30;
        public const int SCROLL_TOP_THRESHOLD = 100;

        private readonly MessengerPage _page;
        private readonly ScrollViewer _messageScrollViewer;
        private readonly StackPanel _messageArea;
        private readonly MessageReadController _read;

        public bool IsLoadingHistory { get; private set; }
        public bool HasMoreHistory { get; private set; } = true;
        public long OldestLoadedMessageId { get; private set; }
        public long FirstUnreadMessageId { get; set; }
        public long OpenedLastMessageId { get; set; }

        public ChatHistoryController(MessengerPage page, ScrollViewer messageScrollViewer, StackPanel messageArea, MessageReadController readCtrl)
        {
            _page = page;
            _messageScrollViewer = messageScrollViewer;
            _messageArea = messageArea;
            _read = readCtrl;

            _messageScrollViewer.ScrollChanged += OnScrollChanged;
        }

        private async void OnScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            var scrollViewer = sender as ScrollViewer;
            if (scrollViewer == null) return;

            // Проверяем, прокручено ли до верха (с порогом)
            if (scrollViewer.VerticalOffset < SCROLL_TOP_THRESHOLD && !IsLoadingHistory && HasMoreHistory && !string.IsNullOrEmpty(_page.ChatId.Value))
            {
                await LoadHistoryMessagesAsync();
            }
        }

        /// <summary>
        /// Загружает более старые сообщения при прокрутке к верху
        /// </summary>
        public async Task LoadHistoryMessagesAsync()
        {
            if (IsLoadingHistory || !HasMoreHistory || OldestLoadedMessageId == 0) return;

            IsLoadingHistory = true;

            try
            {
                App.ErideMessage.AddMessage($"Загрузка истории сообщений (from ID: {OldestLoadedMessageId})", new Erida { Type = MType.Debug });

                // Сначала пробуем загрузить из кеша
                var cachedMessages = App.CacheManager.GetMessages(_page.ChatId.Value, OldestLoadedMessageId, HISTORY_PAGE_SIZE);
                var hasLoadedFromCache = false;

                if (cachedMessages != null && cachedMessages.Count > 0)
                {
                    App.ErideMessage.AddMessage($"Загружено {cachedMessages.Count} сообщений из кеша", new Erida { Type = MType.Debug });
                    await InsertHistoryMessagesAsync(cachedMessages);
                    hasLoadedFromCache = true;
                }

                // Затем загружаем с сервера
                var response = await App.ServerCommunication.GetMessagesWithOffset(
                    App.GParam,
                    _page.ChatId.Value,
                    OldestLoadedMessageId,
                    HISTORY_PAGE_SIZE,
                    0);

                if (response.error.IsSuccess && response.messages != null && response.messages.Count > 0)
                {
                    App.ErideMessage.AddMessage($"Загружено {response.messages.Count} сообщений с сервера", new Erida { Type = MType.Debug });

                    // Сохраняем в кеш
                    foreach (var msg in response.messages)
                    {
                        App.CacheManager.SaveMessage(_page.ChatId.Value, _page.TitleChat, msg, MessageOperation.Added);
                    }

                    // Вставляем в UI (будет удаление дубликатов)
                    await InsertHistoryMessagesAsync(response.messages);

                    // Если получено меньше сообщений, чем запрашивали - значит достигнут конец
                    if (response.messages.Count < HISTORY_PAGE_SIZE)
                    {
                        HasMoreHistory = false;
                        App.ErideMessage.AddMessage("Достигнут конец истории сообщений", new Erida { Type = MType.Debug });
                    }
                }
                else if (response.error.ErrorCode == 1)
                {
                    // Нет больше сообщений
                    HasMoreHistory = false;
                    App.ErideMessage.AddMessage("Нет больше сообщений для загрузки", new Erida { Type = MType.Debug });
                }
                else if (!hasLoadedFromCache)
                {
                    App.ErideMessage.AddMessage($"Ошибка загрузки истории: {response.error.ErrorMessage}", new Erida { Type = MType.Error });
                }
            }
            catch (Exception ex)
            {
                App.ErideMessage.AddMessage($"Исключение при загрузке истории: {ex.Message}", new Erida { Type = MType.Error });
            }
            finally
            {
                IsLoadingHistory = false;
            }
        }

        /// <summary>
        /// Вставляет сообщения истории в начало области сообщений с удалением дубликатов
        /// </summary>
        private async Task InsertHistoryMessagesAsync(List<MessageModel> messages)
        {
            if (messages == null || messages.Count == 0) return;

            await _page.Dispatcher.InvokeAsync(() =>
            {
                // Запоминаем текущую позицию прокрутки
                var scrollViewer = _messageScrollViewer;
                var oldScrollHeight = scrollViewer.ScrollableHeight;
                var oldOffset = scrollViewer.VerticalOffset;

                // Получаем существующие ID сообщений для удаления дубликатов
                var existingMessageIds = new HashSet<long>();
                foreach (var child in _messageArea.Children)
                {
                    if (child is MessageBubble bubble && long.TryParse(bubble.MessageId, out long id))
                    {
                        existingMessageIds.Add(id);
                    }
                }

                // Сортируем сообщения по времени (сначала старые для корректной вставки)
                var sortedMessages = messages.OrderBy(m => m.SentAt.ToDateTime()).ToList();
                var insertedCount = 0;

                foreach (var message in sortedMessages)
                {
                    // Пропускаем дубликаты
                    if (existingMessageIds.Contains(message.MessageId))
                    {
                        continue;
                    }

                    // Обновляем ID самого старого загруженного сообщения
                    if (message.MessageId < OldestLoadedMessageId || OldestLoadedMessageId == 0)
                    {
                        OldestLoadedMessageId = message.MessageId;
                    }

                    var owner = message.SenderId == App.GParam.UserId ? MessageBubble.MessageOwner.Me : MessageBubble.MessageOwner.Interlocutor;
                    var type = MessageTypeMapper.GetMessageType(message);
                    var messageItem = new MessageBubble(owner, type, message, _page.IsGroup);

                    // Вставляем в начало (сначала самые старые сообщения)
                    _messageArea.Children.Insert(insertedCount, messageItem);
                    insertedCount++;
                }

                if (insertedCount > 0)
                {
                    // Восстанавливаем позицию прокрутки (с учётом нового содержимого)
                    scrollViewer.UpdateLayout();
                    var newScrollHeight = scrollViewer.ScrollableHeight;
                    var scrollDifference = newScrollHeight - oldScrollHeight;
                    scrollViewer.ScrollToVerticalOffset(oldOffset + scrollDifference);

                    App.ErideMessage.AddMessage($"Добавлено {insertedCount} сообщений в историю", new Erida { Type = MType.Debug });
                }
            });
        }

        /// <summary>
        /// Отображает список сообщений в области сообщений, группируя по дням
        /// </summary>
        public void DisplayMessages(List<MessageModel> messages, bool scrollToFirstUnread = false)
        {
            if (messages == null || messages.Count == 0)
            {
                return;
            }

            _messageArea.Children.Clear();
            var sortedMessages = messages.OrderBy(m => m.SentAt.ToDateTime()).ToList();

            // Track the oldest message ID for pagination
            if (sortedMessages.Count > 0)
            {
                OldestLoadedMessageId = sortedMessages.First().MessageId;
                HasMoreHistory = true; // Reset history flag when opening a new chat
            }

            // Группировка по дням (используем ЛОКАЛЬНОЕ время для правильного отображения)
            var groupedMessages = sortedMessages.GroupBy(m => m.SentAt.ToDateTime().ToLocalTime().Date)
                                              .OrderBy(g => g.Key);

            bool foundFirstUnread = false;

            foreach (var group in groupedMessages)
            {
                // Добавляем контрол с датой по центру
                var dateHeader = GetDateHeader(group.Key);
                var dateControl = new DateHeaderControl { Text = dateHeader };
                dateControl.HorizontalAlignment = HorizontalAlignment.Center;
                dateControl.Margin = new Thickness(0, 10, 0, 10);
                AddMessageWithoutScroll(dateControl);

                // Добавляем сообщения группы
                foreach (var item in group)
                {
                    var owner = item.SenderId == App.GParam.UserId ? MessageBubble.MessageOwner.Me : MessageBubble.MessageOwner.Interlocutor;
                    var type = MessageTypeMapper.GetMessageType(item);
                    var messageItem = new MessageBubble(owner, type, item, _page.IsGroup);

                    // Если это первое непрочитанное - добавляем разделитель ПЕРЕД ним
                    if (scrollToFirstUnread && !foundFirstUnread && item.MessageId == FirstUnreadMessageId)
                    {
                        foundFirstUnread = true;
                        AddUnreadSeparator();
                    }

                    // Добавляем сообщение БЕЗ автоматического скролла
                    AddMessageWithoutScroll(messageItem);
                }
            }

            // Выполняем скролл после добавления всех сообщений и завершения layout
            if (scrollToFirstUnread && FirstUnreadMessageId != 0)
            {
                _page.Dispatcher.BeginInvoke(new Action(() =>
                {
                    _messageArea.UpdateLayout();
                    ScrollToMessage(FirstUnreadMessageId);
                    // После скролла отмечаем видимые сообщения как прочитанные
                    _read.MarkVisibleMessagesAsRead();
                }), DispatcherPriority.ContextIdle);
            }
            else
            {
                _page.Dispatcher.BeginInvoke(new Action(() =>
                {
                    _messageArea.UpdateLayout();
                    _messageScrollViewer.ScrollToEnd();
                    // При обычной загрузке тоже отмечаем сообщения
                    _read.MarkVisibleMessagesAsRead();
                }), DispatcherPriority.ContextIdle);
            }
        }

        /// <summary>
        /// Добавляет сообщение в MessageArea, скроллит вниз и запускает отложенную отметку как прочитанное
        /// </summary>
        public void AddMessage(UserControl control)
        {
            _messageArea.Children.Add(control);
            var animation = (Storyboard)_page.FindResource("MessageAppearAnimation");
            Storyboard.SetTarget(animation, control);
            animation.Begin();
            _messageScrollViewer.ScrollToEnd();

            // Отметить видимые сообщения как прочтенные с небольшой задержкой
            var delayTimer = new DispatcherTimer();
            delayTimer.Interval = TimeSpan.FromMilliseconds(MessageReadController.INITIAL_MARK_DELAY_MS);
            delayTimer.Tick += (s, args) =>
            {
                delayTimer.Stop();
                _read.MarkVisibleMessagesAsRead();
            };
            delayTimer.Start();
        }

        /// <summary>
        /// Добавляет сообщение в область сообщений без автоматического скролла.
        /// Используется при начальной загрузке сообщений для позиционирования скролла.
        /// </summary>
        public void AddMessageWithoutScroll(UserControl control)
        {
            _messageArea.Children.Add(control);
            var animation = (Storyboard)_page.FindResource("MessageAppearAnimation");
            Storyboard.SetTarget(animation, control);
            animation.Begin();
        }

        private string GetDateHeader(DateTime date)
        {
            if (date.Date == DateTime.Today) return "Сегодня";
            if (date.Date == DateTime.Today.AddDays(-1)) return "Вчера";
            return date.ToString("dd.MM.yyyy");
        }

        /// <summary>
        /// Добавляет разделитель непрочитанных сообщений
        /// </summary>
        private void AddUnreadSeparator()
        {
            var separator = new UnreadSeparatorControl
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 10, 0, 10)
            };
            AddMessageWithoutScroll(separator);
        }

        /// <summary>
        /// Добавляет разделитель даты перед новым сообщением, если дата отличается
        /// </summary>
        public void AddDateSeparatorIfNeeded(DateTime newMessageLocalDate)
        {
            // Используем только локальную дату для сравнения
            var newDate = newMessageLocalDate.Date;

            // Check if we need to add a date separator
            if (_messageArea.Children.Count == 0)
            {
                // Если это первое сообщение, добавляем заголовок даты
                var dateHeader = GetDateHeader(newDate);
                var dateControl = new DateHeaderControl { Text = dateHeader };
                dateControl.HorizontalAlignment = HorizontalAlignment.Center;
                dateControl.Margin = new Thickness(0, 10, 0, 10);
                _messageArea.Children.Add(dateControl);
                return;
            }

            // Проверяем, есть ли уже разделитель с такой же датой
            foreach (var child in _messageArea.Children)
            {
                if (child is DateHeaderControl existingHeader)
                {
                    var headerText = existingHeader.Text;
                    var expectedText = GetDateHeader(newDate);

                    // Если уже есть разделитель с нужной датой - не добавляем новый
                    if (headerText == expectedText)
                    {
                        return;
                    }
                }
            }

            // Get the last message in the area (skip if last item is already a date header)
            var lastChild = _messageArea.Children[_messageArea.Children.Count - 1];

            if (lastChild is DateHeaderControl)
            {
                // If last item is already a date header, don't add another
                return;
            }

            if (lastChild is MessageBubble lastBubble && lastBubble.SentAt != null)
            {
                // Конвертируем UTC время сообщения в локальное для корректного сравнения
                var lastMessageLocalDate = lastBubble.SentAt.ToDateTime().ToLocalTime().Date;

                // Add separator if dates differ
                if (lastMessageLocalDate != newDate)
                {
                    var dateHeader = GetDateHeader(newDate);
                    var dateControl = new DateHeaderControl { Text = dateHeader };
                    dateControl.HorizontalAlignment = HorizontalAlignment.Center;
                    dateControl.Margin = new Thickness(0, 10, 0, 10);
                    _messageArea.Children.Add(dateControl);
                }
            }
        }

        /// <summary>
        /// Прокручивает чат к сообщению с указанным ID, центрируя его на экране
        /// </summary>
        public void ScrollToMessage(long messageId)
        {
            MessageBubble targetBubble = null;
            foreach (var child in _messageArea.Children)
            {
                if (child is MessageBubble bubble && long.TryParse(bubble.MessageId, out long id) && id == messageId)
                {
                    targetBubble = bubble;
                    break;
                }
            }

            if (targetBubble != null)
            {
                _messageArea.UpdateLayout();
                _messageScrollViewer.UpdateLayout();

                var position = targetBubble.TransformToVisual(_messageArea)
                                           .Transform(new Point(0, 0));
                var targetOffset = position.Y - (_messageScrollViewer.ActualHeight / 2) + (targetBubble.ActualHeight / 2);
                var clampedOffset = Math.Max(0, targetOffset);

                var animation = new DoubleAnimation
                {
                    From = _messageScrollViewer.VerticalOffset,
                    To = clampedOffset,
                    Duration = TimeSpan.FromMilliseconds(300),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };

                var story = new Storyboard();
                story.Children.Add(animation);
                Storyboard.SetTarget(animation, _messageScrollViewer);
                Storyboard.SetTargetProperty(animation, new PropertyPath(ScrollAnimationBehavior.VerticalOffsetProperty));

                story.Completed += (s, e) =>
                {
                    _messageScrollViewer.ScrollToVerticalOffset(clampedOffset);
                };

                story.Begin();
            }
        }

        /// <summary>
        /// Сбрасывает состояние контроллера для открытия нового чата.
        /// </summary>
        public void ResetForNewChat(long openedLastMessageId, long firstUnreadId)
        {
            OpenedLastMessageId = openedLastMessageId;
            FirstUnreadMessageId = firstUnreadId;
            OldestLoadedMessageId = 0;
            HasMoreHistory = true;
            _messageArea.Children.Clear();
        }

        /// <summary>
        /// Полная очистка состояния и UI (при закрытии чата).
        /// </summary>
        public void Clear()
        {
            OpenedLastMessageId = 0;
            FirstUnreadMessageId = 0;
            OldestLoadedMessageId = 0;
            HasMoreHistory = true;
            IsLoadingHistory = false;
            _messageArea.Children.Clear();
        }
    }
}
