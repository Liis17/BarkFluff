# BarkFluff.Messages — карта проекта

Messages хранит чаты и сообщения, публикует события и предоставляет пользовательские и межсервисные операции. Пути `Host/`, `Features/`, `Persistence/` и другие пути компонента ниже — относительно `Backend/BarkFluff.Messages/`; `Shared/` и `Tests/` — от корня репозитория.

## Точки входа

- `Program.cs` регистрирует MediatR, `MetricsBehavior`, PostgreSQL/Redis, RabbitMQ и два gRPC service.
- `Host/MessagesApiService.cs` — пользовательский `MessagesApi` (`TokenType.User`). `Host/MessagesServerApiService.cs` — `MessagesServerApi` (`TokenType.Service`). Последний реализует импорт/применение федеративных событий, проверки доступа и серверные операции; `ExportChatEvents` объявлен в proto, но остаётся `Unimplemented`.
- `Infrastructure/` содержит издателей событий и behaviors; `Consumers/` принимает изменения профиля, revoke и отказ от федеративного чата.

## Функциональные области

- Чаты и обычные сообщения: создание группы/личного чата, списки, участники, send/edit/delete/read, покинуть/скрыть/удалить чат.
- Приглашения в зашифрованный личный чат: `Features/{Create,Accept,Reject}PrivateChat`; private ciphertext хранит PostgreSQL через `Persistence/Services/EncryptedMessagesStorage.cs`, приглашения — Redis через `Persistence/Services/PrivateChatInviteStore.cs`.
- Секретные чаты с привязкой к устройству: invite/accept/reject/send/ack; `Persistence/Services/SecretMessageBuffer.cs` хранит opaque envelopes и secret invites в Redis; сервер их не расшифровывает.
- Drafts, full-text search, pinned messages и reply previews имеют отдельные features/storage.
- Федерация: import/apply handlers, UUID-членство, снимки метаданных вложений, LWW resolver и проверки доступа к файлам/presence расположены в `Features/` и `Persistence/Services/ChatsStorage.cs`.

## Данные и интеграции

- `Domain/` содержит Chat, ChatMember, Message, состояние private/secret сообщений, drafts, pins и outbox.
- `Persistence/MessagesContext.cs`, `Persistence/Services/` и миграции обслуживают PostgreSQL.
- Только `SendMessageCommandHandler` пишет сообщение и `NewMessageEvent` в transactional outbox через `MessagesStorage.AddMessageWithOutboxAsync`. `MessageOutboxDispatcher` выполняет повторы; порядок по чату гарантирует outbox Federation, не обычный Messages outbox.
- Redis хранит chat cache, pending private invites и 24-часовой буфер секретных сообщений/приглашений.
- Тесты: `Tests/BarkFluff.Messages.Tests`; PostgreSQL integration test поиска включается переменной окружения.

← [[Backend/Messages]]
