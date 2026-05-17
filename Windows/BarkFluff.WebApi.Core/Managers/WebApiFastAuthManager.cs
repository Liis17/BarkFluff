using BarkFluff.Proto.FastAuth;

using Grpc.Core;

namespace BarkFluff.WebApi.Core.Managers
{
    internal class WebApiFastAuthManager
    {
        private readonly WebApi _webApi;

        public WebApiFastAuthManager(WebApi webApi)
        {
            _webApi = webApi;
        }

        public async Task<(ErrorReturner, GenerateFastAuthTokenResponse?)> GenerateFastAuthToken(TokenFormat format)
        {
            if (_webApi.FastAuthAC == null)
                return (new ErrorReturner(false, "FastAuth клиент не инициализирован"), null);

            try
            {
                var response = await _webApi.FastAuthAC.GenerateFastAuthTokenAsync(
                    new GenerateFastAuthTokenRequest { Format = format });
                return (new ErrorReturner(true), response);
            }
            catch (Exception ex)
            {
                return (new ErrorReturner(false, ex.Message), null);
            }
        }

        public async Task<(ErrorReturner, IAsyncEnumerable<FastAuthResult>?)> SubscribeFastAuthResult(
            string fastAuthId, CancellationToken ct)
        {
            if (_webApi.FastAuthAC == null)
                return (new ErrorReturner(false, "FastAuth клиент не инициализирован"), null);

            try
            {
                // CT в сам streaming-call: без него Cancel() на CTS не закроет стрим — он
                // продолжит висеть на сокете до тайм-аута сервера.
                var call = _webApi.FastAuthAC.SubscribeFastAuthResult(
                    new SubscribeFastAuthResultRequest { FastAuthId = fastAuthId },
                    headers: null, deadline: null, cancellationToken: ct);

                async IAsyncEnumerable<FastAuthResult> GetStream()
                {
                    while (true)
                    {
                        bool hasNext;
                        FastAuthResult item;
                        try
                        {
                            hasNext = await call.ResponseStream.MoveNext(ct);
                            if (!hasNext) yield break;
                            item = call.ResponseStream.Current;
                        }
                        catch (RpcException) { yield break; }
                        catch (OperationCanceledException) { yield break; }
                        catch (Exception) { yield break; }

                        yield return item;
                    }
                }

                return (new ErrorReturner(true), GetStream());
            }
            catch (Exception ex)
            {
                return (new ErrorReturner(false, ex.Message), null);
            }
        }
    }
}
