using BarkFluff.Proto.Onliner;
using BarkFluff.WebApi.Core.MessengerData;

using Grpc.Core;

namespace BarkFluff.WebApi.Core.Managers
{
    /// <summary>
    /// Менеджер для работы с онлайн-статусами пользователей.
    /// </summary>
    internal class WebApiOnlinerManager : WebApiBase
    {
        private readonly WebApi _webApi;

        public WebApiOnlinerManager(WebApi webApi) : base(webApi)
        {
            _webApi = webApi;
        }

        /// <summary>
        /// Подписаться на изменения онлайн-статусов пользователей (streaming).
        /// </summary>
        public async Task<(ErrorReturner error, IAsyncEnumerable<UserOnlineStatus>? stream)> SubscribeToOnlineStatus(
            List<long> userIds,
            GlobalParam globalParam)
        {
            try
            {
                return await _webApi.TokenManager.SafeCallAsync(async () =>
                {
                    // Подготовка заголовков с токеном
                    var headers = new Metadata();
                    if (!string.IsNullOrEmpty(globalParam.AccessToken?.Value))
                    {
                        headers.Add("Authorization", $"Bearer {globalParam.AccessToken.Value}");
                    }

                    var request = new SubscribeToOnlineStatusRequest();
                    request.UserIds.AddRange(userIds);

                    // Вызов метода подписки с заголовками
                    var response = OnlinerAC!.SubscribeToOnlineStatus(request, headers);

                    // Создаём IAsyncEnumerable для стрима
                    async IAsyncEnumerable<UserOnlineStatus> GetStatusStream()
                    {
                        while (true)
                        {
                            bool hasNext;
                            UserOnlineStatus statusUpdate;

                            try
                            {
                                hasNext = await response.ResponseStream.MoveNext(CancellationToken.None);
                                if (!hasNext)
                                {
                                    yield break; // Стрим завершён
                                }
                                statusUpdate = response.ResponseStream.Current;
                            }
                            catch (RpcException)
                            {
                                yield break;
                            }
                            catch (Exception)
                            {
                                yield break;
                            }

                            yield return statusUpdate;
                        }
                    }

                    return ((ErrorReturner, IAsyncEnumerable<UserOnlineStatus>?))(new ErrorReturner(true, ""), GetStatusStream());
                }, globalParam);
            }
            catch (RpcException)
            {
                return (new ErrorReturner(false, "Ошибка аутентификации"), null);
            }
            catch (Exception)
            {
                return (new ErrorReturner(false, "Ошибка подключения к сервису онлайна"), null);
            }
        }

        /// <summary>
        /// Установить свой статус "В сети" (ping).
        /// </summary>
        public async Task<ErrorReturner> SetOnlineStatus(GlobalParam globalParam)
        {
            try
            {
                return await _webApi.TokenManager.SafeCallAsync(async () =>
                {
                    var request = new SetOnlineStatusRequest();
                    await OnlinerAC!.SetOnlineStatusAsync(request);
                    return new ErrorReturner(true);
                }, globalParam);
            }
            catch (RpcException)
            {
                return new ErrorReturner(false, "Ошибка аутентификации при установке статуса");
            }
            catch (Exception)
            {
                return new ErrorReturner(false, "Ошибка установки статуса онлайна");
            }
        }

        /// <summary>
        /// Получить текущие онлайн-статусы пользователей (без подписки).
        /// </summary>
        public async Task<(ErrorReturner error, GetOnlineStatusResponse? response)> GetOnlineStatus(
            List<long> userIds,
            GlobalParam globalParam)
        {
            try
            {
                return await _webApi.TokenManager.SafeCallAsync(async () =>
                {
                    var request = new GetOnlineStatusRequest();
                    request.UserIds.AddRange(userIds);

                    var response = await OnlinerAC!.GetOnlineStatusAsync(request);
                    return ((ErrorReturner, GetOnlineStatusResponse?))(new ErrorReturner(true), response);
                }, globalParam);
            }
            catch (RpcException)
            {
                return (new ErrorReturner(false, "Ошибка аутентификации при получении статусов"), null);
            }
            catch (Exception)
            {
                return (new ErrorReturner(false, "Ошибка получения статусов онлайна"), null);
            }
        }

        /// <summary>
        /// Изменить список отслеживаемых пользователей в существующей подписке.
        /// </summary>
        public async Task<ErrorReturner> ChangeUsersInSubscription(List<long> userIds, GlobalParam globalParam)
        {
            try
            {
                return await _webApi.TokenManager.SafeCallAsync(async () =>
                {
                    var request = new ChangeUsersInSubscriptionRequest();
                    request.UserIds.AddRange(userIds);

                    await OnlinerAC!.ChangeUsersInSubscriptionAsync(request);
                    return new ErrorReturner(true);
                }, globalParam);
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.FailedPrecondition)
            {
                return new ErrorReturner(false, "Нет активной подписки для изменения списка пользователей");
            }
            catch (RpcException)
            {
                return new ErrorReturner(false, "Ошибка аутентификации при изменении списка");
            }
            catch (Exception)
            {
                return new ErrorReturner(false, "Ошибка изменения списка отслеживаемых пользователей");
            }
        }
    }
}
