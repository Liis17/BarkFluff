namespace BarkFluff.Federation.Services;

// Единый выключатель федерации (P1-04). Применяется ко всем входящим (XFed-интерсептор),
// исходящим (outbox-диспетчер) и фоновым (peer-refresh) путям, а также к публикации well-known.
// Internal status API (GetFederationStatus) остаётся доступным оператору независимо от состояния.
//
// - Enabled     — операторский переключатель (Federation:Enabled), «нода-одиночка» при false.
// - Configured  — задан Federation:ServerName (глобальное имя ноды); без него S2S невозможен.
// - IsActive    — оба true: нода принимает/шлёт S2S, публикует well-known, ведёт сетевой refresh.
public class FederationSwitch
{
    private readonly IConfiguration _configuration;

    public FederationSwitch(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public bool IsEnabled
        => string.Equals(_configuration["Federation:Enabled"], "true", StringComparison.OrdinalIgnoreCase);

    public bool IsConfigured
        => !string.IsNullOrWhiteSpace(_configuration["Federation:ServerName"]);

    public bool IsActive => IsEnabled && IsConfigured;
}
