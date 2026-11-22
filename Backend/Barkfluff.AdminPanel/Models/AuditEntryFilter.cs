namespace Barkfluff.AdminPanel.Models;

/// <summary>
/// Необязательные фильтры журнала аудита; все заданные поля комбинируются по AND.
/// Нормализация (trim, регистр, «@», значение success) выполняется в AuditService.
/// </summary>
public sealed record AuditEntryFilter(
    string? AdminUsername = null,
    string? Action = null,
    string? Outcome = null,
    DateTime? FromUtc = null,
    DateTime? ToUtc = null);
