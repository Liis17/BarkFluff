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
                _webApi.ClientManager.CreateOnlyBeaconAC(param);
            }
            return await _webApi.TokenManager.SafeCallAsync(async () =>
            {
                try
                {
                    var response = await BeaconAC!.GetServerInfoAsync(new BarkFluff.Proto.Beacon.GetServerInfoRequest());
                    return (new ErrorReturner(true), response);
                }
                catch (Exception ex)
                {
                    return (new ErrorReturner(false, "Ошибка получения информации о сервере"), null);
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
                            PublicName = item.ServerPublicName
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
