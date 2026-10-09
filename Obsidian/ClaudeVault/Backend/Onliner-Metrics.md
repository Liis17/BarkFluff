# BarkFluff.Onliner — метрики

Onliner использует общий `MetricsCollector`: счётчики пакетируются, gauges сохраняют последнее значение. Общий `MetricsReporterService` экспортирует накопленные счётчики раз в 10 секунд. Пути к коду Onliner ниже отсчитываются от `Backend/BarkFluff.Onliner/`; общая реализация метрик — `Backend/BarkFluff.GrpcServer/Metrics/` от корня репозитория.

## Действующие группы

- API: число вызовов `GetOnlineStatus`, `SetOnlineStatus`, подписок и typing RPC.
- Privacy/членство: проверки, подписки, скрытые по privacy записи и ошибки. Ошибка проверки членства закрывает доступ.
- Fan-out: отправленные online/typing notifications и ошибки; gauges открытых/закрытых subscriptions.
- Background tasks: запуски/ошибки offline detection и database persistence; число сохранённых записей.
- Федерация: отчёты интереса к presence и ошибки, исходящие federated typing, gauges отслеживаемых UUID/статусов.

`MetricsSnapshotService` периодически обновляет gauges по живым managers/stores: активные подписки, отслеживаемые пользователи/UUID и online users. Gauge — текущее значение, не счётчик за интервал.

Точные ключи задаются в `Host/OnlinerApiService.cs`, `Services/`, `BackgroundServices/` и `Consumers/`. Регистрация и экспорт — `Program.cs` и `Backend/BarkFluff.GrpcServer/Metrics/`.
