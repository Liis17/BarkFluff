# BarkFluff.Onliner — реестр метрик

> ↩ Назад: [[Backend/Onliner]] · [[Backend/GrpcServer]] (общий механизм) · [[Backend/Beacon-Metrics]] (пример общей схемы)

## Как они работают

Метрики публикуются через Serilog → Seq под именем сообщения `ServiceMetrics {@Metrics}`, фильтр `Application = BarkFluff.Onliner`. Делает это `MetricsReporterService` из `BarkFluff.GrpcServer.Metrics` каждые 5 секунд: вызывает `MetricsCollector.SnapshotAndReset()` и логирует словарь.

**Важно:** в `MetricsCollector` есть две сущности:
- `_counters` — увеличиваются через `Increment`/`Add`, **сбрасываются** в `SnapshotAndReset` (за интервал).
- `_gauges` — устанавливаются через `Set`, **не сбрасываются** (текущее состояние).

Для текущего числа активных подписок/online-юзеров **нельзя** использовать `Increment(+1)`/`Add(-1)` — значение обнулится между циклами. Поэтому Onliner имеет отдельный `MetricsSnapshotService` (`BackgroundServices/MetricsSnapshotService.cs`), который раз в 2 секунды снимает gauge-значения из in-memory сервисов и кладёт их в `MetricsCollector.Set`.

Админ-панель читает их через `MetricsCollectorService` → `SeqService.GetAllEventsListAsync(filter:"@Message like 'ServiceMetrics%'")` и кеширует в LiteDB (`HourlyServiceMetrics`).

## Реестр метрик

### gRPC API (counters)

| Метрика | Тип | Где | Смысл |
|---|---|---|---|
| `get_online_status_requests` | counter | `OnlinerApiService.GetOnlineStatus` | Сколько раз был запрошен статус пользователей |
| `get_online_status_user_ids_total` | counter (cumulative) | `OnlinerApiService.GetOnlineStatus` | Суммарное число UserId-ов, по которым запрошен статус (реальная нагрузка, не RPS) |
| `set_online_status_requests` | counter | `OnlinerApiService.SetOnlineStatus` | Heartbeat-вызовы клиентов (главный индикатор активности) |
| `subscribe_requests` | counter | `OnlinerApiService.SubscribeToOnlineStatus` | Открытия streaming-подписок |
| `change_users_in_subscription_requests` | counter | `OnlinerApiService.ChangeUsersInSubscription` | Замена списка отслеживаемых юзеров в активной подписке |

### Typing (counters)

| Метрика | Смысл |
|---|---|
| `set_typing_status_requests` | Unary heartbeat отправки статуса печати |
| `typing_heartbeats` | Успешно принятые heartbeat'ы |
| `typing_heartbeats_rejected_by_membership` | Отклонены — юзер не участник чата |
| `typing_subscribe_requests` | Открытия стрима Typing |
| `change_chats_in_typing_subscription_requests` | Смена списка отслеживаемых чатов |
| `typing_subscriptions_registered` / `_disconnected` | Регистрация/закрытие typing-подписок |
| `typing_subscriptions_hidden_by_membership` | Чаты отфильтрованы — юзер не участник |
| `typing_notifications_sent` / `typing_notification_errors` | Доставка typing-события подписчикам |

### Membership filter

| Метрика | Смысл |
|---|---|
| `membership_checks` / `membership_check_errors` | Проверка участия юзера в чате (для typing) |

### Подписки на онлайн-статус (counters + gauges)

| Метрика | Тип | Где | Смысл |
|---|---|---|---|
| `subscriptions_registered` | counter | `SubscribeToOnlineStatusQueryHandler` | Зарегистрированные подписки |
| `subscriptions_disconnected` | counter | `SubscribeToOnlineStatusQueryHandler` | Отключенные подписки (delta ≈ прирост подключений) |
| `subscriptions_hidden_by_privacy` | counter (cumulative) | `SubscribeToOnlineStatusQueryHandler` | Сколько UserId-ов в запросах подписки отфильтровано по приватности |
| `active_subscriptions` | gauge | `MetricsSnapshotService` → `SubscriptionsManager.GetActiveSubscriptionsCount()` | Текущее число открытых streaming-подписок |
| `tracked_unique_users` | gauge | `MetricsSnapshotService` | Размер обратного индекса подписок — сколько уникальных юзеров отслеживается всеми подписчиками вместе |

### Хранилище статусов (gauges + counters)

| Метрика | Тип | Где | Смысл |
|---|---|---|---|
| `online_users_count` | gauge | `MetricsSnapshotService` → `OnlineStatusStorage.GetOnlineCount()` | Сейчас в статусе Online |
| `storage_total_count` | gauge | `MetricsSnapshotService` → `OnlineStatusStorage.GetTotalCount()` | Всего записей в in-memory storage (Online + Offline) |
| `status_changes.online` | counter | `SetOnlineStatusCommandHandler` | Переходы Offline/Unknown → Online |
| `status_changes.offline` | counter | `OfflineDetectionService` | Переходы Online → Offline (по таймауту 5с) |

### Уведомления

| Метрика | Тип | Где | Смысл |
|---|---|---|---|
| `status_notifications_sent` | counter | `OnlineStatusNotifier` | Успешные отправки `UserOnlineStatus` в стримы подписчиков |
| `status_notification_errors` | counter | `OnlineStatusNotifier` | Ошибки записи в стрим (стрим закрыт / клиент дисконнект) |

### Privacy filter

| Метрика | Тип | Где | Смысл |
|---|---|---|---|
| `visibility_checks` | counter | `OnlineVisibilityFilter` | gRPC-вызовы к `Users.GetUserPrivacy` |
| `visibility_check_errors` | counter | `OnlineVisibilityFilter` | Сбои Users-сервиса (fail-closed → пользователь скрыт) |

### Background services

| Метрика | Тип | Где | Смысл |
|---|---|---|---|
| `offline_detection_runs` | counter | `OfflineDetectionService` | Запуски цикла детекции (раз в секунду) |
| `offline_detection_errors` | counter | `OfflineDetectionService` | Сбои цикла детекции |
| `db_persistence_runs` | counter | `DatabasePersistenceService` | Циклы сохранения статусов в БД (раз в 10 мин) |
| `db_persistence_errors` | counter | `DatabasePersistenceService` | Сбои сохранения |
| `db_records_saved_total` | counter (cumulative) | `DatabasePersistenceService` | Сколько строк вставлено/обновлено за интервал |

### Федерация presence/typing (этап 4.2)

| Метрика | Тип | Где | Смысл |
|---|---|---|---|
| `remote_status_upserts` | counter | `UpsertRemoteStatusCommandHandler` | Статусов remote-пользователей влито Federation'ом |
| `remote_typing_injections` | counter | `InjectRemoteTypingCommandHandler` | Событий набора remote-пользователей ретранслировано |
| `remote_snapshot_errors` | counter | `SubscribeToOnlineStatusQueryHandler` | Стрим закрылся до отправки начального снимка remote-статусов |
| `presence_interest_reports` | counter | `PresenceInterestReporter` | Успешных heartbeat'ов интереса в Federation |
| `presence_interest_errors` | counter | `PresenceInterestReporter` | Сбоев heartbeat'а (ретраев нет — следующий тик через N секунд). `Unimplemented` сюда **не** попадает: до этапа 4.3 это норма |
| `remote_tracked_uuids` | gauge | `MetricsSnapshotService` | Сколько уникальных remote-uuid отслеживает **этот** инстанс (per-instance, на дашбордах суммировать) |

### Прочее

| Метрика | Тип | Где | Смысл |
|---|---|---|---|
| `sessions_revoked` | counter | `SessionRevokedConsumer` | Получено событий отзыва сессии из RabbitMQ |
| `service_started_unix` | gauge | `Program.cs` (один раз при старте) | Unix-секунды старта процесса (для аптайма/ребутов) |

## Производные показатели для админ-панели

- **Прирост подключений за период:** `subscriptions_registered - subscriptions_disconnected`. Если расходится с дельтой `active_subscriptions` — есть утечка стримов.
- **Heartbeat success rate:** `set_online_status_requests` показывает живых клиентов в моменте; коррелирует с `online_users_count`.
- **Стабильность Notifier:** `status_notification_errors / (sent + errors)`. Высокая доля = клиенты дисконнектят без cleanup.
- **Стабильность Users-зависимости:** `visibility_check_errors / visibility_checks`. Рост = деградация сервиса Users.
- **Privacy-pressure:** `subscriptions_hidden_by_privacy` показывает, сколько UserId-ов в подписках вырезано privacy.
- **Аптайм:** `now() - service_started_unix`.
- **Здоровье моста presence:** `presence_interest_errors / presence_interest_reports`. Устойчивый рост = Federation недоступен, и статусы remote-собеседников протухнут по TTL (`Onliner:RemotePresenceTtlSeconds`).
- **Спрос на федеративный presence:** сумма `remote_tracked_uuids` по инстансам ≈ размер union'а, который Federation держит в S2S-подписках.

## Где смотреть в коде

- Реестр/реализация: `Backend/BarkFluff.GrpcServer/Metrics/MetricsCollector.cs`, `MetricsReporterService.cs`
- Snapshot gauges: `Backend/BarkFluff.Onliner/BackgroundServices/MetricsSnapshotService.cs`
- Helper-методы для gauge: `OnlineStatusStorage.GetOnlineCount/GetTotalCount`, `OnlineStatusSubscriptionsManager.GetActiveSubscriptionsCount/GetTrackedUniqueUsersCount`
- AdminPanel ingest: `Backend/Barkfluff.AdminPanel/Services/MetricsCollectorService.cs`, `SeqService.cs`, `Endpoints/SeqEndpoints.cs`

### Исходящий federated typing (этап 4.4)

| Метрика | Тип | Где | Смысл |
|---|---|---|---|
| `federated_typing_sent` | counter | `FederatedTypingSender` | Событий набора отправлено в Federation |
| `federated_typing_errors` | counter | `FederatedTypingSender` | Сбоев отправки. Ретраев нет by design; на локальный typing не влияет |
