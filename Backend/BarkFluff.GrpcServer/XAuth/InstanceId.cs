namespace BarkFluff.GrpcServer.XAuth;

/// <summary>
/// Уникальный идентификатор экземпляра сервиса. В Docker это ID контейнера
/// (переменная окружения HOSTNAME), иначе — сгенерированный на старте Guid.
/// Используется для fan-out очередей RabbitMQ: уникальное имя ReceiveEndpoint
/// на экземпляр гарантирует, что каждый экземпляр получает копию broadcast-события
/// (отзыв сессии, доставка в gRPC-стримы) вместо competing-consumer.
/// </summary>
public static class InstanceId
{
    public static string Current { get; } =
        Environment.GetEnvironmentVariable("HOSTNAME") is { Length: > 0 } hostname
            ? hostname
            : Guid.NewGuid().ToString("N");
}
