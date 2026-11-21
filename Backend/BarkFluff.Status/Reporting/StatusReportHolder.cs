namespace BarkFluff.Status.Reporting;

/// <summary>Последний собранный отчёт. Публичные запросы читают только его, не базу.</summary>
public sealed class StatusReportHolder
{
    private volatile StatusReport? _current;

    public StatusReport? Current => _current;

    public void Publish(StatusReport report) => _current = report;
}
