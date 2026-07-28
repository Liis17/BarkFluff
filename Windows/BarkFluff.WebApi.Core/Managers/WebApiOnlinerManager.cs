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
            GlobalParam globalParam,
            CancellationToken ct = default)
        {
            try
            {
                return await _webApi.TokenManager.SafeCallAsync(async () =>
                {
                    var request = new SubscribeToOnlineStatusRequest();
                    request.UserIds.AddRange(userIds);

                    // CT прокидывается в сам streaming-call и в MoveNext, иначе стрим невозможно
                    // отменить — он висит на сокете до тайм-аута сервера, дублируя соединения
                    // при каждом TokenRefreshed/переподключении.
                    var response = OnlinerAC!.SubscribeToOnlineStatus(request, headers: null, deadline: null, cancellationToken: ct);

                    return ((ErrorReturner, IAsyncEnumerable<UserOnlineStatus>?))(new ErrorReturner(true, ""), ReadStream(response, ct));
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
        /// Сообщить серверу, что пользователь печатает в чате (или прекратил).
        /// </summary>
        public async Task<ErrorReturner> SetTypingStatus(string chatId, TypingAction action, GlobalParam globalParam)
        {
            try
            {
                return await _webApi.TokenManager.SafeCallAsync(async () =>
                {
                    await OnlinerAC!.SetTypingStatusAsync(new SetTypingStatusRequest
                    {
                        ChatId = chatId,
                        Action = action
                    });
                    return new ErrorReturner(true);
                }, globalParam);
            }
            catch (RpcException)
            {
                return new ErrorReturner(false, "Ошибка аутентификации при отправке индикатора набора");
            }
            catch (Exception)
            {
                return new ErrorReturner(false, "Ошибка отправки индикатора набора");
            }
        }

        /// <summary>
        /// Подписаться на индикаторы набора текста в выбранных чатах (streaming).
        /// </summary>
        public async Task<(ErrorReturner error, IAsyncEnumerable<TypingEvent>? stream)> SubscribeToTyping(
            List<string> chatIds,
            GlobalParam globalParam,
            CancellationToken ct = default)
        {
            try
            {
                return await _webApi.TokenManager.SafeCallAsync(async () =>
                {
                    var request = new SubscribeToTypingRequest();
                    request.ChatIds.AddRange(chatIds);

                    var response = OnlinerAC!.SubscribeToTyping(request, headers: null, deadline: null, cancellationToken: ct);

                    return ((ErrorReturner, IAsyncEnumerable<TypingEvent>?))(new ErrorReturner(true, ""), ReadStream(response, ct));
                }, globalParam);
            }
            catch (RpcException)
            {
                return (new ErrorReturner(false, "Ошибка аутентификации"), null);
            }
            catch (Exception)
            {
                return (new ErrorReturner(false, "Ошибка подключения к индикаторам набора"), null);
            }
        }

        /// <summary>
        /// Изменить список чатов в существующей подписке на индикаторы набора.
        /// </summary>
        public async Task<ErrorReturner> ChangeChatsInTypingSubscription(List<string> chatIds, GlobalParam globalParam)
        {
            try
            {
                return await _webApi.TokenManager.SafeCallAsync(async () =>
                {
                    var request = new ChangeChatsInTypingSubscriptionRequest();
                    request.ChatIds.AddRange(chatIds);

                    await OnlinerAC!.ChangeChatsInTypingSubscriptionAsync(request);
                    return new ErrorReturner(true);
                }, globalParam);
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.FailedPrecondition)
            {
                return new ErrorReturner(false, "Нет активной подписки на индикаторы набора");
            }
            catch (RpcException)
            {
                return new ErrorReturner(false, "Ошибка аутентификации при изменении списка чатов");
            }
            catch (Exception)
            {
                return new ErrorReturner(false, "Ошибка изменения списка чатов подписки");
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
