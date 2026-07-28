using BarkFluff.Proto.FastAuth;
using BarkFluff.WebApi.Core.MessengerData;

namespace BarkFluff.WebApi.Core.Managers
{
    internal class WebApiFastAuthManager : WebApiBase
    {
        private readonly WebApi _webApi;

        public WebApiFastAuthManager(WebApi webApi) : base(webApi)
        {
            _webApi = webApi;
        }

        public async Task<(ErrorReturner, GenerateFastAuthTokenResponse?)> GenerateFastAuthToken(TokenFormat format)
        {
            if (FastAuthAC == null)
                return (new ErrorReturner(false, "FastAuth клиент не инициализирован"), null);

            try
            {
                var response = await FastAuthAC.GenerateFastAuthTokenAsync(
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
            if (FastAuthAC == null)
                return (new ErrorReturner(false, "FastAuth клиент не инициализирован"), null);

            try
            {
                // CT в сам streaming-call: без него Cancel() на CTS не закроет стрим — он
                // продолжит висеть на сокете до тайм-аута сервера.
                var call = FastAuthAC.SubscribeFastAuthResult(
                    new SubscribeFastAuthResultRequest { FastAuthId = fastAuthId },
                    headers: null, deadline: null, cancellationToken: ct);

                return (new ErrorReturner(true), ReadStream(call, ct));
            }
            catch (Exception ex)
            {
                return (new ErrorReturner(false, ex.Message), null);
            }
        }

        /// <summary>
        /// Шаг 3 (сторона уже авторизованного устройства): распознать QR-код входа.
        /// Возвращает данные запрашивающего устройства и одноразовый confirmation_code,
        /// который нужно передать в <see cref="AcceptFastAuth"/> или <see cref="RejectFastAuth"/>.
        /// Работает через авторизованный канал — требуется User-токен.
        /// </summary>
        public async Task<(ErrorReturner error, ScanFastAuthResponse? info)> ScanFastAuth(string fastAuthId, GlobalParam globalParam)
        {
            if (FastAuthUserAC == null)
                return (new ErrorReturner(false, "FastAuth клиент не инициализирован"), null);

            try
            {
                return await _webApi.TokenManager.SafeCallAsync(async () =>
                {
                    var response = await FastAuthUserAC!.ScanFastAuthAsync(new ScanFastAuthRequest { FastAuthId = fastAuthId });
                    return ((ErrorReturner, ScanFastAuthResponse?))(new ErrorReturner(true), response);
                }, globalParam);
            }
            catch (BarkFluff.Shared.Exceptions.FastAuth.FastAuthSessionNotFoundException)
            {
                return (new ErrorReturner(false, "Сессия входа не найдена"), null);
            }
            catch (BarkFluff.Shared.Exceptions.FastAuth.FastAuthSessionExpiredException)
            {
                return (new ErrorReturner(false, "Срок действия QR-кода истёк"), null);
            }
            catch (BarkFluff.Shared.Exceptions.FastAuth.FastAuthInvalidStateException)
            {
                return (new ErrorReturner(false, "Эта сессия входа уже обработана"), null);
            }
            catch (Exception)
            {
                return (new ErrorReturner(false, "Ошибка распознавания кода входа"), null);
            }
        }

        /// <summary>
        /// Шаг 4a: подтвердить вход нового устройства.
        /// </summary>
        public async Task<ErrorReturner> AcceptFastAuth(string fastAuthId, string confirmationCode, GlobalParam globalParam)
            => await ResolveFastAuth(fastAuthId, confirmationCode, globalParam, accept: true);

        /// <summary>
        /// Шаг 4b: отклонить вход нового устройства.
        /// </summary>
        public async Task<ErrorReturner> RejectFastAuth(string fastAuthId, string confirmationCode, GlobalParam globalParam)
            => await ResolveFastAuth(fastAuthId, confirmationCode, globalParam, accept: false);

        private async Task<ErrorReturner> ResolveFastAuth(string fastAuthId, string confirmationCode, GlobalParam globalParam, bool accept)
        {
            if (FastAuthUserAC == null)
                return new ErrorReturner(false, "FastAuth клиент не инициализирован");

            try
            {
                return await _webApi.TokenManager.SafeCallAsync(async () =>
                {
                    if (accept)
                    {
                        await FastAuthUserAC!.AcceptFastAuthAsync(new AcceptFastAuthRequest
                        {
                            FastAuthId = fastAuthId,
                            ConfirmationCode = confirmationCode
                        });
                    }
                    else
                    {
                        await FastAuthUserAC!.RejectFastAuthAsync(new RejectFastAuthRequest
                        {
                            FastAuthId = fastAuthId,
                            ConfirmationCode = confirmationCode
                        });
                    }
                    return new ErrorReturner(true);
                }, globalParam);
            }
            catch (BarkFluff.Shared.Exceptions.FastAuth.FastAuthInvalidConfirmationCodeException)
            {
                return new ErrorReturner(false, "Неверный код подтверждения");
            }
            catch (BarkFluff.Shared.Exceptions.FastAuth.FastAuthSessionNotFoundException)
            {
                return new ErrorReturner(false, "Сессия входа не найдена");
            }
            catch (BarkFluff.Shared.Exceptions.FastAuth.FastAuthSessionExpiredException)
            {
                return new ErrorReturner(false, "Срок действия QR-кода истёк");
            }
            catch (BarkFluff.Shared.Exceptions.FastAuth.FastAuthInvalidStateException)
            {
                return new ErrorReturner(false, "Эта сессия входа уже обработана");
            }
            catch (Exception)
            {
                return new ErrorReturner(false, accept ? "Ошибка подтверждения входа" : "Ошибка отклонения входа");
            }
        }
    }
}
