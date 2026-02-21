namespace Barkfluff.AdminPanel.Models.Dtos;

/// <summary>
/// Статус контейнера Docker
/// </summary>
public class ContainerStatusDto
{
    /// <summary>
    /// Имя контейнера
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// ID контейнера
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Образ контейнера
    /// </summary>
    public string Image { get; set; } = string.Empty;

    /// <summary>
    /// Статус контейнера (running, stopped, restarting, paused, exited, dead)
    /// </summary>
    public string State { get; set; } = string.Empty;

    /// <summary>
    /// Статус контейнера в формате Docker (Up 2 days, Exited (0) 1 hour ago)
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// Порты контейнера
    /// </summary>
    public string Ports { get; set; } = string.Empty;

    /// <summary>
    /// Время создания контейнера
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Запущен ли контейнер
    /// </summary>
    public bool IsRunning => State.Equals("running", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Запрос на выполнение действия с контейнером
/// </summary>
public class ContainerActionRequestDto
{
    /// <summary>
    /// Имя контейнера
    /// </summary>
    public string ContainerName { get; set; } = string.Empty;

    /// <summary>
    /// Тип действия (start, stop, restart, pull)
    /// </summary>
    public string Action { get; set; } = string.Empty;
}

/// <summary>
/// Результат выполнения действия с контейнером
/// </summary>
public class ContainerActionResponseDto
{
    /// <summary>
    /// Успешно ли выполнено действие
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Сообщение о результате
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Детали ошибки (если есть)
    /// </summary>
    public string? ErrorDetails { get; set; }
}

/// <summary>
/// Информация об образе Docker
/// </summary>
public class ImageInfoDto
{
    /// <summary>
    /// ID образа
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Репозиторий образа
    /// </summary>
    public string Repository { get; set; } = string.Empty;

    /// <summary>
    /// Тег образа
    /// </summary>
    public string Tag { get; set; } = string.Empty;

    /// <summary>
    /// Размер образа в байтах
    /// </summary>
    public long Size { get; set; }

    /// <summary>
    /// Время создания образа
    /// </summary>
    public DateTime CreatedAt { get; set; }
}
