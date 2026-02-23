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

        public WebApiTokenManager(WebApi webApi) : base(webApi)
        {
            _webApi = webApi;
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
                    var (tokenUpdateResult, _) = await TokenUpdate(globalParam);

                    // Если обновление токена не удалось — refresh-токен мёртв
                    if (!tokenUpdateResult.IsSuccess)
                    {
                        // Уведомляем приложение о том, что токен недействителен
                        TokenInvalidated?.Invoke(this, EventArgs.Empty);
                        
                        // Возвращаем значение по умолчанию для типа T
                        // Приложение уже получило уведомление и должно перенаправить пользователя на страницу авторизации
                        return default(T)!;
                    }

                    // Переинициализируем клиентов с новым токеном
                    var initParams = _webApi.ClientManager._initParams.Value;
                    _webApi.ClientManager.AddInterceptor(globalParam, initParams.DeviceName, initParams.Os,
                                 initParams.AppName, initParams.AppVersion, initParams.Ip);

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
                // Токен истёк или скоро истечёт - обновляем
                var (result, _) = await TokenUpdate(globalParam);
                if (!result.IsSuccess)
                    return result;

                // Переинициализируем клиентов с новым токеном
                if (_webApi.ClientManager._initParams.HasValue)
                {
                    var initParams = _webApi.ClientManager._initParams.Value;
                    var reinitResult = _webApi.ClientManager.AddInterceptor(
                        globalParam,
                        initParams.DeviceName,
                        initParams.Os,
                        initParams.AppName,
                        initParams.AppVersion,
                        initParams.Ip);

                    if (!reinitResult.IsSuccess)
                        return reinitResult;
                }
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

            var (result, _) = await TokenUpdate(globalParam);
            if (!result.IsSuccess)
                return result;

            // Переинициализируем клиентов с новым токеном
            if (_webApi.ClientManager._initParams.HasValue)
            {
                var initParams = _webApi.ClientManager._initParams.Value;
                var reInitResult = _webApi.ClientManager.AddInterceptor(
                    globalParam,
                    initParams.DeviceName,
                    initParams.Os,
                    initParams.AppName,
                    initParams.AppVersion,
                    initParams.Ip);

                if (!reInitResult.IsSuccess)
                    return reInitResult;
            }

            return new ErrorReturner(true);
        }
    }
}
