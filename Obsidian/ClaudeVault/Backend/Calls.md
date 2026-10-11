# BarkFluff.Calls

Сервис управления аудио- и видеозвонками один-на-один и в группах. Серверная часть управляет звонками и выдаёт LiveKit-токены; медиапоток идёт напрямую через LiveKit SFU. Пути к коду Calls ниже отсчитываются от `Backend/BarkFluff.Calls/`; `Shared/` и `Tests/` — от корня репозитория.

## Контракт

`Shared/BarkFluff.Proto/calls_api.proto` описывает `InitiateCall`, `JoinCall`, `AcceptCall`, `RejectCall`, `EndCall`, `SetCallAudioQuality`, подписку устройства `SubscribeCallEvents`, `ListCallHistory` и `GetActiveCalls`. `CallsApi` требует пользовательский токен; для подписки событий дополнительно нужен `device-id` в токене.

По умолчанию gRPC слушает порт 7025, HTTP/1.1 для LiveKit webhook — 7026. `GET /ping` добавляют общие health endpoints. Runtime-порты приходят из Settings (`ServiceId.Calls`).

## Жизненный цикл и медиа

- `Features/CallLifecycle/CallLifecycleHandler.cs` проверяет и выполняет переходы состояния для приглашения, входа, принятия/отклонения, завершения, истории и активных звонков.
- `CallSession` хранится в PostgreSQL. База ограничивает число активных личных звонков для участника и активных групповых звонков для чата.
- `LiveKitTokenService` подписывает токены комнаты; управление комнатами использует серверный API LiveKit. Клиентское медиа не проходит через Calls.
- Webhook `/livekit/webhook` завершает записи комнаты и сообщает о входе/выходе участника. Таймаут звонка обрабатывает `CallRingTimeoutSweeper`.
- События звонков идут через RabbitMQ; отдельная очередь с `InstanceId.Current` доставляет их локальным потокам устройств на каждом инстансе. При завершении Calls записывает системное сообщение через Messages.

Текущие ограничения `CallLifecycleHandler`: групповая история включает только звонки, инициированные текущим пользователем; `GetActiveCalls` проверяет членство, но не заполняет `participant_user_ids`. Наличие полей proto не означает их заполнение.

Основные файлы: `Host/CallsApiService.cs`, `Features/CallLifecycle/CallLifecycleHandler.cs`, `Services/LiveKitTokenService.cs`, `Services/CallEventDispatcher.cs`, `Consumers/CallEventDeliveryConsumer.cs`, `BackgroundServices/CallRingTimeoutSweeper.cs`, `Persistence/CallsContext.cs`.

Тесты: `Tests/BarkFluff.Calls.Tests`. Связанные сервисы: [[Backend/Messages]], [[Backend/Updates]], [[Backend/Settings]].
