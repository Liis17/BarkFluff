using BarkFluff.Proto.Updates;
using BarkFluff.WebApi.Core.MessengerData;

using Grpc.Core;

namespace BarkFluff.WebApi.Core.Managers
{
    /// <summary>
    /// Менеджер для реалтайм обновлений.
    /// </summary>
    internal class WebApiUpdateManager : WebApiBase
    {
        private readonly WebApi _webApi;

        public WebApiUpdateManager(WebApi webApi) : base(webApi)
        {
            _webApi = webApi;
        }

        /// <summary>
        /// Общая обвязка подписки: все стримы Updates отличаются только вызовом,
        /// а обработка ошибок и превращение в <see cref="IAsyncEnumerable{T}"/> у них одинаковые.
        /// </summary>
        /// <remarks>
        /// CT прокидывается и в сам streaming-call, и в MoveNext (внутри ReadStream): без этого стрим
        /// невозможно отменить со стороны клиента — он висит на сокете до тайм-аута/обрыва сервером
        /// и держит соединение (утечка + race со свежими стримами после TokenRefreshed).
        /// </remarks>
        private async Task<(ErrorReturner error, IAsyncEnumerable<TEvent>? stream)> Subscribe<TEvent>(
            Func<CancellationToken, AsyncServerStreamingCall<TEvent>> startCall,
            GlobalParam globalParam,
            CancellationToken ct)
        {
            try
            {
                return await _webApi.TokenManager.SafeCallAsync(async () =>
                {
                    var call = startCall(ct);
                    return ((ErrorReturner, IAsyncEnumerable<TEvent>?))(new ErrorReturner(true, ""), ReadStream(call, ct));
                }, globalParam);
            }
            catch (RpcException)
            {
                return (new ErrorReturner(false, "Ошибка аутентификации"), null);
            }
            catch (Exception)
            {
                return (new ErrorReturner(false, "Ошибка подключения к обновлениям"), null);
            }
        }

        /// <summary>
        /// Новые сообщения во всех чатах пользователя.
        /// </summary>
        public async Task<(ErrorReturner error, IAsyncEnumerable<NewMessageEvent>? stream)> JustUpdate(GlobalParam globalParam, CancellationToken ct = default)
            => await Subscribe(token => UpdatesAC!.SubscribeNewMessages(new SubscribeNewMessagesRequest(), headers: null, deadline: null, cancellationToken: token), globalParam, ct);

        /// <summary>
        /// Отметки о прочтении сообщений.
        /// </summary>
        public async Task<(ErrorReturner error, IAsyncEnumerable<MessageReadEvent>? stream)> SubscribeToReadReceipts(GlobalParam globalParam, CancellationToken ct = default)
            => await Subscribe(token => UpdatesAC!.SubscribeMessagesRead(new SubscribeMessagesReadRequest(), headers: null, deadline: null, cancellationToken: token), globalParam, ct);

        /// <summary>
        /// Отредактированные сообщения.
        /// </summary>
        public async Task<(ErrorReturner error, IAsyncEnumerable<MessageEditedEvent>? stream)> SubscribeToMessagesEdited(GlobalParam globalParam, CancellationToken ct = default)
            => await Subscribe(token => UpdatesAC!.SubscribeMessagesEdited(new SubscribeMessagesEditedRequest(), headers: null, deadline: null, cancellationToken: token), globalParam, ct);

        /// <summary>
        /// Удалённые сообщения.
        /// </summary>
        public async Task<(ErrorReturner error, IAsyncEnumerable<MessageDeletedEvent>? stream)> SubscribeToMessagesDeleted(GlobalParam globalParam, CancellationToken ct = default)
            => await Subscribe(token => UpdatesAC!.SubscribeMessagesDeleted(new SubscribeMessagesDeletedRequest(), headers: null, deadline: null, cancellationToken: token), globalParam, ct);

        /// <summary>
        /// Закрепления сообщений.
        /// </summary>
        public async Task<(ErrorReturner error, IAsyncEnumerable<MessagePinnedEvent>? stream)> SubscribeToMessagesPinned(GlobalParam globalParam, CancellationToken ct = default)
            => await Subscribe(token => UpdatesAC!.SubscribeMessagesPinned(new SubscribeMessagesPinnedRequest(), headers: null, deadline: null, cancellationToken: token), globalParam, ct);

        /// <summary>
        /// Открепления отдельных сообщений.
        /// </summary>
        public async Task<(ErrorReturner error, IAsyncEnumerable<MessageUnpinnedEvent>? stream)> SubscribeToMessagesUnpinned(GlobalParam globalParam, CancellationToken ct = default)
            => await Subscribe(token => UpdatesAC!.SubscribeMessagesUnpinned(new SubscribeMessagesUnpinnedRequest(), headers: null, deadline: null, cancellationToken: token), globalParam, ct);

        /// <summary>
        /// Массовое открепление всех сообщений чата.
        /// </summary>
        public async Task<(ErrorReturner error, IAsyncEnumerable<AllMessagesUnpinnedEvent>? stream)> SubscribeToAllMessagesUnpinned(GlobalParam globalParam, CancellationToken ct = default)
            => await Subscribe(token => UpdatesAC!.SubscribeAllMessagesUnpinned(new SubscribeAllMessagesUnpinnedRequest(), headers: null, deadline: null, cancellationToken: token), globalParam, ct);
    }
}
