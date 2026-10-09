# BarkFluff.Users — метрики

Users использует общий `MetricsCollector` и `MetricsReporterService`. Накопленные счётчики экспортируются раз в 10 секунд, gauges сохраняют последнее значение. Пути к коду Users ниже отсчитываются от `Backend/BarkFluff.Users/`; общая реализация метрик — `Backend/BarkFluff.GrpcServer/Metrics/` от корня репозитория.

## Действующие области

- Профиль, поиск аккаунта, проверка username/email и поиск пользователей.
- Устройства, Firebase tokens, notification preferences и mute чата.
- Privacy, legal consent, personalization, папки чатов и постеры профиля.
- Регистрация/получение prekey bundle, пополнение one-time ключей и смена signed prekey.
- Внутренние операции: drafts, подтверждение аккаунта, бейджи, data export, storage limits, bot users, разрешение remote profiles/UUID.
- RabbitMQ: публикация изменений профиля и получение отзыва сессии.

Значения задаются непосредственно в `UsersApiService`, `UsersServerApiService`, нужном feature handler и `Consumers/`; отдельного автоматически создаваемого реестра на каждый RPC нет. `MetricsCollector.Set` задаёт gauge (например, timestamp последней операции), `Increment`/`Add` — counters. Общая реализация находится в `Backend/BarkFluff.GrpcServer/Metrics/`.
