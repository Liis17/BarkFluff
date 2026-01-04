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

        public async Task<(ErrorReturner error, IAsyncEnumerable<NewMessageEvent>? stream)> JustUpdate(GlobalParam globalParam)
        {
            return await _webApi.TokenManager.SafeCallAsync(async () =>
            {
                try
                {
                    // Подготовка заголовков с токеном (предполагается, что токен в globalParam)
                    var headers = new Metadata();
                    if (!string.IsNullOrEmpty(globalParam.AccessToken?.Value))
                    {
                        headers.Add("Authorization", $"Bearer {globalParam.AccessToken.Value}");
                    }

                    // Вызов метода подписки с заголовками
                    var response = UpdatesAC!.SubscribeNewMessages(new SubscribeNewMessagesRequest { }, headers);

                    // Создаём IAsyncEnumerable для стрима
                    async IAsyncEnumerable<NewMessageEvent> GetMessageStream()
                    {
                        while (true)
                        {
                            bool hasNext;
                            NewMessageEvent messageEvent;

                            try
                            {
                                hasNext = await response.ResponseStream.MoveNext(CancellationToken.None);
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
                            catch (Exception)
                            {
                                yield break;
                            }

                            yield return messageEvent;
                        }
                    }

                    return (new ErrorReturner(true, ""), GetMessageStream());
                }
                catch (RpcException)
                {
                    return (new ErrorReturner(false, "Ошибка аутентификации"), null);
                }
                catch (Exception)
                {
                    return (new ErrorReturner(false, "Ошибка подключения к обновлениям"), null);
                }
            }, globalParam);
        }

        public async Task<(ErrorReturner error, IAsyncEnumerable<MessageReadEvent>? stream)> SubscribeToReadReceipts(
            GlobalParam globalParam)
        {
            return await _webApi.TokenManager.SafeCallAsync(async () =>
            {
                try
                {
                    // Подготовка заголовков с токеном
                    var headers = new Metadata();
                    if (!string.IsNullOrEmpty(globalParam.AccessToken?.Value))
                    {
                        headers.Add("Authorization", $"Bearer {globalParam.AccessToken.Value}");
                    }

                    var request = new SubscribeMessagesReadRequest();

                    // Вызов метода подписки с заголовками
                    var response = UpdatesAC!.SubscribeMessagesRead(request, headers);

                    // Создаём IAsyncEnumerable для стрима
                    async IAsyncEnumerable<MessageReadEvent> GetReadReceiptStream()
                    {
                        while (true)
                        {
                            bool hasNext;
                            MessageReadEvent update;

                            try
                            {
                                hasNext = await response.ResponseStream.MoveNext(CancellationToken.None);
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
                            catch (Exception)
                            {
                                yield break;
                            }

                            yield return update;
                        }
                    }

                    return (new ErrorReturner(true, ""), GetReadReceiptStream());
                }
                catch (RpcException)
                {
                    return (new ErrorReturner(false, "Ошибка аутентификации"), null);
                }
                catch (Exception)
                {
                    return (new ErrorReturner(false, "Ошибка подключения к обновлениям"), null);
                }
            }, globalParam);
        }
    }
}
