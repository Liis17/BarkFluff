namespace Barkfluff.AdminPanel.Models.Dtos;

/// <summary>
/// Запрос на обновление перечисленных контейнеров (замена браузерного цикла)
/// </summary>
public class UpdateContainersRequestDto
{
    /// <summary>Имена контейнеров (или сервисов compose)</summary>
    public List<string>? Containers { get; set; }
}

/// <summary>
/// Ответ на постановку задачи деплоя в очередь
/// </summary>
public class DeployJobStartDto
{
    public bool Success => true;

    public Guid JobId { get; init; }

    public string Message { get; init; } = string.Empty;
}
