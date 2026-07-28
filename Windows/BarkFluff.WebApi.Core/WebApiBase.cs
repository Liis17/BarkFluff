using Grpc.Core;
using Grpc.Net.Client;

using System.Runtime.CompilerServices;

namespace BarkFluff.WebApi.Core
{
    /// <summary>
    /// Базовый класс для всех менеджеров WebApi. Pass-through-доступ к gRPC-клиентам
    /// и каналам, хранящимся в единственном источнике истины — <see cref="WebApi"/>.
    /// За счёт этого пересоздание клиентов в <see cref="Managers.WebApiClientManager"/>
    /// автоматически видно всем менеджерам — синхронизировать вручную не требуется.
    /// </summary>
    internal abstract class WebApiBase
    {
        private readonly WebApi _webApi;

        protected BarkFluff.Proto.Users.UsersApi.UsersApiClient? UsersAC => _webApi.UsersAC;
        protected BarkFluff.Proto.Beacon.BeaconApi.BeaconApiClient? BeaconAC => _webApi.BeaconAC;
        protected BarkFluff.Proto.Identity.IdentityApi.IdentityApiClient? IdentityAC => _webApi.IdentityAC;
        protected BarkFluff.Proto.Files.FilesApi.FilesApiClient? FilesAC => _webApi.FilesAC;
        protected BarkFluff.Proto.Messages.MessagesApi.MessagesApiClient? MessagesAC => _webApi.MessagesAC;
        protected BarkFluff.Proto.Navigator.NavigatorApi.NavigatorApiClient? NavigatorAC => _webApi.NavigatorAC;
        protected BarkFluff.Proto.Updates.UpdatesApi.UpdatesApiClient? UpdatesAC => _webApi.UpdatesAC;
        protected BarkFluff.Proto.Onliner.OnlinerApi.OnlinerApiClient? OnlinerAC => _webApi.OnlinerAC;
        protected BarkFluff.Proto.FastAuth.FastAuthApi.FastAuthApiClient? FastAuthAC => _webApi.FastAuthAC;
        protected BarkFluff.Proto.FastAuth.FastAuthApi.FastAuthApiClient? FastAuthUserAC => _webApi.FastAuthUserAC;
        protected BarkFluff.Proto.Calls.CallsApi.CallsApiClient? CallsAC => _webApi.CallsAC;

        protected GrpcChannel? BeaconChannel => _webApi.BeaconChannel;
        protected GrpcChannel? UserChannel => _webApi.UserChannel;
        protected GrpcChannel? IdentityChannel => _webApi.IdentityChannel;
        protected GrpcChannel? FilesChannel => _webApi.FilesChannel;
        protected GrpcChannel? MessagesChannel => _webApi.MessagesChannel;
        protected GrpcChannel? NavigatorChannel => _webApi.NavigatorChannel;
        protected GrpcChannel? UpdatesChannel => _webApi.UpdatesChannel;
        protected GrpcChannel? OnlinerChannel => _webApi.OnlinerChannel;
        protected GrpcChannel? FastAuthChannel => _webApi.FastAuthChannel;
        protected GrpcChannel? FastAuthUserChannel => _webApi.FastAuthUserChannel;
        protected GrpcChannel? CallsChannel => _webApi.CallsChannel;

        protected WebApiBase(WebApi webApi)
        {
            _webApi = webApi;
        }

        /// <summary>
        /// Превращает серверный gRPC-стрим в <see cref="IAsyncEnumerable{T}"/>.
        /// Любой обрыв (RpcException, отмена, сетевая ошибка) завершает перечисление —
        /// клиент решает, пересоздавать ли подписку (например, после события TokenRefreshed).
        /// Сам вызов освобождается по завершении перечисления, в том числе когда
        /// потребитель вышел из цикла досрочно — иначе стрим остаётся висеть на сокете.
        /// </summary>
        protected static async IAsyncEnumerable<T> ReadStream<T>(
            AsyncServerStreamingCall<T> call,
            [EnumeratorCancellation] CancellationToken ct)
        {
            using (call)
            {
                while (true)
                {
                    T item;

                    try
                    {
                        if (!await call.ResponseStream.MoveNext(ct))
                        {
                            yield break; // Стрим завершён сервером
                        }
                        item = call.ResponseStream.Current;
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

                    yield return item;
                }
            }
        }
    }
}
