# BarkFluff Metrics Reference

All metrics are emitted as structured log events every 5 seconds via `MetricsReporterService`.
They are sent to Seq and can be queried by `Application` property or metric name.

## Global Metrics (all services via ServerExceptionInterceptor)

| Metric | Type | Description |
|--------|------|-------------|
| `grpc_requests_total` | counter | Total gRPC unary requests received |
| `grpc_requests_failed` | counter | gRPC requests that threw BaseGrpcException (business errors) |
| `grpc_requests_errors` | counter | gRPC requests that threw unexpected exceptions |

## Identity (BarkFluff.Identity)

| Metric | Type | Description |
|--------|------|-------------|
| `auth_login_attempts` | counter | Login attempts (Auth RPC called) |
| `auth_login_success` | counter | Successful authentications |
| `tokens_refreshed` | counter | Access token refresh via CreateToken |
| `sessions_created` | counter | New account registrations via CreateAccount |
| `sessions_removed` | counter | Active sessions removed via RemoveActiveSession |
| `otp_codes_sent` | counter | OTP codes dispatched via ResetPassword |

## Users (BarkFluff.Users)

| Metric | Type | Description |
|--------|------|-------------|
| `profile_updates` | counter | Profile changes (name, username, bio, picture) |
| `user_searches` | counter | User search operations |
| `user_lookups` | counter | GetUser/GetById/ListByIds lookups |
| `device_registrations` | counter | New device registrations |

## Messages (BarkFluff.Messages)

| Metric | Type | Description |
|--------|------|-------------|
| `messages_sent` | counter | Messages sent via SendMessage |
| `messages_read` | counter | MarkAsRead operations |
| `chats_created` | counter | Group chats created via CreateGroupChat |
| `rabbitmq_events_consumed` | counter | RabbitMQ events processed (UserChangedName, UserChangedAvatar) |

## Files (BarkFluff.Files)

| Metric | Type | Description |
|--------|------|-------------|
| `files_uploaded` | counter | Successfully uploaded files |
| `files_downloaded` | counter | Download URL generation requests |
| `upload_bytes_total` | counter | Total bytes uploaded |

## Updates (BarkFluff.Updates)

**Counters:**

| Metric | Type | Description |
|--------|------|-------------|
| `new_messages_subscriptions_opened` | counter | gRPC `SubscribeNewMessages` подписки открыты |
| `new_messages_subscriptions_closed` | counter | gRPC `SubscribeNewMessages` подписки закрыты |
| `read_by_subscriptions_opened` | counter | gRPC `SubscribeMessagesRead` подписки открыты |
| `read_by_subscriptions_closed` | counter | gRPC `SubscribeMessagesRead` подписки закрыты |
| `active_subscriptions` | counter | (legacy) суммарный счётчик открытых подписок |
| `active_subscriptions_removed` | counter | (legacy) суммарный счётчик закрытых подписок |
| `rabbitmq_events_consumed` | counter | Все события из RabbitMQ (NewMessage + ReadBy + SessionRevoked) |
| `new_message_events_consumed` | counter | События NewMessage из RabbitMQ |
| `new_message_events_errors` | counter | Ошибки парсинга/публикации NewMessage |
| `read_by_events_consumed` | counter | События MessageRead из RabbitMQ |
| `read_by_events_errors` | counter | Ошибки обработки MessageRead |
| `session_revoked_events_consumed` | counter | События SessionRevoked из RabbitMQ |
| `sessions_revoked` | counter | Сессии, отозванные через TokenRevocationCache |
| `new_messages_broadcast` | counter | Успешно доставленных NewMessage в gRPC-стримы |
| `new_messages_broadcast_errors` | counter | Ошибки записи NewMessage в стрим |
| `events_broadcast` | counter | (legacy) синоним `new_messages_broadcast` |
| `events_broadcast_errors` | counter | (legacy) синоним `new_messages_broadcast_errors` |
| `read_by_broadcast` | counter | Успешно доставленных MessageRead в gRPC-стримы |
| `read_by_broadcast_errors` | counter | Ошибки записи MessageRead в стрим |
| `push_notifications_scheduled` | counter | Запланированных push-уведомлений (через 5 сек) |
| `push_notifications_sent` | counter | Успешно опубликованных PushNotificationEvent в RabbitMQ |
| `push_notifications_cancelled` | counter | Push-уведомлений, отменённых из-за прочтения |
| `push_notifications_errors` | counter | Ошибки при публикации push-уведомления |

**Gauges:**

| Metric | Type | Description |
|--------|------|-------------|
| `service_started_unix` | gauge | Unix-timestamp старта сервиса (для uptime) |
| `new_messages_subscriptions_active` | gauge | Текущее число активных gRPC-стримов NewMessage |
| `read_by_subscriptions_active` | gauge | Текущее число активных gRPC-стримов MessageRead |
| `subscriptions_active_total` | gauge | Сумма активных подписок обоих типов |

## Notification (BarkFluff.Notification)

| Metric | Type | Description |
|--------|------|-------------|
| `emails_sent` | counter | Successfully sent emails |
| `emails_failed` | counter | Failed email deliveries (SMTP errors) |
| `rabbitmq_events_consumed` | counter | RabbitMQ events processed |

## Beacon (BarkFluff.Beacon)

| Metric | Type | Description |
|--------|------|-------------|
| `server_info_requests` | counter | GetServerInfo RPC calls |
| `navigator_registrations` | counter | Successful Navigator registration cycles |

## FastAuth (BarkFluff.FastAuth)

| Metric | Type | Description |
|--------|------|-------------|
| `tokens_generated` | counter | Device connection tokens generated |

## Onliner (BarkFluff.Onliner)

| Metric | Type | Description |
|--------|------|-------------|
| `status_changes` | counter | SetOnlineStatus calls |
| `active_subscriptions` | counter | Online status stream subscriptions |
| `offline_detections` | counter | Users detected as offline by OfflineDetectionService |

## Configuration (BarkFluff.Configuration)

| Metric | Type | Description |
|--------|------|-------------|
| `config_requests` | counter | GetConfiguration RPC calls |

## Seq Access

- **Web UI**: `http://localhost:8880` (configurable via `SEQ_WEBPORT`)
- **Ingestion API**: `http://seq:5341` (internal Docker network only)
- **Query**: Filter by `Application = "BarkFluff.{ServiceName}"` or search for `ServiceMetrics`
