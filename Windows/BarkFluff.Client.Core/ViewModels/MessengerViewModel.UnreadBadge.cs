using System.Collections.Specialized;
using System.ComponentModel;
using BarkFluff.Proto.Shared;

namespace BarkFluff.Client.Core.ViewModels;

public sealed partial class MessengerViewModel
{
    internal static readonly TimeSpan UnreadBadgeUpdateInterval = TimeSpan.FromMilliseconds(200);
    internal TimeProvider UnreadBadgeClock { get; set; } = TimeProvider.System;
    private long _unreadBadgeCount;

    public long UnreadBadgeCount
    {
        get => _unreadBadgeCount;
        private set => SetProperty(ref _unreadBadgeCount, value);
    }

    private void ConfirmUnreadAfterReadReceipt(RealtimeSession session, string chatId, bool isPrivate, long? messageId = null)
    {
        // ReadBy содержит всех читателей. Сообщения до первого непрочитанного уже учтены.
        if (Chats.Any(chat => chat.Id == chatId && chat.IsPrivate == isPrivate && chat.UnreadCount > 0 &&
            (messageId is null || chat.FirstUnreadMessageId > 0 && messageId >= chat.FirstUnreadMessageId)))
        {
            session.Journal.TouchUnread(chatId, isPrivate);
            session.ChatRefresh.RequestConfirmation();
        }
    }

    private Action ObserveUnreadBadge(RealtimeSession session)
    {
        var watched = new HashSet<ChatItemViewModel>();
        var scheduled = false;
        var timer = UnreadBadgeClock.CreateTimer(_ => PostSession(session, _ =>
        {
            scheduled = false;
            UnreadBadgeCount = Chats
                .Where(chat => !chat.IsMuted && chat.Definition.ChatType != ChatType.Secret)
                .Sum(chat => Math.Max(0L, chat.UnreadCount));
        }), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

        void Schedule()
        {
            if (scheduled)
                return;

            scheduled = true;
            timer.Change(UnreadBadgeUpdateInterval, Timeout.InfiniteTimeSpan);
        }

        void ItemChanged(object? sender, PropertyChangedEventArgs args)
        {
            if (args.PropertyName is nameof(ChatItemViewModel.UnreadCount) or nameof(ChatItemViewModel.IsMuted))
                Schedule();
        }

        void Watch(ChatItemViewModel chat)
        {
            if (watched.Add(chat))
                chat.PropertyChanged += ItemChanged;
        }

        void UnwatchAll()
        {
            foreach (var chat in watched)
                chat.PropertyChanged -= ItemChanged;
            watched.Clear();
        }

        void ChatsChanged(object? sender, NotifyCollectionChangedEventArgs args)
        {
            if (args.Action == NotifyCollectionChangedAction.Move)
                return;

            if (args.Action == NotifyCollectionChangedAction.Reset)
            {
                UnwatchAll();
                foreach (var chat in Chats)
                    Watch(chat);
            }
            else
            {
                if (args.OldItems is not null)
                    foreach (ChatItemViewModel chat in args.OldItems)
                        if (watched.Remove(chat))
                            chat.PropertyChanged -= ItemChanged;
                if (args.NewItems is not null)
                    foreach (ChatItemViewModel chat in args.NewItems)
                        Watch(chat);
            }
            Schedule();
        }

        foreach (var chat in Chats)
            Watch(chat);
        Chats.CollectionChanged += ChatsChanged;
        return () =>
        {
            Chats.CollectionChanged -= ChatsChanged;
            UnwatchAll();
            timer.Dispose();
        };
    }
}
