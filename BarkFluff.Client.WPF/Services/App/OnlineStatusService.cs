using BarkFluff.Proto.Onliner;
using BarkFluff.WebApi.Core.MessengerData;
using System.ComponentModel;

namespace BarkFluff.Client.WPF.Services.App
{
    /// <summary>
    /// Singleton service for managing real-time online status updates via gRPC streaming
    /// </summary>
    public class OnlineStatusService : IDisposable
    {
        private static OnlineStatusService? _instance;
        private static readonly object _lock = new object();

        private CancellationTokenSource? _subscriptionCts;
        private Task? _streamingTask;
        private bool _isConnected;
        private bool _disposed;
        private int _reconnectAttempts;
        private const int InitialReconnectDelayMs = 2000;
        private const int MaxReconnectDelayMs = 30000;
        private GlobalParam? _currentGlobalParam;

        // Ping mechanism
        private CancellationTokenSource? _pingCts;
        private Task? _pingTask;
        private const int PingIntervalMs = 3000; // 3 seconds

        // Status cache and tracked users
        private readonly Dictionary<long, UserOnlineStatus> _statusCache = new();
        private readonly object _statusCacheLock = new();
        private readonly HashSet<long> _trackedUserIds = new();
        private readonly object _trackedUsersLock = new();

        // Debounced update timer
        private System.Timers.Timer? _updateDebounceTimer;
        private const int DebounceDelayMs = 500;

        /// <summary>
        /// Event raised when a user's online status changes
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
            _reconnectAttempts = 0;
        }

        /// <summary>
        /// Indicates whether the service is connected to the online status stream
        /// </summary>
        public bool IsConnected => _isConnected;

        /// <summary>
        /// Starts the online status streaming and subscribes to window state changes
        /// </summary>
        public void Start(GlobalParam globalParam)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(OnlineStatusService));

            if (_streamingTask != null && !_streamingTask.IsCompleted)
            {
                WPF.App.ErideMessage.AddMessage("Online status service is already running", new Erida.MessageType { Type = Erida.MessageType.MessageTypeEnum.Debug });
                return;
            }

            _currentGlobalParam = globalParam;
            _subscriptionCts = new CancellationTokenSource();
            _streamingTask = Task.Run(() => StreamOnlineStatusesAsync(globalParam, _subscriptionCts.Token));

            // Subscribe to window state changes for ping mechanism
            SubscribeToWindowState();

            WPF.App.ErideMessage.AddMessage("Online status service started", new Erida.MessageType { Type = Erida.MessageType.MessageTypeEnum.Debug });
        }

        /// <summary>
        /// Stops the online status streaming
        /// </summary>
        public void Stop()
        {
            if (_subscriptionCts != null)
            {
                _subscriptionCts.Cancel();
                _subscriptionCts.Dispose();
                _subscriptionCts = null;
            }

            StopPingTask();

            _isConnected = false;
            ConnectionStatusChanged?.Invoke(false);
            WPF.App.ErideMessage.AddMessage("Online status service stopped", new Erida.MessageType { Type = Erida.MessageType.MessageTypeEnum.Debug });
        }

        /// <summary>
        /// Adds a user to the tracked users list and updates subscription
        /// </summary>
        public void TrackUser(long userId)
        {
            lock (_trackedUsersLock)
            {
                if (_trackedUserIds.Add(userId))
                {
                    WPF.App.ErideMessage.AddMessage($"Tracking user {userId} for online status", new Erida.MessageType { Type = Erida.MessageType.MessageTypeEnum.Debug });
                    UpdateTrackedUsersDebounced();
                }
            }
        }

        /// <summary>
        /// Removes a user from the tracked users list and updates subscription
        /// </summary>
        public void UntrackUser(long userId)
        {
            lock (_trackedUsersLock)
            {
                if (_trackedUserIds.Remove(userId))
                {
                    WPF.App.ErideMessage.AddMessage($"Stopped tracking user {userId} for online status", new Erida.MessageType { Type = Erida.MessageType.MessageTypeEnum.Debug });
                    UpdateTrackedUsersDebounced();
                }
            }
        }

        /// <summary>
        /// Gets the cached online status for a user
        /// </summary>
        public UserOnlineStatus? GetCachedStatus(long userId)
        {
            lock (_statusCacheLock)
            {
                return _statusCache.TryGetValue(userId, out var status) ? status : null;
            }
        }

        private async Task StreamOnlineStatusesAsync(GlobalParam globalParam, CancellationToken cancellationToken)
        {
            // Infinite reconnection loop
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    List<long> userIds;
                    lock (_trackedUsersLock)
                    {
                        userIds = _trackedUserIds.ToList();
                    }

                    // If no users to track, wait and retry
                    if (userIds.Count == 0)
                    {
                        WPF.App.ErideMessage.AddMessage("No users to track, waiting...", new Erida.MessageType { Type = Erida.MessageType.MessageTypeEnum.Debug });
                        await Task.Delay(5000, cancellationToken);
                        continue;
                    }

                    WPF.App.ErideMessage.AddMessage($"Connecting to online status stream for {userIds.Count} users...", new Erida.MessageType { Type = Erida.MessageType.MessageTypeEnum.Debug });

                    var (error, stream) = await WPF.App.ServerCommunication.SubscribeToOnlineStatus(userIds, globalParam);

                    if (!error.IsSuccess || stream == null)
                    {
                        WPF.App.ErideMessage.AddMessage($"Failed to connect to online status stream: {error.ErrorMessage}", new Erida.MessageType { Type = Erida.MessageType.MessageTypeEnum.Error });
                        await HandleReconnectionAsync(cancellationToken);
                        continue;
                    }

                    _isConnected = true;
                    _reconnectAttempts = 0;
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
                        await HandleReconnectionAsync(cancellationToken);
                    }
                }
            }

            _isConnected = false;
            ConnectionStatusChanged?.Invoke(false);
        }

        private void ProcessStatusUpdate(UserOnlineStatus status)
        {
            try
            {
                bool shouldNotify = false;
                lock (_statusCacheLock)
                {
                    // Check if status has changed
                    if (!_statusCache.TryGetValue(status.UserId, out var cachedStatus) ||
                        cachedStatus.Status != status.Status ||
                        cachedStatus.LastSeen != status.LastSeen)
                    {
                        _statusCache[status.UserId] = status;
                        shouldNotify = true;
                    }
                }

                if (shouldNotify)
                {
                    var statusText = status.Status == StatusTypeId.StatusOnline ? "online" : "offline";
                    var lastSeenText = status.LastSeen != null ? $", last seen: {status.LastSeen.ToDateTime():yyyy-MM-dd HH:mm:ss}" : "";
                    WPF.App.ErideMessage.AddMessage(
                        $"User {status.UserId} status: {statusText}{lastSeenText}",
                        new Erida.MessageType { Type = Erida.MessageType.MessageTypeEnum.Debug });

                    OnlineStatusChanged?.Invoke(status);
                }
            }
            catch (Exception ex)
            {
                WPF.App.ErideMessage.AddMessage($"Error processing status update: {ex.Message}", new Erida.MessageType { Type = Erida.MessageType.MessageTypeEnum.Error });
            }
        }

        private async Task HandleReconnectionAsync(CancellationToken cancellationToken)
        {
            _reconnectAttempts++;
            var delay = CalculateBackoffDelay(_reconnectAttempts);
            WPF.App.ErideMessage.AddMessage($"Reconnection attempt {_reconnectAttempts} in {delay / 1000} seconds", new Erida.MessageType { Type = Erida.MessageType.MessageTypeEnum.Debug });

            try
            {
                await Task.Delay(delay, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Ignore cancellation during delay
            }
        }

        private int CalculateBackoffDelay(int attempts)
        {
            return Math.Min(InitialReconnectDelayMs * (int)Math.Pow(2, Math.Min(attempts - 1, 10)), MaxReconnectDelayMs);
        }

        /// <summary>
        /// Debounced method to update the tracked users subscription on the server
        /// </summary>
        private void UpdateTrackedUsersDebounced()
        {
            _updateDebounceTimer?.Stop();
            _updateDebounceTimer?.Dispose();

            _updateDebounceTimer = new System.Timers.Timer(DebounceDelayMs);
            _updateDebounceTimer.Elapsed += async (s, e) =>
            {
                _updateDebounceTimer?.Stop();
                await UpdateTrackedUsers();
            };
            _updateDebounceTimer.AutoReset = false;
            _updateDebounceTimer.Start();
        }

        private async Task UpdateTrackedUsers()
        {
            if (!_isConnected || _currentGlobalParam == null)
                return;

            List<long> userIds;
            lock (_trackedUsersLock)
            {
                userIds = _trackedUserIds.ToList();
            }

            try
            {
                WPF.App.ErideMessage.AddMessage($"Updating tracked users list: {userIds.Count} users", new Erida.MessageType { Type = Erida.MessageType.MessageTypeEnum.Debug });
                var result = await WPF.App.ServerCommunication.ChangeUsersInSubscription(userIds, _currentGlobalParam);

                if (!result.IsSuccess)
                {
                    WPF.App.ErideMessage.AddMessage($"Failed to update tracked users: {result.ErrorMessage}", new Erida.MessageType { Type = Erida.MessageType.MessageTypeEnum.Warning });
                }
            }
            catch (Exception ex)
            {
                WPF.App.ErideMessage.AddMessage($"Error updating tracked users: {ex.Message}", new Erida.MessageType { Type = Erida.MessageType.MessageTypeEnum.Error });
            }
        }

        /// <summary>
        /// Subscribe to window state changes to start/stop ping mechanism
        /// </summary>
        private void SubscribeToWindowState()
        {
            WPF.App.WindowStateService.IsApplicationActive.PropertyChanged += OnApplicationActiveChanged;
        }

        /// <summary>
        /// Unsubscribe from window state changes
        /// </summary>
        private void UnsubscribeFromWindowState()
        {
            WPF.App.WindowStateService.IsApplicationActive.PropertyChanged -= OnApplicationActiveChanged;
        }

        private void OnApplicationActiveChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (WPF.App.WindowStateService.IsApplicationActive.Value)
            {
                WPF.App.ErideMessage.AddMessage("Application became active, starting ping task", new Erida.MessageType { Type = Erida.MessageType.MessageTypeEnum.Debug });
                StartPingTask();
            }
            else
            {
                WPF.App.ErideMessage.AddMessage("Application became inactive, stopping ping task", new Erida.MessageType { Type = Erida.MessageType.MessageTypeEnum.Debug });
                StopPingTask();
            }
        }

        private void StartPingTask()
        {
            // If ping task is already running, don't start another
            if (_pingTask != null && !_pingTask.IsCompleted)
                return;

            if (_currentGlobalParam == null)
                return;

            _pingCts = new CancellationTokenSource();
            _pingTask = Task.Run(async () =>
            {
                while (!_pingCts.Token.IsCancellationRequested)
                {
                    try
                    {
                        var result = await WPF.App.ServerCommunication.SetOnlineStatus(_currentGlobalParam);

                        if (result.IsSuccess)
                        {
                            WPF.App.ErideMessage.AddMessage("Ping sent successfully", new Erida.MessageType { Type = Erida.MessageType.MessageTypeEnum.Debug });
                        }
                        else
                        {
                            WPF.App.ErideMessage.AddMessage($"Ping failed: {result.ErrorMessage}", new Erida.MessageType { Type = Erida.MessageType.MessageTypeEnum.Warning });
                        }

                        await Task.Delay(PingIntervalMs, _pingCts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        WPF.App.ErideMessage.AddMessage($"Error sending ping: {ex.Message}", new Erida.MessageType { Type = Erida.MessageType.MessageTypeEnum.Error });
                    }
                }
            });
        }

        private void StopPingTask()
        {
            if (_pingCts != null)
            {
                _pingCts.Cancel();
                _pingCts.Dispose();
                _pingCts = null;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;

            Stop();
            UnsubscribeFromWindowState();

            _updateDebounceTimer?.Stop();
            _updateDebounceTimer?.Dispose();

            _disposed = true;
        }
    }
}
