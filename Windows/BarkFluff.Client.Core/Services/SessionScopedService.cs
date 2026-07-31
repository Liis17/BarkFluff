using BarkFluff.WebApi.Core.MessengerData;

using WebApiClient = BarkFluff.WebApi.Core.WebApi;

namespace BarkFluff.Client.Core.Services;

/// <summary>
/// База сервисов настроек: каждый вызов фасада требует <see cref="GlobalParam"/> текущей сессии.
/// </summary>
/// <remarks>
/// <see cref="MessengerService"/> сознательно оставлен со своей копией этой логики: он работал
/// до появления настроек, и переносить его на общую базу здесь незачем.
/// </remarks>
public abstract class SessionScopedService
{
    private readonly IClientSession _session;

    protected SessionScopedService(WebApiClient webApi, IClientSession session)
    {
        WebApi = webApi;
        _session = session;
    }

    protected WebApiClient WebApi { get; }

    protected GlobalParam Parameters =>
        _session.CurrentConnection?.ConnectionParameters
        ?? throw new InvalidOperationException("The node session is unavailable.");
}
