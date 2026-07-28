using BarkFluff.Proto.Calls;
using BarkFluff.WebApi.Core.MessengerData;

using Google.Protobuf.WellKnownTypes;

using Grpc.Core;

namespace BarkFluff.WebApi.Core.Managers
{
    /// <summary>
    /// Менеджер звонков: call-control через gRPC-сервис Calls.
    /// </summary>
    /// <remarks>
    /// Библиотека закрывает только сигнализацию — создание/приём/завершение звонка,
    /// события и историю. Медиа идёт мимо неё: сервер выдаёт <c>livekit_url</c> и
    /// <c>access_token</c>, а подключение к LiveKit SFU остаётся за приложением.
    /// </remarks>
    internal class WebApiCallsManager : WebApiBase
    {
        private const string CallsUnavailable = "Звонки недоступны на этом сервере";
        private const int MaxHistoryLimit = 50;

        private readonly WebApi _webApi;

        public WebApiCallsManager(WebApi webApi) : base(webApi)
        {
            _webApi = webApi;
        }

        /// <summary>
        /// Начать личный звонок пользователю.
        /// </summary>
        public async Task<(ErrorReturner error, InitiateCallResponse? call)> InitiateCallToUser(
            long calleeUserId, CallMediaType mediaType, GlobalParam globalParam)
            => await InitiateCall(new InitiateCallRequest { CalleeUserId = calleeUserId, MediaType = mediaType }, globalParam);

        /// <summary>
        /// Начать групповой звонок в чате.
        /// </summary>
        public async Task<(ErrorReturner error, InitiateCallResponse? call)> InitiateCallInChat(
            string chatId, CallMediaType mediaType, GlobalParam globalParam)
            => await InitiateCall(new InitiateCallRequest { ChatId = chatId, MediaType = mediaType }, globalParam);

        private async Task<(ErrorReturner error, InitiateCallResponse? call)> InitiateCall(
            InitiateCallRequest request, GlobalParam globalParam)
        {
            if (CallsAC == null)
                return (new ErrorReturner(false, CallsUnavailable), null);

            try
            {
                return await _webApi.TokenManager.SafeCallAsync(async () =>
                {
                    var response = await CallsAC!.InitiateCallAsync(request);
                    return ((ErrorReturner, InitiateCallResponse?))(new ErrorReturner(true), response);
                }, globalParam);
            }
            catch (BarkFluff.Shared.Exceptions.Messages.NoAccessToChatException)
            {
                return (new ErrorReturner(false, "Нет доступа к чату"), null);
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.FailedPrecondition)
            {
                return (new ErrorReturner(false, "Звонок сейчас невозможен"), null);
            }
            catch (Exception)
            {
                return (new ErrorReturner(false, "Ошибка начала звонка"), null);
            }
        }

        /// <summary>
        /// Присоединиться к идущему звонку (групповой late-join или второе устройство).
        /// </summary>
        public async Task<(ErrorReturner error, JoinCallResponse? call)> JoinCall(string callId, GlobalParam globalParam)
        {
            if (CallsAC == null)
                return (new ErrorReturner(false, CallsUnavailable), null);

            try
            {
                return await _webApi.TokenManager.SafeCallAsync(async () =>
                {
                    var response = await CallsAC!.JoinCallAsync(new JoinCallRequest { CallId = callId });
                    return ((ErrorReturner, JoinCallResponse?))(new ErrorReturner(true), response);
                }, globalParam);
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
            {
                return (new ErrorReturner(false, "Звонок уже завершён"), null);
            }
            catch (Exception)
            {
                return (new ErrorReturner(false, "Ошибка присоединения к звонку"), null);
            }
        }

        /// <summary>
        /// Принять входящий звонок. Ринг на остальных устройствах гасится сервером.
        /// </summary>
        public async Task<(ErrorReturner error, AcceptCallResponse? call)> AcceptCall(string callId, GlobalParam globalParam)
        {
            if (CallsAC == null)
                return (new ErrorReturner(false, CallsUnavailable), null);

            try
            {
                return await _webApi.TokenManager.SafeCallAsync(async () =>
                {
                    var response = await CallsAC!.AcceptCallAsync(new AcceptCallRequest { CallId = callId });
                    return ((ErrorReturner, AcceptCallResponse?))(new ErrorReturner(true), response);
                }, globalParam);
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
            {
                return (new ErrorReturner(false, "Звонок уже завершён"), null);
            }
            catch (Exception)
            {
                return (new ErrorReturner(false, "Ошибка приёма звонка"), null);
            }
        }

        /// <summary>
        /// Отклонить входящий звонок.
        /// </summary>
        public async Task<ErrorReturner> RejectCall(string callId, GlobalParam globalParam)
            => await SimpleCall(callId, globalParam, reject: true);

        /// <summary>
        /// Завершить звонок.
        /// </summary>
        public async Task<ErrorReturner> EndCall(string callId, GlobalParam globalParam)
            => await SimpleCall(callId, globalParam, reject: false);

        private async Task<ErrorReturner> SimpleCall(string callId, GlobalParam globalParam, bool reject)
        {
            if (CallsAC == null)
                return new ErrorReturner(false, CallsUnavailable);

            try
            {
                return await _webApi.TokenManager.SafeCallAsync(async () =>
                {
                    if (reject)
                        await CallsAC!.RejectCallAsync(new RejectCallRequest { CallId = callId });
                    else
                        await CallsAC!.EndCallAsync(new EndCallRequest { CallId = callId });

                    return new ErrorReturner(true);
                }, globalParam);
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
            {
                // Звонок уже завершился сам (таймаут, вторая сторона положила трубку) —
                // для клиента результат тот же, что и при успешном вызове.
                return new ErrorReturner(true);
            }
            catch (Exception)
            {
                return new ErrorReturner(false, reject ? "Ошибка отклонения звонка" : "Ошибка завершения звонка");
            }
        }

        /// <summary>
        /// Сменить общее качество голоса звонка — применяется ко всем участникам.
        /// </summary>
        public async Task<ErrorReturner> SetCallAudioQuality(string callId, CallAudioQuality quality, GlobalParam globalParam)
        {
            if (CallsAC == null)
                return new ErrorReturner(false, CallsUnavailable);

            try
            {
                return await _webApi.TokenManager.SafeCallAsync(async () =>
                {
                    await CallsAC!.SetCallAudioQualityAsync(new SetCallAudioQualityRequest
                    {
                        CallId = callId,
                        Quality = quality
                    });
                    return new ErrorReturner(true);
                }, globalParam);
            }
            catch (Exception)
            {
                return new ErrorReturner(false, "Ошибка смены качества голоса");
            }
        }

        /// <summary>
        /// Подписка на события звонков текущего устройства: входящие, приём, отклонение,
        /// завершение, вход/выход участников, смена качества голоса.
        /// </summary>
        /// <remarks>
        /// Стрим device-scope: сервер различает устройства по device-id из JWT, поэтому
        /// подписка обязана идти по тому же каналу, что и остальные вызовы.
        /// Как и прочие стримы, пересоздаётся после события TokenRefreshed.
        /// </remarks>
        public async Task<(ErrorReturner error, IAsyncEnumerable<CallEvent>? stream)> SubscribeCallEvents(
            GlobalParam globalParam, CancellationToken ct = default)
        {
            if (CallsAC == null)
                return (new ErrorReturner(false, CallsUnavailable), null);

            try
            {
                return await _webApi.TokenManager.SafeCallAsync(async () =>
                {
                    var call = CallsAC!.SubscribeCallEvents(new SubscribeCallEventsRequest(),
                        headers: null, deadline: null, cancellationToken: ct);

                    return ((ErrorReturner, IAsyncEnumerable<CallEvent>?))(new ErrorReturner(true, ""), ReadStream(call, ct));
                }, globalParam);
            }
            catch (RpcException)
            {
                return (new ErrorReturner(false, "Ошибка аутентификации"), null);
            }
            catch (Exception)
            {
                return (new ErrorReturner(false, "Ошибка подключения к событиям звонков"), null);
            }
        }

        /// <summary>
        /// История звонков. Курсор <paramref name="beforeStartedAt"/> — вернуть записи
        /// старше указанного момента (null = с начала).
        /// </summary>
        public async Task<(ErrorReturner error, List<CallHistoryItem>? items, bool hasMore)> ListCallHistory(
            GlobalParam globalParam,
            CallHistoryFilter filter = CallHistoryFilter.CallHistoryAll,
            int limit = MaxHistoryLimit,
            DateTime? beforeStartedAt = null)
        {
            if (CallsAC == null)
                return (new ErrorReturner(false, CallsUnavailable), null, false);

            try
            {
                return await _webApi.TokenManager.SafeCallAsync(async () =>
                {
                    var request = new ListCallHistoryRequest
                    {
                        Filter = filter,
                        Limit = Math.Clamp(limit, 1, MaxHistoryLimit)
                    };
                    if (beforeStartedAt.HasValue)
                        request.BeforeStartedAt = Timestamp.FromDateTime(beforeStartedAt.Value.ToUniversalTime());

                    var response = await CallsAC!.ListCallHistoryAsync(request);
                    return (new ErrorReturner(true), response.Items.ToList(), response.HasMore);
                }, globalParam);
            }
            catch (Exception)
            {
                return (new ErrorReturner(false, "Ошибка получения истории звонков"), null, false);
            }
        }

        /// <summary>
        /// Активные звонки в указанных чатах — для баннера «присоединиться».
        /// </summary>
        public async Task<(ErrorReturner error, List<ActiveCallItem>? calls)> GetActiveCalls(
            List<string> chatIds, GlobalParam globalParam)
        {
            if (CallsAC == null)
                return (new ErrorReturner(false, CallsUnavailable), null);

            try
            {
                return await _webApi.TokenManager.SafeCallAsync(async () =>
                {
                    var request = new GetActiveCallsRequest();
                    request.ChatIds.AddRange(chatIds);

                    var response = await CallsAC!.GetActiveCallsAsync(request);
                    return (new ErrorReturner(true), response.Calls.ToList());
                }, globalParam);
            }
            catch (Exception)
            {
                return (new ErrorReturner(false, "Ошибка получения активных звонков"), null);
            }
        }
    }
}
