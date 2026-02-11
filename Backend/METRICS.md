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

| Metric | Type | Description |
|--------|------|-------------|
| `active_subscriptions` | counter | New stream subscriptions registered |
| `active_subscriptions_removed` | counter | Stream subscriptions removed |
| `events_broadcast` | counter | Events forwarded to subscribers |
| `rabbitmq_events_consumed` | counter | RabbitMQ events processed (NewMessage, ReadBy) |

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
