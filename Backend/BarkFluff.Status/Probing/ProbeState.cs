namespace BarkFluff.Status.Probing;

/// <summary>
/// Результат одной проверки. Up/Degraded — доступен (degraded помечен отдельно), Down — недоступен.
/// Starting, Unknown и Stale не входят ни в числитель, ни в знаменатель доступности.
/// </summary>
public enum ProbeState
{
    Up,
    Degraded,
    Down,
    Starting,
    Unknown,
    Stale,
}
