namespace BarkFluff.Federation.Tests.Infrastructure;

/// <summary>
/// Управляемые часы для тестов TTL/окон. Минимальная замена FakeTimeProvider из
/// Microsoft.Extensions.TimeProvider.Testing — тянуть пакет ради двух методов незачем.
/// </summary>
public sealed class TestTimeProvider : TimeProvider
{
    private DateTimeOffset _now;

    public TestTimeProvider(DateTimeOffset? start = null)
        => _now = start ?? new DateTimeOffset(2026, 7, 26, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan delta) => _now = _now.Add(delta);
}
