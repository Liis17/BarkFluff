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
                    await TokenUpdate(globalParam);

                    // Переинициализируем клиентов с новым токеном
                    var initParams = _webApi.ClientManager._initParams.Value;
                    _webApi.ClientManager.AddInterceptor(globalParam, initParams.DeviceName, initParams.Os,
                                 initParams.AppName, initParams.AppVersion, initParams.Ip);

                    // Повторяем операцию (только один раз, чтобы избежать бесконечной рекурсии)
                    return await ExecuteWithTokenRefresh(globalParam, operation, allowRetry: false);
                }
                catch (Exception refreshEx)
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
    }
}
