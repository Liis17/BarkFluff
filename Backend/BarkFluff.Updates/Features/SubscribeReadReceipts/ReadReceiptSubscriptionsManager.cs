namespace BarkFluff.Updates.Features.SubscribeReadReceipts;

using System.Collections.Concurrent;
using BarkFluff.Proto.Updates;
using Grpc.Core;

public class ReadReceiptSubscriptionsManager
{
    // Subscriptions for all chats (last message only) - used for chat list
    private readonly ConcurrentDictionary<long, HashSet<IServerStreamWriter<ReadReceiptUpdate>>> _globalSubscriptions = new();
    
    // Subscriptions for specific chat - used for open chat
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<long, HashSet<IServerStreamWriter<ReadReceiptUpdate>>>> _chatSubscriptions = new();
    
    private readonly object _lock = new();

    /// <summary>
    /// Register a global subscription (for chat list - last message only)
    /// </summary>
    public void RegisterGlobalSubscription(long userId, IServerStreamWriter<ReadReceiptUpdate> responseStream)
    {
        lock (_lock)
        {
            if (!_globalSubscriptions.TryGetValue(userId, out var streams))
            {
                streams = new HashSet<IServerStreamWriter<ReadReceiptUpdate>>();
                _globalSubscriptions[userId] = streams;
            }
            streams.Add(responseStream);
        }
    }

    /// <summary>
    /// Register a chat-specific subscription (for open chat - all unread messages)
    /// </summary>
    public void RegisterChatSubscription(long userId, string chatId, IServerStreamWriter<ReadReceiptUpdate> responseStream)
    {
        lock (_lock)
        {
            if (!_chatSubscriptions.TryGetValue(chatId, out var userStreams))
            {
                userStreams = new ConcurrentDictionary<long, HashSet<IServerStreamWriter<ReadReceiptUpdate>>>();
                _chatSubscriptions[chatId] = userStreams;
            }

            if (!userStreams.TryGetValue(userId, out var streams))
            {
                streams = new HashSet<IServerStreamWriter<ReadReceiptUpdate>>();
                userStreams[userId] = streams;
            }
            streams.Add(responseStream);
        }
    }

    /// <summary>
    /// Remove global subscription
    /// </summary>
    public void RemoveGlobalSubscription(long userId, IServerStreamWriter<ReadReceiptUpdate> responseStream)
    {
        lock (_lock)
        {
            if (_globalSubscriptions.TryGetValue(userId, out var streams))
            {
                streams.Remove(responseStream);
                
                if (streams.Count == 0)
                {
                    _globalSubscriptions.TryRemove(userId, out _);
                }
            }
        }
    }

    /// <summary>
    /// Remove chat-specific subscription
    /// </summary>
    public void RemoveChatSubscription(long userId, string chatId, IServerStreamWriter<ReadReceiptUpdate> responseStream)
    {
        lock (_lock)
        {
            if (_chatSubscriptions.TryGetValue(chatId, out var userStreams))
            {
                if (userStreams.TryGetValue(userId, out var streams))
                {
                    streams.Remove(responseStream);
                    
                    if (streams.Count == 0)
                    {
                        userStreams.TryRemove(userId, out _);
                    }
                }
                
                if (userStreams.IsEmpty)
                {
                    _chatSubscriptions.TryRemove(chatId, out _);
                }
            }
        }
    }

    /// <summary>
    /// Get all global subscription streams for a user
    /// </summary>
    public IEnumerable<IServerStreamWriter<ReadReceiptUpdate>> GetGlobalStreams(long userId)
    {
        lock (_lock)
        {
            if (_globalSubscriptions.TryGetValue(userId, out var streams))
            {
                return streams.ToList();
            }
            return Enumerable.Empty<IServerStreamWriter<ReadReceiptUpdate>>();
        }
    }

    /// <summary>
    /// Get all chat-specific subscription streams for a user and chat
    /// </summary>
    public IEnumerable<IServerStreamWriter<ReadReceiptUpdate>> GetChatStreams(long userId, string chatId)
    {
        lock (_lock)
        {
            if (_chatSubscriptions.TryGetValue(chatId, out var userStreams) &&
                userStreams.TryGetValue(userId, out var streams))
            {
                return streams.ToList();
            }
            return Enumerable.Empty<IServerStreamWriter<ReadReceiptUpdate>>();
        }
    }
}
