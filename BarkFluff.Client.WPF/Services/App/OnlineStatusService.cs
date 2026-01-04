using BarkFluff.Proto.Onliner;
using BarkFluff.WebApi.Core.MessengerData;
using System.Collections.Concurrent;

namespace BarkFluff.Client.WPF.Services.App
{
    /// <summary>
    /// Singleton service for managing online status of users via gRPC streaming
    /// </summary>
    public class OnlineStatusService : IDisposable
    {
        private static OnlineStatusService? _instance;
        private static readonly object _lock = new object();

        private CancellationTokenSource? _streamCancellationTokenSource;
        private Task? _streamingTask;
        private bool _isConnected;
        private bool _disposed;
        private GlobalParam? _currentGlobalParam;

        // Хранение статусов пользователей
        private readonly ConcurrentDictionary<long, UserOnlineStatus> _userStatuses = new();

        // Список отслеживаемых пользователей
        private readonly HashSet<long> _trackedUserIds = new();
        private readonly object _trackedUsersLock = new();

        // Таймер для периодических пингов
        private System.Timers.Timer? _pingTimer;
        private const int PingIntervalMs = 30000; // 30 секунд

        /// <summary>
        /// Event raised when user's online status changes
        /// </summary>
        public event Action<UserOnlineStatus>? OnlineStatusChanged;

        /// <summary>
        /// Event raised when connection status changes
        /// </summary>
        public event Action<bool>? ConnectionStatusChanged;

        /// <summary>
        /// Gets the singleton instance of the service
        /// </summary>
        public static OnlineStatusService Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        _instance ??= new OnlineStatusService();
                    }
                }
                return _instance;
            }
        }

        private OnlineStatusService()
        {
            _isConnected = false;
        }

        /// <summary>
        /// Indicates whether the service is connected to the online status stream
        /// </summary>
        public bool IsConnected => _isConnected;

        /// <summary>
        /// Starts the online status service with given tracked user IDs
        /// </summary>
        public void Start(GlobalParam globalParam, List<long> initialTrackedUserIds)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(OnlineStatusService));

            if (_streamingTask != null && !_streamingTask.IsCompleted)
            {
                WPF.App.ErideMessage.AddMessage("Online status service is already running", new Erida.MessageType { Type = Erida.MessageType.MessageTypeEnum.Debug });
                return;
            }

            _currentGlobalParam = globalParam;

            lock (_trackedUsersLock)
            {
                _trackedUserIds.Clear();
                foreach (var userId in initialTrackedUserIds)
                {
                    _trackedUserIds.Add(userId);
                }
            }

            _streamCancellationTokenSource = new CancellationTokenSource();
            _streamingTask = Task.Run(() => StreamOnlineStatusAsync(globalParam, _streamCancellationTokenSource.Token));

            WPF.App.ErideMessage.AddMessage("Online status service started", new Erida.MessageType { Type = Erida.MessageType.MessageTypeEnum.Debug });
        }

        /// <summary>
        /// Stops the online status service
        /// </summary>
        public void Stop()
        {
            if (_streamCancellationTokenSource != null)
            {
                _streamCancellationTokenSource.Cancel();
                _streamCancellationTokenSource.Dispose();
                _streamCancellationTokenSource = null;
            }

            StopPinging();

            _isConnected = false;
            ConnectionStatusChanged?.Invoke(false);
            WPF.App.ErideMessage.AddMessage("Online status service stopped", new Erida.MessageType { Type = Erida.MessageType.MessageTypeEnum.Debug });
        }

        /// <summary>
        /// Starts sending periodic pings to indicate user is online
        /// </summary>
        public void StartPinging()
        {
            if (_pingTimer != null)
                return;

            _pingTimer = new System.Timers.Timer(PingIntervalMs);
            _pingTimer.Elapsed += async (sender, e) => await SendOnlinePingAsync();
            _pingTimer.AutoReset = true;
            _pingTimer.Start();

            // Send first ping immediately
            _ = Task.Run(SendOnlinePingAsync);

            WPF.App.ErideMessage.AddMessage("Online status pinging started", new Erida.MessageType { Type = Erida.MessageType.MessageTypeEnum.Debug });
        }

        /// <summary>
        /// Stops sending periodic pings
        /// </summary>
        public void StopPinging()
        {
            if (_pingTimer != null)
            {
                _pingTimer.Stop();
                _pingTimer.Dispose();
                _pingTimer = null;
                WPF.App.ErideMessage.AddMessage("Online status pinging stopped", new Erida.MessageType { Type = Erida.MessageType.MessageTypeEnum.Debug });
            }
        }

        /// <summary>
        /// Updates the list of tracked users
        /// </summary>
        public async Task UpdateTrackedUsers(List<long> userIds)
        {
            if (_currentGlobalParam == null)
                return;

            lock (_trackedUsersLock)
            {
                _trackedUserIds.Clear();
                foreach (var userId in userIds)
                {
                    _trackedUserIds.Add(userId);
                }
            }

            try
            {
                var error = await WPF.App.ServerCommunication.ChangeUsersInSubscription(_currentGlobalParam, userIds);
                if (!error.IsSuccess)
                {
                    WPF.App.ErideMessage.AddMessage($"Failed to update tracked users: {error.ErrorMessage}", new Erida.MessageType { Type = Erida.MessageType.MessageTypeEnum.Error });
                }
                else
                {
                    WPF.App.ErideMessage.AddMessage($"Tracked users updated: {userIds.Count} users", new Erida.MessageType { Type = Erida.MessageType.MessageTypeEnum.Debug });
                }
            }
            catch (Exception ex)
            {
                WPF.App.ErideMessage.AddMessage($"Error updating tracked users: {ex.Message}", new Erida.MessageType { Type = Erida.MessageType.MessageTypeEnum.Error });
            }
        }

        /// <summary>
        /// Adds users to the tracked list
        /// </summary>
        public async Task AddTrackedUsers(params long[] userIds)
        {
            List<long> updatedList;
            lock (_trackedUsersLock)
            {
                foreach (var userId in userIds)
                {
                    _trackedUserIds.Add(userId);
                }
                updatedList = _trackedUserIds.ToList();
            }

            await UpdateTrackedUsers(updatedList);
        }

        /// <summary>
        /// Removes users from the tracked list
        /// </summary>
        public async Task RemoveTrackedUsers(params long[] userIds)
        {
            List<long> updatedList;
            lock (_trackedUsersLock)
            {
                foreach (var userId in userIds)
                {
                    _trackedUserIds.Remove(userId);
                }
                updatedList = _trackedUserIds.ToList();
            }

            await UpdateTrackedUsers(updatedList);
        }

        /// <summary>
        /// Gets the online status of a user
        /// </summary>
        public UserOnlineStatus? GetUserStatus(long userId)
        {
            _userStatuses.TryGetValue(userId, out var status);
            return status;
        }

        /// <summary>
        /// Gets all cached user statuses
        /// </summary>
        public Dictionary<long, UserOnlineStatus> GetAllStatuses()
        {
            return new Dictionary<long, UserOnlineStatus>(_userStatuses);
        }

        private async Task StreamOnlineStatusAsync(GlobalParam globalParam, CancellationToken cancellationToken)
        {
            int reconnectAttempts = 0;
            const int InitialReconnectDelayMs = 2000;
            const int MaxReconnectDelayMs = 30000;

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    WPF.App.ErideMessage.AddMessage("Connecting to online status stream...", new Erida.MessageType { Type = Erida.MessageType.MessageTypeEnum.Debug });

                    List<long> currentTrackedUsers;
                    lock (_trackedUsersLock)
                    {
                        currentTrackedUsers = _trackedUserIds.ToList();
                    }

                    var (error, stream) = await WPF.App.ServerCommunication.SubscribeToOnlineStatus(globalParam, currentTrackedUsers);

                    if (!error.IsSuccess || stream == null)
                    {
                        WPF.App.ErideMessage.AddMessage($"Failed to connect to online status stream: {error.ErrorMessage}", new Erida.MessageType { Type = Erida.MessageType.MessageTypeEnum.Error });
                        await HandleReconnectionAsync(reconnectAttempts, cancellationToken);
                        reconnectAttempts++;
                        continue;
                    }

                    _isConnected = true;
                    reconnectAttempts = 0;
                    ConnectionStatusChanged?.Invoke(true);
                    WPF.App.ErideMessage.AddMessage("Connected to online status stream", new Erida.MessageType { Type = Erida.MessageType.MessageTypeEnum.Debug });

                    await foreach (var statusUpdate in stream.WithCancellation(cancellationToken))
                    {
                        ProcessStatusUpdate(statusUpdate);
                    }

                    // Stream ended normally - attempt to reconnect
                    WPF.App.ErideMessage.AddMessage("Online status stream ended, reconnecting...", new Erida.MessageType { Type = Erida.MessageType.MessageTypeEnum.Warning });
                    _isConnected = false;
                    ConnectionStatusChanged?.Invoke(false);
                }
                catch (OperationCanceledException)
                {
                    // Normal cancellation
                    break;
                }
                catch (Exception ex)
                {
                    WPF.App.ErideMessage.AddMessage($"Error in online status stream: {ex.Message}", new Erida.MessageType { Type = Erida.MessageType.MessageTypeEnum.Error });
                    _isConnected = false;
                    ConnectionStatusChanged?.Invoke(false);

                    if (!cancellationToken.IsCancellationRequested)
                    {
                        await HandleReconnectionAsync(reconnectAttempts, cancellationToken);
                        reconnectAttempts++;
                    }
                }
            }

            _isConnected = false;
            ConnectionStatusChanged?.Invoke(false);
        }

        private async Task HandleReconnectionAsync(int attempts, CancellationToken cancellationToken)
        {
            int delay = Math.Min(2000 * (int)Math.Pow(2, Math.Min(attempts, 10)), 30000);
            WPF.App.ErideMessage.AddMessage($"Reconnection attempt {attempts + 1} in {delay / 1000} seconds", new Erida.MessageType { Type = Erida.MessageType.MessageTypeEnum.Debug });

            try
            {
                await Task.Delay(delay, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Ignore cancellation during delay
            }
        }

        private void ProcessStatusUpdate(UserOnlineStatus statusUpdate)
        {
            try
            {
                _userStatuses[statusUpdate.UserId] = statusUpdate;

                var statusText = statusUpdate.Status switch
                {
                    StatusTypeId.StatusOnline => "online",
                    StatusTypeId.StatusOffline => "offline",
                    _ => "unknown"
                };

                WPF.App.ErideMessage.AddMessage(
                    $"User {statusUpdate.UserId} status updated: {statusText} (last seen: {statusUpdate.LastSeen})",
                    new Erida.MessageType { Type = Erida.MessageType.MessageTypeEnum.Debug });

                // Raise event for UI updates
                OnlineStatusChanged?.Invoke(statusUpdate);
            }
            catch (Exception ex)
            {
                WPF.App.ErideMessage.AddMessage($"Error processing status update: {ex.Message}", new Erida.MessageType { Type = Erida.MessageType.MessageTypeEnum.Error });
            }
        }

        private async Task SendOnlinePingAsync()
        {
            if (_currentGlobalParam == null || !_isConnected)
                return;

            try
            {
                var error = await WPF.App.ServerCommunication.SetOnlineStatus(_currentGlobalParam);
                if (!error.IsSuccess)
                {
                    WPF.App.ErideMessage.AddMessage($"Failed to send online ping: {error.ErrorMessage}", new Erida.MessageType { Type = Erida.MessageType.MessageTypeEnum.Warning });
                }
            }
            catch (Exception ex)
            {
                WPF.App.ErideMessage.AddMessage($"Error sending online ping: {ex.Message}", new Erida.MessageType { Type = Erida.MessageType.MessageTypeEnum.Error });
            }
        }

        public void Dispose()
        {
            if (_disposed) return;

            Stop();
            _disposed = true;
        }
    }
}
