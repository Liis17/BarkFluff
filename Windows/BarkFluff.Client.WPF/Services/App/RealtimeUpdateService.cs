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
        private const int InitialReconnectDelayMs = 2000;
        private const int MaxReconnectDelayMs = 30000;
        private GlobalParam? _currentGlobalParam;

        // Read receipt streaming
        private CancellationTokenSource? _readReceiptCancellationTokenSource;
        private Task? _readReceiptStreamingTask;
        private bool _isReadReceiptConnected;
        private bool _readReceiptSubscriptionStarted;

        // Duplicate detection
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

            _currentGlobalParam = globalParam;
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
            // Infinite reconnection loop
            while (!cancellationToken.IsCancellationRequested)
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

                    // Stream ended normally - attempt to reconnect
                    WPF.App.ErideMessage.AddMessage("Updates stream ended, reconnecting...", new Erida.MessageType { Type = Erida.MessageType.MessageTypeEnum.Warning });
                    _isConnected = false;
                    ConnectionStatusChanged?.Invoke(false);

                    // Reconnect all streams when one disconnects
                    await ReconnectAllStreamsAsync(globalParam, cancellationToken);
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
                        // Reconnect all streams when one fails
                        await ReconnectAllStreamsAsync(globalParam, cancellationToken);
                    }
                }
            }

            _isConnected = false;
            ConnectionStatusChanged?.Invoke(false);
        }

        /// <summary>
        /// Calculates the backoff delay using exponential backoff with a cap
        /// </summary>
        private int CalculateBackoffDelay(int attempts)
        {
            return Math.Min(InitialReconnectDelayMs * (int)Math.Pow(2, Math.Min(attempts - 1, 10)), MaxReconnectDelayMs);
        }

        /// <summary>
        /// Notifies about connection failure if all streams are down
        /// </summary>
        private void NotifyConnectionFailureIfAllStreamsDown()
        {
            if (!_isConnected && !_isReadReceiptConnected)
            {
                ConnectionStatusChanged?.Invoke(false);
            }
        }

        private async Task HandleReconnectionAsync(CancellationToken cancellationToken)
        {
            _reconnectAttempts++;
            var delay = CalculateBackoffDelay(_reconnectAttempts);
            WPF.App.ErideMessage.AddMessage($"Reconnection attempt {_reconnectAttempts} in {delay / 1000} seconds", new Erida.MessageType { Type = Erida.MessageType.MessageTypeEnum.Debug });

            // Перед переподключением проверяем и обновляем токен при необходимости
            if (_currentGlobalParam != null)
            {
                try
                {
                    var tokenResult = await WPF.App.ServerCommunication.EnsureTokenValidAsync(_currentGlobalParam);
                    if (!tokenResult.IsSuccess)
                    {
                        WPF.App.ErideMessage.AddMessage($"Token refresh failed: {tokenResult.ErrorMessage}", new Erida.MessageType { Type = Erida.MessageType.MessageTypeEnum.Warning });

                        // Если обычное обновление не помогло и уже было несколько попыток - пробуем принудительное
                        if (_reconnectAttempts >= 3)
                        {
                            WPF.App.ErideMessage.AddMessage("Attempting forced token refresh...", new Erida.MessageType { Type = Erida.MessageType.MessageTypeEnum.Debug });
                            var forceResult = await WPF.App.ServerCommunication.ForceRefreshTokenAsync(_currentGlobalParam);
                            if (forceResult.IsSuccess)
                            {
                                WPF.App.ErideMessage.AddMessage("Token forcefully refreshed", new Erida.MessageType { Type = Erida.MessageType.MessageTypeEnum.Debug });
                            }
                        }
                    }
                    else
                    {
                        WPF.App.ErideMessage.AddMessage("Token validated/refreshed before reconnection", new Erida.MessageType { Type = Erida.MessageType.MessageTypeEnum.Debug });
                    }
                }
                catch (Exception ex)
                {
                    WPF.App.ErideMessage.AddMessage($"Error refreshing token: {ex.Message}", new Erida.MessageType { Type = Erida.MessageType.MessageTypeEnum.Warning });
                }
            }

            try
            {
                await Task.Delay(delay, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Ignore cancellation during delay
            }
        }

        /// <summary>
        /// Reconnects all realtime streams (messages and read receipts) when one connection fails
        /// </summary>
        private async Task ReconnectAllStreamsAsync(GlobalParam globalParam, CancellationToken cancellationToken)
        {
            await HandleReconnectionAsync(cancellationToken);

            if (cancellationToken.IsCancellationRequested)
                return;

            // Restart read receipt subscription if it was previously started
            if (_readReceiptSubscriptionStarted)
            {
                StopGlobalReadReceiptSubscription();
                StartGlobalReadReceiptSubscription(globalParam);
            }
        }

        private void ProcessMessageEvent(NewMessageEvent messageEvent)
        {
            try
            {
                var messageId = messageEvent.Message.Id;

                // Check if we have already processed this message
                lock (_processedMessagesLock)
                {
                    if (_processedMessageIds.Contains(messageId))
                    {
                        WPF.App.ErideMessage.AddMessage($"Duplicate message ignored: ID={messageId}", new Erida.MessageType { Type = Erida.MessageType.MessageTypeEnum.Debug });
                        return;
                    }

                    // Add to processed messages cache
                    _processedMessageIds.Add(messageId);

                    // Clear cache if it gets too large
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
        /// Starts the global read receipt subscription
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

            _readReceiptSubscriptionStarted = true;
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
            int readReceiptReconnectAttempts = 0;

            // Infinite reconnection loop
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    WPF.App.ErideMessage.AddMessage("Connecting to read receipts stream...", new Erida.MessageType { Type = Erida.MessageType.MessageTypeEnum.Debug });

                    var (error, stream) = await WPF.App.ServerCommunication.SubscribeToReadReceipts(globalParam);

                    if (!error.IsSuccess || stream == null)
                    {
                        WPF.App.ErideMessage.AddMessage($"Failed to connect to read receipts stream: {error.ErrorMessage}", new Erida.MessageType { Type = Erida.MessageType.MessageTypeEnum.Error });
                        _isReadReceiptConnected = false;
                        NotifyConnectionFailureIfAllStreamsDown();

                        readReceiptReconnectAttempts++;

                        // Проверяем и обновляем токен перед переподключением
                        await EnsureTokenBeforeReconnectAsync(globalParam, readReceiptReconnectAttempts);

                        var delay = CalculateBackoffDelay(readReceiptReconnectAttempts);
                        await Task.Delay(delay, cancellationToken);
                        continue;
                    }

                    _isReadReceiptConnected = true;
                    readReceiptReconnectAttempts = 0;
                    WPF.App.ErideMessage.AddMessage("Connected to read receipts stream", new Erida.MessageType { Type = Erida.MessageType.MessageTypeEnum.Debug });

                    await foreach (var update in stream.WithCancellation(cancellationToken))
                    {
                        ProcessReadReceiptUpdate(update);
                    }

                    // Stream ended normally - attempt to reconnect
                    WPF.App.ErideMessage.AddMessage("Read receipts stream ended, reconnecting...", new Erida.MessageType { Type = Erida.MessageType.MessageTypeEnum.Warning });
                    _isReadReceiptConnected = false;
                    NotifyConnectionFailureIfAllStreamsDown();
                }
                catch (OperationCanceledException)
                {
                    // Normal cancellation
                    break;
                }
                catch (Exception ex)
                {
                    WPF.App.ErideMessage.AddMessage($"Error in read receipts stream: {ex.Message}", new Erida.MessageType { Type = Erida.MessageType.MessageTypeEnum.Error });
                    _isReadReceiptConnected = false;
                    NotifyConnectionFailureIfAllStreamsDown();

                    if (!cancellationToken.IsCancellationRequested)
                    {
                        readReceiptReconnectAttempts++;

                        // Проверяем и обновляем токен перед переподключением
                        await EnsureTokenBeforeReconnectAsync(globalParam, readReceiptReconnectAttempts);

                        var delay = CalculateBackoffDelay(readReceiptReconnectAttempts);
                        await Task.Delay(delay, cancellationToken);
                    }
                }
            }

            _isReadReceiptConnected = false;
        }

        /// <summary>
        /// Проверяет и обновляет токен перед переподключением streaming соединения
        /// </summary>
        private async Task EnsureTokenBeforeReconnectAsync(GlobalParam globalParam, int reconnectAttempts)
        {
            try
            {
                var tokenResult = await WPF.App.ServerCommunication.EnsureTokenValidAsync(globalParam);
                if (!tokenResult.IsSuccess)
                {
                    WPF.App.ErideMessage.AddMessage($"Token validation failed: {tokenResult.ErrorMessage}", new Erida.MessageType { Type = Erida.MessageType.MessageTypeEnum.Warning });

                    // После нескольких неудачных попыток - принудительное обновление
                    if (reconnectAttempts >= 3)
                    {
                        WPF.App.ErideMessage.AddMessage("Attempting forced token refresh...", new Erida.MessageType { Type = Erida.MessageType.MessageTypeEnum.Debug });
                        var forceResult = await WPF.App.ServerCommunication.ForceRefreshTokenAsync(globalParam);
                        if (forceResult.IsSuccess)
                        {
                            WPF.App.ErideMessage.AddMessage("Token forcefully refreshed", new Erida.MessageType { Type = Erida.MessageType.MessageTypeEnum.Debug });
                        }
                    }
                }
                else
                {
                    WPF.App.ErideMessage.AddMessage("Token validated/refreshed before reconnection", new Erida.MessageType { Type = Erida.MessageType.MessageTypeEnum.Debug });
                }
            }
            catch (Exception ex)
            {
                WPF.App.ErideMessage.AddMessage($"Error during token refresh: {ex.Message}", new Erida.MessageType { Type = Erida.MessageType.MessageTypeEnum.Warning });
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
