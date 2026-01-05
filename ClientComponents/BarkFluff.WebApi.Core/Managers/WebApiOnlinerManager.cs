using BarkFluff.Proto.Onliner;
using BarkFluff.WebApi.Core.MessengerData;
using Grpc.Core;

namespace BarkFluff.WebApi.Core.Managers
{
    /// <summary>
    /// Менеджер для работы с Onliner API (статус онлайн пользователей).
    /// </summary>
    internal class WebApiOnlinerManager : WebApiBase
    {
        private readonly WebApi _webApi;

        public WebApiOnlinerManager(WebApi webApi) : base(webApi)
        {
            _webApi = webApi;
        }

        /// <summary>
        /// Подписаться на изменения статуса онлайн указанных пользователей.
        /// </summary>
        public async Task<(ErrorReturner error, IAsyncEnumerable<UserOnlineStatus>? stream)> SubscribeToOnlineStatus(
            GlobalParam globalParam,
            List<long> userIds)
        {
            return await _webApi.TokenManager.SafeCallAsync(async () =>
            {
                try
                {
                    var request = new SubscribeToOnlineStatusRequest();
                    request.UserIds.AddRange(userIds);

                    var response = OnlinerAC!.SubscribeToOnlineStatus(request);

                    // Создаём IAsyncEnumerable для стрима
                    async IAsyncEnumerable<UserOnlineStatus> GetOnlineStatusStream()
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

                    return (new ErrorReturner(true, ""), GetOnlineStatusStream());
                }
                catch (RpcException)
                {
                    return (new ErrorReturner(false, "Ошибка аутентификации"), null);
                }
                catch (Exception)
                {
                    return (new ErrorReturner(false, "Ошибка подключения к Onliner"), null);
                }
            }, globalParam);
        }

        /// <summary>
        /// Установить статус "В сети" (пинг).
        /// </summary>
        public async Task<ErrorReturner> SetOnlineStatus(GlobalParam globalParam)
        {
            return await _webApi.TokenManager.SafeCallAsync(async () =>
            {
                try
                {
                    var request = new SetOnlineStatusRequest();
                    await OnlinerAC!.SetOnlineStatusAsync(request);

                    return new ErrorReturner(true, "");
                }
                catch (RpcException)
                {
                    return new ErrorReturner(false, "Ошибка аутентификации");
                }
                catch (Exception)
                {
                    return new ErrorReturner(false, "Ошибка подключения к Onliner");
                }
            }, globalParam);
        }

        /// <summary>
        /// Получить текущий статус онлайн указанных пользователей.
        /// </summary>
        public async Task<(ErrorReturner error, List<UserOnlineStatus>? statuses)> GetOnlineStatus(
            GlobalParam globalParam,
            List<long> userIds)
        {
            return await _webApi.TokenManager.SafeCallAsync(async () =>
            {
                try
                {
                    var request = new GetOnlineStatusRequest();
                    request.UserIds.AddRange(userIds);

                    var response = await OnlinerAC!.GetOnlineStatusAsync(request);

                    return (new ErrorReturner(true, ""), response.UsersStatuses.ToList());
                }
                catch (RpcException)
                {
                    return (new ErrorReturner(false, "Ошибка аутентификации"), null);
                }
                catch (Exception)
                {
                    return (new ErrorReturner(false, "Ошибка подключения к Onliner"), null);
                }
            }, globalParam);
        }

        /// <summary>
        /// Изменить список пользователей, на которых подписан клиент.
        /// </summary>
        public async Task<ErrorReturner> ChangeUsersInSubscription(
            GlobalParam globalParam,
            List<long> userIds)
        {
            return await _webApi.TokenManager.SafeCallAsync(async () =>
            {
                try
                {
                    var request = new ChangeUsersInSubscriptionRequest();
                    request.UserIds.AddRange(userIds);

                    await OnlinerAC!.ChangeUsersInSubscriptionAsync(request);

                    return new ErrorReturner(true, "");
                }
                catch (RpcException)
                {
                    return new ErrorReturner(false, "Ошибка аутентификации");
                }
                catch (Exception)
                {
                    return new ErrorReturner(false, "Ошибка подключения к Onliner");
                }
            }, globalParam);
        }
    }
}
