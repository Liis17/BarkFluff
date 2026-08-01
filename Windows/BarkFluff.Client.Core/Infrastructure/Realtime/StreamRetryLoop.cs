namespace BarkFluff.Client.Core.Infrastructure.Realtime;

/// <summary>
/// Вечный цикл «подключиться — читать — переподключиться» поверх серверного gRPC-стрима.
/// <para>
/// Нужен потому, что <c>WebApiBase.ReadStream</c> гасит <c>RpcException</c> и завершает
/// перечисление: обрыв связи неотличим от штатного конца стрима и не приходит исключением.
/// Без цикла первый же разрыв — сон ноутбука, смена сети, перезапуск сервера — навсегда
/// оставлял клиент без сообщений и статусов.
/// </para>
/// Цикл вынесен отдельно от сервисов и работает на делегатах: сервисы держат конкретный
/// <c>WebApi</c>, который не подменить в тестах, а поведение переподключения проверять надо.
/// </summary>
internal static class StreamRetryLoop
{
    private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Сколько соединение обязано прожить, чтобы следующий обрыв считался первым.
    /// Без этого порога сервер, принимающий соединение и тут же его роняющий, держал бы
    /// нас на минимальной паузе бесконечно.
    /// </summary>
    internal static readonly TimeSpan StableConnection = TimeSpan.FromSeconds(30);

    /// <summary>Задержка перед попыткой номер <paramref name="attempt"/>: 2, 4, 8, 16, 30, 30 с.</summary>
    internal static TimeSpan Backoff(int attempt)
    {
        var multiplier = Math.Pow(2, Math.Min(attempt - 1, 10));
        var delay = InitialDelay * multiplier;
        return delay < MaxDelay ? delay : MaxDelay;
    }

    /// <summary>
    /// Номер следующей попытки. Вынесен из цикла отдельно, потому что иначе проверить сброс
    /// счётчика можно было бы только реальным ожиданием порога.
    /// </summary>
    internal static int NextAttempt(int attempt, long connectionLifetimeMs) =>
        connectionLifetimeMs >= StableConnection.TotalMilliseconds ? 1 : attempt + 1;

    /// <param name="connect">Подписка на стрим; <c>null</c> — подключиться не удалось.</param>
    /// <param name="onItem">Обработчик элемента; вызывается из фонового потока.</param>
    /// <param name="onConnectedChanged">Смена состояния связи, если она кому-то нужна.</param>
    /// <param name="backoff">Политика пауз; параметром — чтобы тесты не ждали реальные секунды.</param>
    internal static async Task RunAsync<T>(
        Func<CancellationToken, Task<IAsyncEnumerable<T>?>> connect,
        Action<T> onItem,
        Action<bool>? onConnectedChanged,
        Func<int, TimeSpan> backoff,
        CancellationToken cancellationToken)
    {
        var attempt = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            var startedAt = Environment.TickCount64;
            var wasConnected = false;
            try
            {
                var stream = await connect(cancellationToken);
                if (stream is not null)
                {
                    wasConnected = true;
                    onConnectedChanged?.Invoke(true);
                    await foreach (var item in stream.WithCancellation(cancellationToken))
                    {
                        onItem(item);
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception)
            {
                // Сбой подключения или обработчика — такой же повод переподключиться,
                // как и тихий конец стрима. Уронить цикл он не должен.
            }

            // Остановка сервиса и перезапуск после обновления токена отменяют этот же токен.
            // Сообщить о потере связи здесь значило бы мигать индикатором каждые несколько
            // минут, а гонка с уже стартовавшим новым циклом залипила бы его насовсем.
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            if (wasConnected)
            {
                onConnectedChanged?.Invoke(false);
            }

            attempt = NextAttempt(attempt, Environment.TickCount64 - startedAt);
            try
            {
                await Task.Delay(backoff(attempt), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
