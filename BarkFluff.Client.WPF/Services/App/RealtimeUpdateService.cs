using BarkFluff.Client.WPF.Services.App.Caching;
using BarkFluff.Proto.Shared;
using BarkFluff.Proto.Updates;
using BarkFluff.WebApi.Core.MessengerData;
using BarkFluff.WebApi.Core.MessengerData.NonSavedData;

namespace BarkFluff.Client.WPF.Services.App
{
    /// <summary>
    /// Singleton service for managing real-time message updates via gRPC streaming
    /// </summary>
    public class RealtimeUpdateService : IDisposable
    {
        private static RealtimeUpdateService? _instance;
        private static readonly object _lock = new object();

        private CancellationTokenSource? _cancellationTokenSource;
        private Task? _streamingTask;
        private bool _isConnected;
        private bool _disposed;
        private int _reconnectAttempts;
        private const int MaxReconnectAttempts = 5;
        private const int ReconnectDelayMs = 5000;

        // Read receipt streaming - только одна глобальная подписка
        private CancellationTokenSource? _readReceiptCancellationTokenSource;
        private Task? _readReceiptStreamingTask;

        // Дедупликация сообщений
        private readonly HashSet<long> _processedMessageIds = new();
        private readonly object _processedMessagesLock = new();
        private const int MaxProcessedMessagesCacheSize = 1000;

        /// <summary>
        /// Event raised when a new message is received
        /// </summary>
        public event Action<string, MessageModel>? NewMessageReceived;

        /// <summary>
        /// Event raised when a read receipt update is received
        /// </summary>
        public event Action<MessageReadEvent>? ReadReceiptReceived;

        /// <summary>
        /// Event raised when connection status changes
        /// </summary>
        public event Action<bool>? ConnectionStatusChanged;

        /// <summary>
        /// Gets the singleton instance of the service
        /// </summary>
        public static RealtimeUpdateService Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        _instance ??= new RealtimeUpdateService();
                    }
                }
                return _instance;
            }
        }

        private RealtimeUpdateService()
        {
            _isConnected = false;
            _reconnectAttempts = 0;
        }

        /// <summary>
        /// Indicates whether the service is connected to the update stream
        /// </summary>
        public bool IsConnected => _isConnected;

        /// <summary>
        /// Starts the real-time updates streaming
        /// </summary>
        public void Start(GlobalParam globalParam)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(RealtimeUpdateService));

            if (_streamingTask != null && !_streamingTask.IsCompleted)
            {
                WPF.App.ErideMessage.AddMessage("Realtime update service is already running", new Erida.MessageType { Type = Erida.MessageType.MessageTypeEnum.Debug });
                return;
            }

            _cancellationTokenSource = new CancellationTokenSource();
            _streamingTask = Task.Run(() => StreamUpdatesAsync(globalParam, _cancellationTokenSource.Token));

            WPF.App.ErideMessage.AddMessage("Realtime update service started", new Erida.MessageType { Type = Erida.MessageType.MessageTypeEnum.Debug });
        }

        /// <summary>
        /// Stops the real-time updates streaming
        /// </summary>
        public void Stop()
        {
            if (_cancellationTokenSource != null)
            {
                _cancellationTokenSource.Cancel();
                _cancellationTokenSource.Dispose();
                _cancellationTokenSource = null;
            }

            _isConnected = false;
            ConnectionStatusChanged?.Invoke(false);
            WPF.App.ErideMessage.AddMessage("Realtime update service stopped", new Erida.MessageType { Type = Erida.MessageType.MessageTypeEnum.Debug });
        }

        private async Task StreamUpdatesAsync(GlobalParam globalParam, CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested && _reconnectAttempts < MaxReconnectAttempts)
            {
                try
                {
                    WPF.App.ErideMessage.AddMessage("Connecting to real-time updates stream...", new Erida.MessageType { Type = Erida.MessageType.MessageTypeEnum.Debug });

                    var (error, stream) = await WPF.App.ServerCommunication.JustUpdate(globalParam);

                    if (!error.IsSuccess || stream == null)
                    {
                        WPF.App.ErideMessage.AddMessage($"Failed to connect to updates stream: {error.ErrorMessage}", new Erida.MessageType { Type = Erida.MessageType.MessageTypeEnum.Error });
                        await HandleReconnectionAsync(cancellationToken);
                        continue;
                    }

                    _isConnected = true;
                    _reconnectAttempts = 0;
                    ConnectionStatusChanged?.Invoke(true);
                    WPF.App.ErideMessage.AddMessage("Connected to real-time updates stream", new Erida.MessageType { Type = Erida.MessageType.MessageTypeEnum.Debug });

                    await foreach (var messageEvent in stream.WithCancellation(cancellationToken))
                    {
                        ProcessMessageEvent(messageEvent);
                    }
                }
                catch (OperationCanceledException)
                {
                    // Normal cancellation
                    break;
                }
                catch (Exception ex)
                {
                    WPF.App.ErideMessage.AddMessage($"Error in updates stream: {ex.Message}", new Erida.MessageType { Type = Erida.MessageType.MessageTypeEnum.Error });
                    _isConnected = false;
                    ConnectionStatusChanged?.Invoke(false);

                    if (!cancellationToken.IsCancellationRequested)
                    {
                        await HandleReconnectionAsync(cancellationToken);
                    }
                }
            }

            _isConnected = false;
            ConnectionStatusChanged?.Invoke(false);

            if (_reconnectAttempts >= MaxReconnectAttempts)
            {
                WPF.App.ErideMessage.AddMessage("Max reconnection attempts reached. Stopping realtime updates.", new Erida.MessageType { Type = Erida.MessageType.MessageTypeEnum.Error });
            }
        }

        private async Task HandleReconnectionAsync(CancellationToken cancellationToken)
        {
            _reconnectAttempts++;
            var delay = ReconnectDelayMs * _reconnectAttempts;
            WPF.App.ErideMessage.AddMessage($"Reconnection attempt {_reconnectAttempts}/{MaxReconnectAttempts} in {delay / 1000} seconds", new Erida.MessageType { Type = Erida.MessageType.MessageTypeEnum.Debug });

            try
            {
                await Task.Delay(delay, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Ignore cancellation during delay
            }
        }

        private void ProcessMessageEvent(NewMessageEvent messageEvent)
        {
            try
            {
                var messageId = messageEvent.Message.Id;

                // Проверяем, не обрабатывали ли мы уже это сообщение
                lock (_processedMessagesLock)
                {
                    if (_processedMessageIds.Contains(messageId))
                    {
                        WPF.App.ErideMessage.AddMessage($"Duplicate message ignored: ID={messageId}", new Erida.MessageType { Type = Erida.MessageType.MessageTypeEnum.Debug });
                        return;
                    }

                    // Добавляем в кэш обработанных сообщений
                    _processedMessageIds.Add(messageId);

                    // Очищаем кэш, если он стал слишком большим
                    if (_processedMessageIds.Count > MaxProcessedMessagesCacheSize)
                    {
                        _processedMessageIds.Clear();
                        _processedMessageIds.Add(messageId);
                    }
                }

                var message = new MessageModel
                {
                    ChatId = messageEvent.ChatId,
                    MessageId = messageEvent.Message.Id,
                    SenderId = messageEvent.Message.SenderId,
                    Text = messageEvent.Message.Content.Text,
                    SentAt = messageEvent.Message.SentAt,
                    Attachments = messageEvent.Message.Content.Attachments
                        .Select(a => new AttachmentsModel
                        {
                            Id = a.Id,
                            Type = a.Type,
                            FileId = a.FileId,
                            PreviewUrl = a.PreviewUrl,
                            Size = a.AttachmentSize
                        }).ToList(),
                    IsSystemMessage = messageEvent.Message.Type == MessageContentType.System,
                    ReadBy = messageEvent.Message.ReadBy.ToList(),
                    Type = messageEvent.Message.Type
                };

                // Log the received message
                var textPreview = messageEvent.Message.Content.Text;
                if (!string.IsNullOrEmpty(textPreview) && textPreview.Length > 50)
                {
                    textPreview = textPreview.Substring(0, 50) + "...";
                }
                string messageInfo = $"New message in chat {messageEvent.ChatId}: " +
                                    $"ID={messageEvent.Message.Id}, " +
                                    $"Sender={messageEvent.Message.SenderId}, " +
                                    $"Text={textPreview ?? "(no text)"}";

                WPF.App.ErideMessage.AddMessage(messageInfo, new Erida.MessageType { Type = Erida.MessageType.MessageTypeEnum.Debug });

                // Save to cache
                WPF.App.CacheManager.SaveMessage(message.ChatId, string.Empty, message, MessageOperation.Added);

                // Raise event for UI updates
                NewMessageReceived?.Invoke(messageEvent.ChatId, message);
            }
            catch (Exception ex)
            {
                WPF.App.ErideMessage.AddMessage($"Error processing message event: {ex.Message}", new Erida.MessageType { Type = Erida.MessageType.MessageTypeEnum.Error });
            }
        }

        /// <summary>
        /// Resets the reconnection counter and forces a new connection attempt
        /// </summary>
        public void ResetAndReconnect(GlobalParam globalParam)
        {
            Stop();
            _reconnectAttempts = 0;
            Start(globalParam);
        }

        /// <summary>
        /// Запускает глобальную подписку на подтверждение о прочтении
        /// </summary>
        public void StartGlobalReadReceiptSubscription(GlobalParam globalParam)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(RealtimeUpdateService));

            if (_readReceiptStreamingTask != null && !_readReceiptStreamingTask.IsCompleted)
            {
                WPF.App.ErideMessage.AddMessage("Global read receipt subscription is already running", new Erida.MessageType { Type = Erida.MessageType.MessageTypeEnum.Debug });
                return;
            }

            _readReceiptCancellationTokenSource = new CancellationTokenSource();
            _readReceiptStreamingTask = Task.Run(() => StreamReadReceiptsAsync(globalParam, _readReceiptCancellationTokenSource.Token));

            WPF.App.ErideMessage.AddMessage("Global read receipt subscription started", new Erida.MessageType { Type = Erida.MessageType.MessageTypeEnum.Debug });
        }

        /// <summary>
        /// Stops the global read receipt subscription
        /// </summary>
        public void StopGlobalReadReceiptSubscription()
        {
            if (_readReceiptCancellationTokenSource != null)
            {
                _readReceiptCancellationTokenSource.Cancel();
                _readReceiptCancellationTokenSource.Dispose();
                _readReceiptCancellationTokenSource = null;
            }

            WPF.App.ErideMessage.AddMessage("Global read receipt subscription stopped", new Erida.MessageType { Type = Erida.MessageType.MessageTypeEnum.Debug });
        }

        private async Task StreamReadReceiptsAsync(GlobalParam globalParam, CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    WPF.App.ErideMessage.AddMessage("Connecting to read receipts stream...", new Erida.MessageType { Type = Erida.MessageType.MessageTypeEnum.Debug });

                    var (error, stream) = await WPF.App.ServerCommunication.SubscribeToReadReceipts(globalParam);

                    if (!error.IsSuccess || stream == null)
                    {
                        WPF.App.ErideMessage.AddMessage($"Failed to connect to read receipts stream: {error.ErrorMessage}", new Erida.MessageType { Type = Erida.MessageType.MessageTypeEnum.Error });
                        await Task.Delay(ReconnectDelayMs, cancellationToken);
                        continue;
                    }

                    WPF.App.ErideMessage.AddMessage("Connected to read receipts stream", new Erida.MessageType { Type = Erida.MessageType.MessageTypeEnum.Debug });

                    await foreach (var update in stream.WithCancellation(cancellationToken))
                    {
                        ProcessReadReceiptUpdate(update);
                    }
                }
                catch (OperationCanceledException)
                {
                    // Normal cancellation
                    break;
                }
                catch (Exception ex)
                {
                    WPF.App.ErideMessage.AddMessage($"Error in read receipts stream: {ex.Message}", new Erida.MessageType { Type = Erida.MessageType.MessageTypeEnum.Error });

                    if (!cancellationToken.IsCancellationRequested)
                    {
                        await Task.Delay(ReconnectDelayMs, cancellationToken);
                    }
                }
            }
        }

        private void ProcessReadReceiptUpdate(MessageReadEvent update)
        {
            try
            {
                WPF.App.ErideMessage.AddMessage(
                    $"Read receipt update: Chat={update.ChatId}, Message={update.MessageId}, ReadBy=[{string.Join(",", update.NewReadBy)}]",
                    new Erida.MessageType { Type = Erida.MessageType.MessageTypeEnum.Debug });

                ReadReceiptReceived?.Invoke(update);
            }
            catch (Exception ex)
            {
                WPF.App.ErideMessage.AddMessage($"Error processing read receipt update: {ex.Message}", new Erida.MessageType { Type = Erida.MessageType.MessageTypeEnum.Error });
            }
        }

        public void Dispose()
        {
            if (_disposed) return;

            Stop();
            StopGlobalReadReceiptSubscription();
            _disposed = true;
        }
    }
}
