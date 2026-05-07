# BarkFluff.Users — реестр метрик

> ↩ Назад: [[Backend/Users]] · [[Backend/Beacon-Metrics]] (эталон) · [[Backend/GrpcServer]] (общий механизм)

## Как работает сбор метрик

В Seq нет нормальной системы метрик, поэтому метрики сервиса передаются **через структурированные логи** (тот же механизм, что и у [[Backend/Beacon-Metrics|Beacon]]).

1. Код сервиса вызывает `MetricsCollector.Increment / Add / Set` (in-memory, потокобезопасно).
2. `MetricsReporterService` (BackgroundService из `BarkFluff.GrpcServer`) **каждые 5 секунд** делает `SnapshotAndReset` и пишет лог формата
   ```csharp
   _logger.LogInformation("ServiceMetrics {@Metrics}",
       new { ServiceName = "BarkFluff.Users", Metrics = snapshot, Timestamp = DateTime.UtcNow });
   ```
3. Serilog отправляет лог в Seq (`WriteTo.Seq`, batch 100 событий / 2 сек).
4. `Barkfluff.AdminPanel/Services/MetricsCollectorService` раз в час забирает события из Seq по фильтру `@Message like 'ServiceMetrics%'`, парсит `Properties.Metrics.Metrics`, группирует по `Application` и складывает в LiteDB (`HourlyServiceMetrics`).
5. UI админки рендерит метрики из этого кеша.

> ⚠️ Counters в часовом срезе AdminPanel содержат значения только последнего ~5-секундного окна — это ограничение текущей реализации.

## Откуда берутся метрики в Users

| Слой                                             | Что фиксируется                                                                                             |
| ------------------------------------------------ | ----------------------------------------------------------------------------------------------------------- |
| `Host/UsersApiService.cs`                        | gRPC-вход для клиентов (User-токен): профиль, поиск, устройства, приватность, персонализация               |
| `Host/UsersServerApiService.cs`                  | gRPC-вход межсервисный (Service-токен): drafts, confirm, badges, экспорт, public-профиль, админ-операции   |
| `Consumers/SessionRevokedConsumer.cs`            | RabbitMQ: получение событий отзыва сессии                                                                   |
| `Infrastructure/UserInfoQueueSender.cs`          | RabbitMQ: публикация событий об изменении профиля                                                           |
| `Features/AddDraftUser/AddDraftUserCommandHandler.cs` | Бизнес-конфликты при создании черновика (email/username/reserved)                                      |
| `Features/ExportData/ExportDataCommandHandler.cs`     | Исходящие вызовы Files/Messages во время экспорта                                                      |
| `Features/SetProfilePicture/SetProfilePictureCommandHandler.cs` | Исходящий вызов Files                                                                        |
| `BarkFluff.GrpcServer/ServerExceptionInterceptor.cs` | Авто-метрики gRPC: `grpc_requests_total`, `grpc_request_duration_ms_total`, `grpc_requests_failed`, `grpc_requests_errors` |
| `Program.cs`                                     | Стартовые gauges: `service_started_unix`, `db_migration_healthy`                                            |

## Реестр метрик

### Авто-метрики gRPC (общие, ставит interceptor)

| Метрика                          | Тип       | Описание                                                                  |
| -------------------------------- | --------- | ------------------------------------------------------------------------- |
| `grpc_requests_total`            | counter   | Все unary-вызовы к сервису.                                               |
| `grpc_request_duration_ms_total` | counter   | Сумма длительностей всех вызовов за окно. Среднее = `total / requests`.   |
| `grpc_requests_failed`           | counter   | Бизнес-ошибки (`BaseGrpcException`).                                      |
| `grpc_requests_errors`           | counter   | Системные исключения.                                                     |

### Жизненный цикл аккаунта

| Метрика                            | Тип     | Где                                                              | Описание                                                                                                |
| ---------------------------------- | ------- | ---------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------- |
| `drafts_create_requests`           | counter | `UsersServerApiService.AddDraftUser`                             | Любая попытка создания черновика (включая упавшие на конфликте).                                        |
| `drafts_created`                   | counter | `UsersServerApiService.AddDraftUser` (success)                   | Успешные создания черновика. Ключевая метрика регистраций.                                              |
| `drafts_create_errors`             | counter | `UsersServerApiService.AddDraftUser` (catch)                     | Падения создания черновика любого вида.                                                                 |
| `drafts_overridden`                | counter | `UsersServerApiService.OverrideDraftUser`                        | Переопределение черновика (повторная регистрация на те же данные).                                      |
| `users_email_conflicts`            | counter | `AddDraftUserCommandHandler`                                     | Попытка создать черновик на занятый email.                                                              |
| `users_username_conflicts`         | counter | `AddDraftUserCommandHandler`                                     | Попытка создать черновик на занятый username.                                                           |
| `users_reserved_username_blocked`  | counter | `AddDraftUserCommandHandler`                                     | Попытка взять зарезервированный username.                                                               |
| `users_confirm_requests`           | counter | `UsersServerApiService.ConfirmUser`                              | Любой запрос на подтверждение пользователя.                                                             |
| `users_confirmed`                  | counter | `UsersServerApiService.ConfirmUser` (success)                    | Успешные подтверждения. **Главная воронка регистраций**: `users_confirmed / drafts_created`.            |
| `users_confirm_errors`             | counter | `UsersServerApiService.ConfirmUser` (catch)                      | Падения подтверждения (не найден пользователь и т.п.).                                                  |

### Профиль и персонализация

| Метрика                       | Тип     | Где                                                | Описание                                                              |
| ----------------------------- | ------- | -------------------------------------------------- | --------------------------------------------------------------------- |
| `profile_name_updates`        | counter | `UsersApiService.ChangeName`                       | Изменение имени/фамилии.                                              |
| `profile_username_updates`    | counter | `UsersApiService.ChangeUsername`                   | Изменение username.                                                   |
| `profile_bio_updates`         | counter | `UsersApiService.ChangeBio`                        | Изменение био.                                                        |
| `profile_avatar_updates`      | counter | `UsersApiService.SetProfilePicture` (FileId != null), `UsersServerApiService.SetProfilePictureServer` | Установка/смена аватара. |
| `profile_avatar_removals`     | counter | `UsersApiService.SetProfilePicture` (FileId == null) | Удаление аватара (отдельно, чтобы видеть отказы).                   |
| `profile_poster_updates`      | counter | `UsersApiService.SetProfilePoster`                 | Смена/удаление профильного постера.                                   |
| `personalization_updates`     | counter | `UsersApiService.UpdatePersonalization`            | Изменение настроек персонализации.                                    |
| `privacy_updates`             | counter | `UsersApiService.UpdatePrivacySettings`            | Изменение настроек приватности.                                       |

### Поиск и просмотр

| Метрика                       | Тип     | Где                                                                     | Описание                                                                |
| ----------------------------- | ------- | ----------------------------------------------------------------------- | ----------------------------------------------------------------------- |
| `user_searches`               | counter | `UsersApiService.SearchUsers`, `UsersServerApiService.SearchUsersServer` | Любой полнотекстовый/триграммный поиск пользователей.                   |
| `user_search_errors`          | counter | `UsersApiService.SearchUsers` (catch)                                   | Поиск упал (DB / SQL ошибка и т.п.).                                    |
| `user_search_duration_ms_total` | counter | `UsersApiService.SearchUsers` (success)                                 | Сумма времени успешных поисков. Среднее = `total / user_searches`.      |
| `user_lookups`                | counter | `UsersApiService.GetUser`, `UsersServerApiService.GetById/ListByIds`    | Точечные get-запросы пользователя (включая межсервисные).               |
| `existence_checks`            | counter | `CheckExistEmail` / `CheckExistUsername` (User+Server)                  | Проверки занятости email/username (используются формами регистрации).   |
| `login_lookups`               | counter | `UsersServerApiService.FindByLogin`                                     | Поиск пользователя по логину для Identity. Корреляция с попытками логина. |
| `contact_lookups`             | counter | `UsersServerApiService.GetUserContacts`                                 | Получение контактов (email).                                            |
| `public_profile_views`        | counter | `UsersServerApiService.GetUserByUsername`                               | Просмотры публичной страницы профиля (через web).                       |
| `public_profile_not_found`    | counter | `UsersServerApiService.GetUserByUsername`                               | Профиль не найден или это черновик.                                     |
| `public_profile_hidden`       | counter | `UsersServerApiService.GetUserByUsername`                               | Профиль скрыт настройками приватности (`ProfileVisibleOnSite=false`).   |

### Устройства и FCM

| Метрика                       | Тип     | Где                                                                     | Описание                                                              |
| ----------------------------- | ------- | ----------------------------------------------------------------------- | --------------------------------------------------------------------- |
| `device_registrations`        | counter | `UsersServerApiService.RegisterDevice`                                  | Регистрация / обновление устройства (по сути число входов).           |
| `device_deletions`            | counter | `UsersServerApiService.DeleteUserDevice`                                | Удаление устройства (logout с другого устройства).                    |
| `device_renames`              | counter | `UsersApiService.RenameDevice`                                          | Переименование устройства пользователем.                              |
| `device_lookups`              | counter | `GetDevices` / `GetCurrentDevice` / `GetUserDevices` / `GetDevicesWithFirebaseTokens` | Чтение списка устройств.                              |
| `firebase_token_updates`      | counter | `UsersApiService.SetFirebaseToken`                                      | Установка/обновление FCM-токена.                                      |
| `notifications_toggles`       | counter | `UsersApiService.SetNotificationsEnabled`                               | Включение/выключение пушей.                                           |

### Бейджи

| Метрика                       | Тип     | Где                                          | Описание                                                  |
| ----------------------------- | ------- | -------------------------------------------- | --------------------------------------------------------- |
| `badges_assigned`             | counter | `UsersServerApiService.AssignUserBadge`      | Назначение бейджа пользователю (admin op).                |
| `badges_removed`              | counter | `UsersServerApiService.RemoveUserBadge`      | Снятие бейджа.                                            |
| `badges_priority_updated`     | counter | `UsersServerApiService.UpdateUserBadgePriority` | Изменение приоритета бейджа на пользователе.           |
| `badges_created`              | counter | `UsersServerApiService.CreateBadge`          | Создание нового бейджа (CRUD).                            |
| `badges_updated`              | counter | `UsersServerApiService.UpdateBadge`          | Редактирование бейджа.                                    |
| `badges_deleted`              | counter | `UsersServerApiService.DeleteBadge`          | Удаление бейджа.                                          |
| `badge_lookups`               | counter | `GetUserBadges` / `GetAllBadges`             | Чтение бейджей.                                           |

### Тяжёлые / админ-операции

| Метрика                          | Тип     | Где                                                | Описание                                                                       |
| -------------------------------- | ------- | -------------------------------------------------- | ------------------------------------------------------------------------------ |
| `data_exports`                   | counter | `UsersServerApiService.ExportData`                 | GDPR-выгрузка данных пользователя. Должно быть редким событием.                |
| `data_export_errors`             | counter | `UsersServerApiService.ExportData` (catch)         | Падения экспорта.                                                              |
| `data_export_duration_ms_total`  | counter | `UsersServerApiService.ExportData` (success)       | Сумма длительностей экспортов. Среднее = `total / data_exports`.               |
| `storage_limit_updates`          | counter | `UsersServerApiService.UpdateStorageLimit`         | Изменение квоты на хранилище через админку.                                    |

### Исходящие gRPC-вызовы

| Метрика                       | Тип     | Где                                                                     | Описание                                                          |
| ----------------------------- | ------- | ----------------------------------------------------------------------- | ----------------------------------------------------------------- |
| `files_fetch_success`         | counter | `SetProfilePictureCommandHandler`, `ExportDataCommandHandler`, `UsersServerApiService.GetUserByUsername` | Успешный gRPC-вызов в `BarkFluff.Files`. |
| `files_fetch_errors`          | counter | те же места (catch)                                                     | Сбой связи с Files. Маркер проблем Users ↔ Files.                  |
| `messages_fetch_success`      | counter | `ExportDataCommandHandler`                                              | Успешный gRPC-вызов в `BarkFluff.Messages`.                       |
| `messages_fetch_errors`       | counter | `ExportDataCommandHandler` (catch)                                      | Сбой связи с Messages.                                            |

### RabbitMQ

| Метрика                            | Тип     | Где                                                | Описание                                                                  |
| ---------------------------------- | ------- | -------------------------------------------------- | ------------------------------------------------------------------------- |
| `session_revoked_received`         | counter | `SessionRevokedConsumer`                           | Получено событие отзыва сессии от Identity. Должно совпадать с logout-ами. |
| `user_events_published`            | counter | `UserInfoQueueSender` (любой Publish)              | Сводный счётчик событий, отправленных в очередь.                          |
| `user_name_changed_published`      | counter | `UserInfoQueueSender.NameChangedEvent`             | Публикация `UserChangedName`.                                             |
| `user_username_changed_published`  | counter | `UserInfoQueueSender.UsernameChangedEvent`         | Публикация `UserChangedUsername`.                                         |
| `user_avatar_changed_published`    | counter | `UserInfoQueueSender.UserChangedAvatarEvent`       | Публикация `UserChangedAvatar`.                                           |
| `user_password_changed_published`  | counter | `UserInfoQueueSender.UserChangedPasswordEvent`     | Публикация `UserChangedPassword`.                                         |
| `user_bio_changed_published`       | counter | `UserInfoQueueSender.UserBioChangedEvent`          | Публикация `UserChangedBio`.                                              |

### Gauges (показатели — последнее значение, не сбрасываются)

| Метрика                            | Где                                                       | Описание                                                                                       |
| ---------------------------------- | --------------------------------------------------------- | ---------------------------------------------------------------------------------------------- |
| `service_started_unix`             | `Program.cs` (один раз)                                   | Unix-timestamp старта процесса. Uptime = `now - service_started_unix`.                         |
| `db_migration_healthy`             | `Program.cs`                                              | 1 — миграция применена успешно, 0 — упала (процесс не дошёл до `Migrate()` или вылетел).       |
| `last_user_confirmed_unix`         | `UsersServerApiService.ConfirmUser`                       | Когда последний раз кто-то завершил регистрацию.                                               |
| `last_draft_created_unix`          | `UsersServerApiService.AddDraftUser`                      | Когда последний раз создавался черновик.                                                       |
| `last_device_registered_unix`      | `UsersServerApiService.RegisterDevice`                    | Когда последний раз кто-то логинился (регистрация устройства = логин).                         |
| `last_user_search_unix`            | `UsersApiService.SearchUsers`                             | Когда последний раз кто-то искал пользователя.                                                 |
| `last_data_export_unix`            | `UsersServerApiService.ExportData`                        | Когда последний раз был запрошен экспорт данных.                                               |
| `last_session_revoked_unix`        | `SessionRevokedConsumer`                                  | Когда последний раз приходило событие отзыва. Если «протухло» — RabbitMQ возможно молчит.      |

## Производные показатели для админки

| Производный показатель                 | Формула                                                                  |
| -------------------------------------- | ------------------------------------------------------------------------ |
| Конверсия регистраций                  | `users_confirmed / drafts_created`                                       |
| Доля сбоев при создании черновика      | `drafts_create_errors / drafts_create_requests`                          |
| Доля сбоев при подтверждении           | `users_confirm_errors / users_confirm_requests`                          |
| Доля «занят/зарезервирован» при регистрации | `(users_email_conflicts + users_username_conflicts + users_reserved_username_blocked) / drafts_create_requests` |
| Среднее время поиска, мс               | `user_search_duration_ms_total / user_searches`                          |
| Среднее время экспорта, мс             | `data_export_duration_ms_total / data_exports`                           |
| Доля скрытых публичных профилей        | `(public_profile_hidden + public_profile_not_found) / public_profile_views` |
| Доля сбоев Files-API                   | `files_fetch_errors / (files_fetch_success + files_fetch_errors)`        |
| Доля сбоев Messages-API                | `messages_fetch_errors / (messages_fetch_success + messages_fetch_errors)` |
| Uptime сервиса, сек                    | `now_unix - service_started_unix`                                        |
| Минут с последней регистрации          | `(now_unix - last_user_confirmed_unix) / 60`                             |
| Минут с последнего отзыва сессии       | `(now_unix - last_session_revoked_unix) / 60`                            |

## Соглашения по именованию

Те же, что и у [[Backend/Beacon-Metrics|Beacon]]:

- `snake_case`, plurals для счётчиков (`drafts_created`, `device_registrations`).
- Суффикс `_errors` — счётчики падений, парный к успехам.
- Суффикс `_requests` — общий счётчик попыток (= `_success + _errors`), там, где важна воронка.
- Суффикс `_total` — кумулятивная сумма за окно (мс, байты).
- Суффикс `_unix` — gauge с Unix-timestamp последнего события.
- Суффикс `_healthy` — бинарный gauge 0/1.
- Префикс по доменной зоне: `profile_*`, `device_*`, `badges_*`, `users_*`, `drafts_*`, `data_export_*`, `public_profile_*`, `files_*`, `messages_*`.

## Куда добавлять новые метрики

Любая ветка кода, которая:
- обрабатывает gRPC-запрос (вход → счётчик, успех → счётчик, ошибка → счётчик; для дорогих — длительность через `_duration_ms_total`),
- ходит во внешний сервис (Files/Messages/...) — `*_fetch_success` / `*_fetch_errors`,
- публикует/принимает RabbitMQ-событие — `*_published` / `*_received`,
- меняет ключевую сущность (пользователь, устройство, бейдж) — счётчик факта изменения + gauge `last_*_unix`,

должна писать через `MetricsCollector`. Не дублируем в общий `LogInformation` — для метрик используется только формат `ServiceMetrics {@Metrics}` через `MetricsReporterService`.
