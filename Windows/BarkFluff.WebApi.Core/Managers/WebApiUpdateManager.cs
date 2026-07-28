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

        public async Task<(ErrorReturner error, IAsyncEnumerable<NewMessageEvent>? stream)> JustUpdate(GlobalParam globalParam, CancellationToken ct = default)
        {
            try
            {
                return await _webApi.TokenManager.SafeCallAsync(async () =>
                {
                    // CT прокидываем и в сам streaming-call, и в MoveNext: без этого стрим
                    // невозможно отменить со стороны клиента — он висит на сокете до тайм-аута/обрыва
                    // сервером и держит соединение (утечка + race со свежими стримами после TokenRefreshed).
                    var response = UpdatesAC!.SubscribeNewMessages(new SubscribeNewMessagesRequest { }, headers: null, deadline: null, cancellationToken: ct);

                    return ((ErrorReturner, IAsyncEnumerable<NewMessageEvent>?))(new ErrorReturner(true, ""), ReadStream(response, ct));
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

        public async Task<(ErrorReturner error, IAsyncEnumerable<MessageReadEvent>? stream)> SubscribeToReadReceipts(
            GlobalParam globalParam, CancellationToken ct = default)
        {
            try
            {
                return await _webApi.TokenManager.SafeCallAsync(async () =>
                {
                    var request = new SubscribeMessagesReadRequest();
                    var response = UpdatesAC!.SubscribeMessagesRead(request, headers: null, deadline: null, cancellationToken: ct);

                    return ((ErrorReturner, IAsyncEnumerable<MessageReadEvent>?))(new ErrorReturner(true, ""), ReadStream(response, ct));
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
    }
}
