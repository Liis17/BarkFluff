namespace Barkfluff.AdminPanel.Models;

public enum DeployJobKind
{
    Update,
    Restart,
    SwitchBranch
}

public enum DeployJobState
{
    Queued,
    Running,
    Completed,
    Failed
}

public enum DeployStepState
{
    Pending,
    InProgress,
    Completed,
    Failed
}

/// <summary>
/// Один шаг задачи деплоя — операция над одним сервисом
/// </summary>
public class DeployStep
{
    /// <summary>Имя сервиса в docker compose</summary>
    public string Service { get; init; } = string.Empty;

    /// <summary>Целевая ветка (только для SwitchBranch)</summary>
    public string? Branch { get; init; }

    public DeployStepState State { get; set; } = DeployStepState.Pending;

    /// <summary>Сообщение о результате шага (ошибка, причина отката и т.п.)</summary>
    public string? Message { get; set; }

    /// <summary>Был ли шаг откачен на предыдущий образ/ветку после неудачного деплоя</summary>
    public bool RolledBack { get; set; }
}

/// <summary>
/// Задача деплоя в серверной очереди: обновление, перезапуск или переключение ветки
/// одного или нескольких сервисов. Выполняется последовательно, по одному шагу.
/// </summary>
public class DeployJob
{
    public Guid Id { get; init; }

    public DeployJobKind Kind { get; init; }

    public List<DeployStep> Steps { get; init; } = new();

    public DeployJobState State { get; set; } = DeployJobState.Queued;

    /// <summary>Сводка ошибок завершённой задачи</summary>
    public string? Error { get; set; }

    public DateTime CreatedAtUtc { get; init; }

    public DateTime? StartedAtUtc { get; set; }

    public DateTime? FinishedAtUtc { get; set; }
}

/// <summary>
/// Снимок задачи для LiteDB (история между перезапусками панели). Состояния — строками,
/// чтобы сохранённые данные не зависели от порядка значений enum.
/// </summary>
public class DeployJobRecord
{
    public Guid Id { get; set; }

    public string Kind { get; set; } = string.Empty;

    public string State { get; set; } = string.Empty;

    public string? Error { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? StartedAtUtc { get; set; }

    public DateTime? FinishedAtUtc { get; set; }

    public List<DeployStepRecord> Steps { get; set; } = new();
}

public class DeployStepRecord
{
    public string Service { get; set; } = string.Empty;

    public string? Branch { get; set; }

    public string State { get; set; } = string.Empty;

    public string? Message { get; set; }

    public bool RolledBack { get; set; }
}
