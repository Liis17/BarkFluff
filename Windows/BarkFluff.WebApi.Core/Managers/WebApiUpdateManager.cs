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

                    // Создаём IAsyncEnumerable для стрима
                    async IAsyncEnumerable<NewMessageEvent> GetMessageStream()
                    {
                        while (true)
                        {
                            bool hasNext;
                            NewMessageEvent messageEvent;

                            try
                            {
                                hasNext = await response.ResponseStream.MoveNext(ct);
                                if (!hasNext)
                                {
                                    yield break; // Стрим завершён
                                }
                                messageEvent = response.ResponseStream.Current;
                            }
                            catch (RpcException ex)
                            {
                                var a = ex;
                                yield break;
                            }
                            catch (OperationCanceledException)
                            {
                                yield break;
                            }
                            catch (Exception)
                            {
                                yield break;
                            }

                            yield return messageEvent;
                        }
                    }

                    return ((ErrorReturner, IAsyncEnumerable<NewMessageEvent>?))(new ErrorReturner(true, ""), GetMessageStream());
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

                    // Создаём IAsyncEnumerable для стрима
                    async IAsyncEnumerable<MessageReadEvent> GetReadReceiptStream()
                    {
                        while (true)
                        {
                            bool hasNext;
                            MessageReadEvent update;

                            try
                            {
                                hasNext = await response.ResponseStream.MoveNext(ct);
                                if (!hasNext)
                                {
                                    yield break; // Стрим завершён
                                }
                                update = response.ResponseStream.Current;
                            }
                            catch (RpcException)
                            {
                                yield break;
                            }
                            catch (OperationCanceledException)
                            {
                                yield break;
                            }
                            catch (Exception)
                            {
                                yield break;
                            }

                            yield return update;
                        }
                    }

                    return ((ErrorReturner, IAsyncEnumerable<MessageReadEvent>?))(new ErrorReturner(true, ""), GetReadReceiptStream());
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
