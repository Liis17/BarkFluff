using BarkFluff.WebApi.Core.MessengerData;
using BarkFluff.WebApi.Core.MessengerData.NonSavedData;

namespace BarkFluff.WebApi.Core.Managers
{
    /// <summary>
    /// Менеджер для работы с серверами.
    /// </summary>
    internal class WebApiServerManager : WebApiBase
    {
        private readonly WebApi _webApi;

        public WebApiServerManager(WebApi webApi) : base(webApi)
        {
            _webApi = webApi;
        }

        /// <summary>
        /// Получает информацию о сервере
        /// </summary>
        public async Task<(ErrorReturner error, Proto.Beacon.GetServerInfoResponse?)> GetServerInfo(GlobalParam param)
        {
            if (BeaconAC == null)
            {
                var createResult = _webApi.ClientManager.CreateOnlyBeaconAC(param);
                if (!createResult.IsSuccess || BeaconAC == null)
                    return (createResult.IsSuccess ? new ErrorReturner(false, "Beacon клиент не инициализирован") : createResult, null);
            }
            return await _webApi.TokenManager.SafeCallAsync(async () =>
            {
                try
                {
                    var response = await BeaconAC!.GetServerInfoAsync(new BarkFluff.Proto.Beacon.GetServerInfoRequest());
                    return (new ErrorReturner(true), response);
                }
                catch (Grpc.Core.RpcException rpcEx)
                {
                    return (new ErrorReturner(false, $"Ошибка получения информации о сервере: {rpcEx.Status.StatusCode} {rpcEx.Status.Detail}"), null);
                }
                catch (Exception ex)
                {
                    return (new ErrorReturner(false, $"Ошибка получения информации о сервере: {ex.Message}"), null);
                }
            }, param);
        }

        /// <summary>
        /// Получает список серверов, доступных для подключения.
        /// </summary>
        public async Task<(ErrorReturner, List<ServerDataElement> ServerElements)> GetServerList(GlobalParam global)
        {
            try
            {
                return await _webApi.TokenManager.SafeCallAsync(async () =>
                {
                    var response = await NavigatorAC!.ListServersAsync(new Proto.Navigator.ListServersRequest { });
                    var serverList = response.Servers
                        .Select(item => new ServerDataElement
                        {
                            Ip = $"{item.BeaconUri.Host}:{item.BeaconUri.Port}",
                            Title = item.Name,
                            UserCount = item.AccountsCount.ToString(),
                            Description = item.Description,
                            PublicName = item.ServerPublicName,
                            Location = item.Location,
                            HexColor = item.Color?.MainHex ?? string.Empty
                        })
                        .ToList();

                    return (new ErrorReturner(true), serverList);
                }, global);
            }
            catch (Exception)
            {
                return (new ErrorReturner(false, "Ошибка получения списка серверов"), new List<ServerDataElement>());
            }
        }
    }
}
