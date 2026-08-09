using BarkFluff.WebApi.Core.MessengerData;

using Grpc.Core;

namespace BarkFluff.WebApi.Core.Managers
{
    /// <summary>
    /// Менеджер для работы с токенами и безопасными вызовами API.
    /// </summary>
    internal class WebApiTokenManager : WebApiBase
    {
        private readonly WebApi _webApi;

        /// <summary>
        /// Событие вызывается когда refresh-токен стал недействителен (отозван, заблокирован через админ-панель, истёк срок действия).
        /// Приложение должно перенаправить пользователя на страницу выбора сервера для повторной авторизации.
        /// </summary>
        public event EventHandler? TokenInvalidated;

        /// <summary>
        /// Событие вызывается после успешного проактивного обновления токена.
        /// Подписчики должны пересоздать стриминговые gRPC-соединения с новым токеном.
        /// </summary>
        public event EventHandler? TokenRefreshed;

        private CancellationTokenSource? _autoRefreshCts;

        // Сериализация refresh-токена: один refresh за раз на весь WebApi.
        // Refresh-токен на сервере одноразовый (rotation) — параллельные вызовы
        // инвалидируют друг друга и приводят к cascade logout.
        private readonly SemaphoreSlim _refreshLock = new(1, 1);
        private Task<bool>? _ongoingRefresh;

        public WebApiTokenManager(WebApi webApi) : base(webApi)
        {
            _webApi = webApi;
        }

        /// <summary>
        /// Запускает refresh-токен, гарантируя что одновременно работает только один.
        /// Если refresh уже идёт — ждём его результат. Если access-токен уже сменился
        /// между моментом перехвата ошибки и захватом lock — refresh пропускаем.
        /// </summary>
        /// <param name="globalParam">Параметры с актуальным токеном</param>
        /// <param name="staleAccessTokenValue">Значение access-токена, которое вызвало ошибку</param>
        /// <returns>true — токен валиден (обновлён нами или другим вызовом), false — refresh-токен мёртв</returns>
        private async Task<bool> RefreshOnceAsync(GlobalParam globalParam, string staleAccessTokenValue)
        {
            Task<bool> refreshTask;
            bool isOwner = false;

            await _refreshLock.WaitAsync();
            try
            {
                // Кто-то уже успел обновить токен пока мы ждали lock — повторяем сразу.
                if (globalParam.AccessToken?.Value != staleAccessTokenValue)
                {
                    return true;
                }

                if (_ongoingRefresh == null || _ongoingRefresh.IsCompleted)
                {
                    _ongoingRefresh = DoRefreshAsync(globalParam);
                    isOwner = true;
                }
                refreshTask = _ongoingRefresh;
            }
            finally
            {
                _refreshLock.Release();
            }

            var success = await refreshTask;

            if (isOwner)
            {
                await _refreshLock.WaitAsync();
                try { _ongoingRefresh = null; }
                finally { _refreshLock.Release(); }
            }

            return success;
        }

        /// <summary>
        /// Фактически обновляет токен и переинициализирует gRPC-клиентов.
        /// Должен вызываться только из RefreshOnceAsync (под lock'ом).
        /// </summary>
        private async Task<bool> DoRefreshAsync(GlobalParam globalParam)
        {
            var (result, _) = await TokenUpdate(globalParam);
            if (!result.IsSuccess) return false;

            if (_webApi.ClientManager._initParams.HasValue)
            {
                var p = _webApi.ClientManager._initParams.Value;
                _webApi.ClientManager.AddInterceptor(
                    globalParam, p.DeviceName, p.Os, p.AppName, p.AppVersion, p.Ip);
            }
            return true;
        }

        /// <summary>
        /// Запускает фоновый автоматический обновитель токена.
        /// Токен обновляется за 1 минуту до истечения (при времени жизни 5 минут — раз в ~4 минуты).
        /// После успешного обновления вызывается событие <see cref="TokenRefreshed"/>.
        /// </summary>
        /// <param name="globalParam">Глобальные параметры с токеном</param>
        public void StartAutoRefresh(GlobalParam globalParam)
        {
            StopAutoRefresh();
            _autoRefreshCts = new CancellationTokenSource();
            var cts = _autoRefreshCts;

            _ = Task.Run(async () =>
            {
                // Проверяем каждые 30 секунд — дёшево и точно ловим окно «за 1 минуту до»
                using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
                try
                {
                    while (await timer.WaitForNextTickAsync(cts.Token))
                    {
                        if (globalParam?.AccessToken == null) continue;

                        var expirationTime = globalParam.AccessToken.ExpirationDate?.ToDateTime();
                        if (expirationTime == null) continue;

                        // Если до истечения меньше 1 минуты — обновляем
                        var timeLeft = expirationTime.Value - DateTime.UtcNow;
                        if (timeLeft <= TimeSpan.FromMinutes(1))
                        {
                            var staleToken = globalParam.AccessToken?.Value ?? string.Empty;
                            var success = await RefreshOnceAsync(globalParam, staleToken);
                            if (!success)
                            {
                                // refresh-токен мёртв — уведомляем приложение
                                TokenInvalidated?.Invoke(this, EventArgs.Empty);
                                return;
                            }

                            // Уведомляем подписчиков — нужно пересоздать стримы
                            TokenRefreshed?.Invoke(this, EventArgs.Empty);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    // Нормальная остановка через StopAutoRefresh()
                }
            }, cts.Token);
        }

        /// <summary>
        /// Останавливает фоновый автоматический обновитель токена.
        /// </summary>
        public void StopAutoRefresh()
        {
            if (_autoRefreshCts == null) return;
            _autoRefreshCts.Cancel();
            _autoRefreshCts.Dispose();
            _autoRefreshCts = null;
        }

        /// <summary>
        /// Обновляет токен доступа для приложения.
        /// </summary>
        public async Task<(ErrorReturner, string)> TokenUpdate(GlobalParam globalParam)
        {
            try
            {
                var response = await IdentityAC!.CreateTokenAsync(new BarkFluff.Proto.Identity.CreateTokenRequest { RefreshToken = globalParam.RefreshToken.Value });
                globalParam.AccessToken = response.AccessToken;
                return (new ErrorReturner(true), response.AccessToken.Value);
            }
            catch (Exception)
            {
                return (new ErrorReturner(false, "Ошибка обновления токена"), "");
            }
        }

        /// <summary>
        /// Вызов API с обработкой возможных ошибок, связанных с токеном.
        /// </summary>
        public async Task<T> ExecuteWithTokenRefresh<T>(GlobalParam globalParam, Func<Task<T>> operation, bool allowRetry = true)
        {
            try
            {
                return await operation();
            }
            catch (Exception ex) when (IsTransientConnectionError(ex) && allowRetry)
            {
                // Первый вызов на свежесозданном gRPC-канале иногда не успевает поднять
                // TLS/HTTP2-соединение за один заход (Unavailable/DeadlineExceeded) — повтор
                // идёт уже по прогретому соединению и обычно проходит.
                return await ExecuteWithTokenRefresh(globalParam, operation, allowRetry: false);
            }
            catch (Exception ex) when (IsTokenRelatedError(ex) && allowRetry)
            {
                if (globalParam?.RefreshToken == null)
                {
                    throw new InvalidOperationException("Refresh token is not available for token renewal", ex);
                }

                if (_webApi.ClientManager._initParams == null)
                {
                    throw new InvalidOperationException("Initialization parameters are not available for client reinitialization", ex);
                }

                try
                {
                    // Сериализованный refresh: параллельные Unauthenticated дожидаются одного TokenUpdate,
                    // а не запускают каждый свой (refresh-токен одноразовый — race логаутил пользователя).
                    var staleToken = globalParam.AccessToken?.Value ?? string.Empty;
                    var success = await RefreshOnceAsync(globalParam, staleToken);

                    if (!success)
                    {
                        TokenInvalidated?.Invoke(this, EventArgs.Empty);
                        return default(T)!;
                    }

                    // Повторяем операцию (только один раз, чтобы избежать бесконечной рекурсии)
                    return await ExecuteWithTokenRefresh(globalParam, operation, allowRetry: false);
                }
                catch (Exception refreshEx) when (refreshEx is not InvalidOperationException)
                {
                    throw new InvalidOperationException("Failed to refresh token and retry operation", refreshEx);
                }
            }
        }

        /// <summary>
        /// Проверяет, является ли ошибка связанной с токеном доступа.
        /// </summary>
        private bool IsTokenRelatedError(Exception ex)
        {
            if (ex is RpcException rpcEx)
            {
                return rpcEx.StatusCode == StatusCode.Unauthenticated ||
                       rpcEx.StatusCode == StatusCode.PermissionDenied ||
                       (rpcEx.Status.Detail?.Contains("token", StringComparison.OrdinalIgnoreCase) == true) ||
                       (rpcEx.Status.Detail?.Contains("unauthorized", StringComparison.OrdinalIgnoreCase) == true) ||
                       (rpcEx.Status.Detail?.Contains("expired", StringComparison.OrdinalIgnoreCase) == true);
            }

            return false;
        }

        /// <summary>
        /// Ошибка соединения (не токена): не достучались до сервера или не успели за дедлайн.
        /// </summary>
        private static bool IsTransientConnectionError(Exception ex) =>
            ex is RpcException { StatusCode: StatusCode.Unavailable or StatusCode.DeadlineExceeded };

        public async Task<TResponse> SafeCallAsync<TResponse>(Func<Task<TResponse>> apiCall, GlobalParam globalParam)
        {
            return await ExecuteWithTokenRefresh(globalParam, apiCall);
        }

        /// <summary>
        /// Проверяет срок действия токена и обновляет его при необходимости.
        /// Используется перед переподключением streaming соединений.
        /// </summary>
        /// <param name="globalParam">Глобальные параметры с токеном</param>
        /// <param name="bufferMinutes">Запас времени до истечения токена (по умолчанию 5 минут)</param>
        /// <returns>Результат операции</returns>
        public async Task<ErrorReturner> EnsureTokenValidAsync(GlobalParam globalParam, int bufferMinutes = 5)
        {
            if (globalParam?.AccessToken == null)
                return new ErrorReturner(false, "AccessToken is null");

            // Проверяем срок действия токена с запасом
            var expirationTime = globalParam.AccessToken.ExpirationDate?.ToDateTime();
            if (expirationTime.HasValue && expirationTime.Value <= DateTime.UtcNow.AddMinutes(bufferMinutes))
            {
                // Сериализованный refresh — параллельные вызывающие подождут один TokenUpdate.
                var staleToken = globalParam.AccessToken?.Value ?? string.Empty;
                var success = await RefreshOnceAsync(globalParam, staleToken);
                if (!success)
                    return new ErrorReturner(false, "Ошибка обновления токена");
            }

            return new ErrorReturner(true);
        }

        /// <summary>
        /// Принудительно обновляет токен и переинициализирует клиентов.
        /// Используется когда известно, что токен недействителен.
        /// </summary>
        public async Task<ErrorReturner> ForceRefreshTokenAsync(GlobalParam globalParam)
        {
            if (globalParam?.RefreshToken == null)
                return new ErrorReturner(false, "RefreshToken is null");

            // Сериализованный refresh; если параллельно идёт другой — присоединяемся к нему.
            var staleToken = globalParam.AccessToken?.Value ?? string.Empty;
            var success = await RefreshOnceAsync(globalParam, staleToken);
            return success
                ? new ErrorReturner(true)
                : new ErrorReturner(false, "Ошибка обновления токена");
        }
    }
}
