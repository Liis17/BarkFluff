# Закреплённые сообщения — гайд для клиентских агентов

> ↩ Назад: [[Backend/Messages]] · proto: `messages_api.proto`, `shared.proto` · события: [[Backend/Updates]]

Этот документ описывает, какие RPC и события клиент (Android / WPF / Web / iOS / macOS / Linux) должен вызывать и слушать для интеграции функции «закреплённые сообщения». Все вызовы — на сервис **BarkFluff.Messages** (порт 7007), `TokenType.User`. Авторизация — стандартный JWT в заголовках, как у `SendMessage`/`ListMessages`.

---

## 1. Общая модель

- **Сущность**: одна запись на каждое закреплённое сообщение `PinnedMessage { Id, ChatId, MessageId, PinnerUserId, PinnedAt }`.
- **Кто может закреплять/откреплять**: любой участник чата (DM или группа). Системно ни ролей, ни админских прав нет — закреп общий для всех.
- **Лимит**: до **100** закрепов на чат. На 101-м сервер вернёт `TooManyPinnedMessagesException`.
- **Idempotency**: повторный `PinMessage` для уже закреплённого — no-op без события (вернёт текущий `PinnedMessageInfo`). Повторный `UnpinMessage` для не закреплённого — no-op.
- **При закреплении/откреплении**: сервер всегда добавляет в чат **системное сообщение** (`MessageContentType.System` = 2) с текстом `"Пользователь {Имя Фамилия} закрепил сообщение"` / `"… открепил сообщение"` / `"… открепил все сообщения"`. Клиент получает его через обычный канал `NewMessageEvent` — рендерить как системное (как kick-сообщения).
- **Soft-delete**: если закреплённое сообщение удалят через `DeleteMessage`, сервер автоматически удалит закреп и опубликует `MessageUnpinnedEvent`. Отдельный вызов `UnpinMessage` для удалённого сообщения **не нужен**.

---

## 2. RPC

Все методы в `messages_api.proto`, сервис `MessagesApi`. Авторизация — User-токен.

### 2.1. `PinMessage(PinMessageRequest) → PinMessageResponse`

```proto
message PinMessageRequest  { string chat_id = 1; int64 message_id = 2; }
message PinMessageResponse { barkfluff.shared.PinnedMessageInfo pinned = 1; }
```

**Когда вызывать:**
- Пользователь нажимает пункт «Закрепить» в контекстном меню сообщения (long-press на Android/iOS, ПКМ на WPF/Web/Linux/macOS).
- Можно использовать quick-action из reply-карточки или из админ-меню группы.

**Параметры:**
- `chat_id` — Guid чата как строка (тот же формат, что в `ListMessages`).
- `message_id` — int64 ID сообщения.

**Что делать с ответом:**
- Положить `pinned` в локальный кеш закрепов чата на самой первой позиции (sort by `pinned_at DESC`).
- Обновить «закреплённую плашку» в шапке чата (top-bar), если показываете последний закреп.
- НЕ ждать прихода `MessagePinnedEvent` — он придёт позже, нужен только для остальных клиентов и для синхронизации между устройствами одного пользователя.

**Возможные ошибки (gRPC trailer `x-error-code`):**
| ErrorCode | Когда | Что показать пользователю |
|-----------|-------|---------------------------|
| `NoAccessToChatException` | Пользователь не член чата (например, кикнули) | Закрыть чат, показать «Нет доступа» |
| `MessageNotFoundException` | Сообщение не существует, удалено, или принадлежит другому чату | «Сообщение недоступно» |
| `TooManyPinnedMessagesException` (Guid `F7E1A4B8-2C9D-4F3A-B6E7-8D5C1A0F9B23`) | В чате уже 100 закрепов | «Достигнут лимит закреплённых сообщений (100). Открепите старые, чтобы закрепить новое.» |
| `ChatIdNotValidException` | `chat_id` не Guid | Только баг клиента — логировать |

### 2.2. `UnpinMessage(UnpinMessageRequest) → UnpinMessageResponse`

```proto
message UnpinMessageRequest  { string chat_id = 1; int64 message_id = 2; }
message UnpinMessageResponse { }
```

**Когда вызывать:**
- Пункт «Открепить» в контекстном меню сообщения (если оно закреплено).
- Свайп-действие на карточке закрепа в списке закреплённых.
- Кнопка-крестик в top-bar плашке последнего закрепа (Telegram-style).

**Что делать с ответом:**
- Удалить запись из локального кеша закрепов чата по `message_id`.
- Если открепили последний — скрыть плашку в шапке.

**Ошибки:** `NoAccessToChatException`, `ChatIdNotValidException`. `MessageNotFoundException` **не возникает** — несуществующий закреп возвращает обычный `UnpinMessageResponse` (idempotent).

### 2.3. `ListPinnedMessages(ListPinnedMessagesRequest) → ListPinnedMessagesResponse`

```proto
message ListPinnedMessagesRequest {
  string chat_id = 1;
  barkfluff.shared.PageRequest pagination = 2;  // offset + size; size auto-capped to 50
}
message ListPinnedMessagesResponse {
  repeated barkfluff.shared.PinnedMessageInfo pinned = 1;
  int32 total_count = 2;
}
```

**Когда вызывать:**
- При открытии экрана/диалога «Закреплённые сообщения» из меню чата.
- При первом входе в чат — для инициализации top-bar плашки (можно с `size=1`, чтобы получить только последний закреп и `total_count`).
- При pull-to-refresh на экране закрепов.
- НЕ нужно вызывать после собственного `PinMessage`/`UnpinMessage` — кеш обновляйте локально из ответа этих методов.

**Сортировка:** `PinnedAt DESC` (новые закрепы сверху).

**Пагинация:**
- `pagination.offset` — сколько пропустить, `pagination.size` — размер страницы (макс 50, дефолт 50).
- Если `pagination` не задана — сервер вернёт первые 50.
- Для подгрузки следующей страницы инкрементируйте `offset` на размер уже полученного списка.

**Что делать с ответом:**
- Заполнить локальный кеш закрепов чата.
- Сообщения внутри `PinnedMessageInfo.message` — полные `barkfluff.shared.Message` со всеми вложениями и preview-URL'ами; рендерите как обычные сообщения.
- Soft-deleted сообщения автоматически отфильтрованы — `total_count` показывает общее число активных закрепов (без удалённых).
- `pinner_user_id` — рендерить в виде «Закрепил {Имя Фамилия}» — имя подтягивайте из своего user-кеша или `UsersApi.GetById`.

**Ошибки:** `NoAccessToChatException`, `ChatIdNotValidException`.

### 2.4. `UnpinAll(UnpinAllRequest) → UnpinAllResponse`

```proto
message UnpinAllRequest  { string chat_id = 1; }
message UnpinAllResponse { int32 unpinned_count = 1; }
```

**Когда вызывать:**
- Пункт «Открепить все» в меню «Закреплённые сообщения» (с подтверждающим диалогом — это деструктивное действие). Сделайте это пунктом-в-конце-списка с красным цветом.

**Что делать с ответом:**
- Полностью очистить локальный кеш закрепов чата.
- Скрыть top-bar плашку.
- Если хотите — показать toast «Откреплено сообщений: {unpinned_count}».

**Idempotency:** если в чате не было закрепов, вернётся `unpinned_count = 0` без ошибок и без системного сообщения / события.

**Ошибки:** `NoAccessToChatException`, `ChatIdNotValidException`.

---

## 3. Realtime-события (RabbitMQ → Updates)

События приходят в [[Backend/Updates]] и доставляются клиенту через **отдельные стримы** (по аналогии с `SubscribeMessagesEdited`/`SubscribeMessagesDeleted`):

- `UpdatesApi.SubscribeMessagesPinned(SubscribeMessagesPinnedRequest) → stream MessagePinnedEvent`
- `UpdatesApi.SubscribeMessagesUnpinned(SubscribeMessagesUnpinnedRequest) → stream MessageUnpinnedEvent`
- `UpdatesApi.SubscribeAllMessagesUnpinned(SubscribeAllMessagesUnpinnedRequest) → stream AllMessagesUnpinnedEvent`

Авторизация — User-токен. Клиент должен открыть три долгоживущих стрима (как для других подписок) и обрабатывать сообщения, сравнивая `chat_id` с активными чатами. Контракты queue-событий — в `Shared/BarkFluff.Shared.Queue/Messages/`, proto-сообщения — в `Shared/BarkFluff.Proto/updates_api.proto`.

### 3.1. `MessagePinnedEvent`

Proto (`updates_api.proto`):
```proto
message MessagePinnedEvent {
  string chat_id = 1;
  int64 message_id = 2;
  int64 pinner_user_id = 3;
  google.protobuf.Timestamp pinned_at = 4;
}
```

Внутреннее queue-событие (`Shared/BarkFluff.Shared.Queue/Messages/MessagePinnedEvent.cs`):
```csharp
public class MessagePinnedEvent {
    public Guid ChatId;
    public List<long> ChatMembers;       // кому раздавать (только сервер)
    public long MessageId;
    public long PinnerUserId;
    public DateTime PinnedAt;
}
```

**Действие клиента:**
- Если активен этот чат — добавить закреп в локальный кеш на первую позицию.
- Обновить top-bar плашку.
- Если событие пришло от другого устройства того же пользователя (`PinnerUserId == myUserId`) — это синхронизация: применять как обычно.

### 3.2. `MessageUnpinnedEvent`

Proto:
```proto
message MessageUnpinnedEvent {
  string chat_id = 1;
  int64 message_id = 2;
}
```

Queue-событие:
```csharp
public class MessageUnpinnedEvent {
    public Guid ChatId;
    public List<long> ChatMembers;
    public long MessageId;
}
```

**Действие клиента:**
- Удалить запись по `MessageId` из локального кеша.
- Скрыть плашку, если она показывала именно этот закреп.

> Это же событие приходит, когда закреплённое сообщение **soft-удаляют** через `DeleteMessage` — клиент получит **сначала** `MessageDeletedEvent`, **затем** `MessageUnpinnedEvent`. Обрабатывайте оба независимо: первый убирает сообщение из ленты, второй — из списка закрепов.

### 3.3. `AllMessagesUnpinnedEvent`

Proto:
```proto
message AllMessagesUnpinnedEvent {
  string chat_id = 1;
}
```

Queue-событие:
```csharp
public class AllMessagesUnpinnedEvent {
    public Guid ChatId;
    public List<long> ChatMembers;
}
```

**Действие клиента:**
- Полностью очистить локальный кеш закрепов чата.

### 3.4. Системные сообщения (`NewMessageEvent` с `MessageContentType.System`)

При каждом pin/unpin/unpin-all сервер дополнительно отправляет в чат **системное сообщение** (тот же механизм, что у `KickUser`):

| Действие | Текст |
|----------|-------|
| `PinMessage` | `Пользователь {имя фамилия} закрепил сообщение` |
| `UnpinMessage` | `Пользователь {имя фамилия} открепил сообщение` |
| `UnpinAll` | `Пользователь {имя фамилия} открепил все сообщения` |

**Действие клиента:**
- Рендерить как обычное системное сообщение по тем же правилам, что используются для kick-сообщений (`MessageContentType.System` = 2).
- Это сообщение появляется в ленте чата у всех участников.
- Это **отдельное** сообщение от pin-события: оно приходит как `NewMessageEvent` и должно быть добавлено в ленту чата. Pin-событие (`MessagePinnedEvent`) обновляет только список закрепов.

---

## 4. UI-сценарии

### 4.1. Контекстное меню сообщения

В меню сообщения добавить:
- **«Закрепить»** — если сообщение НЕ закреплено сейчас. Видно **всем участникам** чата (любой может закреплять).
- **«Открепить»** — если сообщение УЖЕ закреплено. Видно всем участникам.

Чтобы знать состояние, держите в памяти `Set<long> pinnedMessageIds` для активного чата (наполняется из `ListPinnedMessages` при входе и из realtime-событий).

Не показывайте «Закрепить» для:
- Системных сообщений (`type == MessageContentType.System`) — формально сервер позволит, но это бессмысленно.
- Soft-deleted сообщений — они и так не должны быть в ленте.

### 4.2. Top-bar плашка с последним закрепом (Telegram-style)

Над чатом — компактная плашка:
- Аватар/превью сообщения слева
- Имя автора оригинала + 1-2 строки текста
- Крестик «открепить» справа (вызывает `UnpinMessage` для этого сообщения)
- Если закрепов > 1 — счётчик «1 / N» и тап-навигация между ними

**Источник данных:** последний элемент `pinned[0]` из `ListPinnedMessages` (sort `PinnedAt DESC`) + `total_count`.

При тапе на плашку — скролл к оригинальному сообщению (по `message.id`). Если оригинал не в текущей загруженной истории — вызвать `ListMessages` с `from_message_id = pinned.message.id`.

### 4.3. Экран «Закреплённые сообщения»

Полноэкранный список (или диалог) — открывается из меню чата:
- Список карточек: оригинальное сообщение + строка «Закрепил {имя} · {pinned_at}»
- Тап → скролл к сообщению в чате
- Long-press / swipe → открепить
- В правом верхнем углу — пункт «Открепить все» с подтверждением (`UnpinAll`)

Источник: `ListPinnedMessages` с пагинацией (50 за раз).

---

## 5. Локальный кеш и состояние

Минимальная структура для каждого чата:

```
ChatPinnedState {
  Map<long, PinnedMessageInfo> byMessageId   // быстрый lookup для контекстного меню
  List<PinnedMessageInfo> sorted             // sort by PinnedAt DESC, для UI
  int totalCount                             // из ListPinnedMessages
}
```

Триггеры обновления:
- Вход в чат: `ListPinnedMessages(chatId, size=1)` → если `total_count > 0`, показать плашку. Полную загрузку списка закрепов делайте лениво при открытии экрана закрепов.
- `PinMessage` ответ → добавить в `byMessageId` и в начало `sorted`, `totalCount++`
- `UnpinMessage` ответ → удалить из обоих, `totalCount--`
- `UnpinAll` ответ → очистить, `totalCount = 0`
- `MessagePinnedEvent` (если этот чат активен) → то же что `PinMessage` ответ
- `MessageUnpinnedEvent` → то же что `UnpinMessage` ответ
- `AllMessagesUnpinnedEvent` → очистить
- `MessageDeletedEvent` для закреплённого `MessageId` → клиент **не должен** делать ничего особого с закрепом самостоятельно: следом придёт `MessageUnpinnedEvent`. Но защититься от рассинхрона — удалить из кеша при получении любого из двух — безопасно.

---

## 6. Маппинг данных

### 6.1. `PinnedMessageInfo`

```proto
message PinnedMessageInfo {
  Message message = 1;             // полное сообщение со всеми вложениями
  int64 pinner_user_id = 2;        // кто закрепил
  google.protobuf.Timestamp pinned_at = 3;
}
```

`pinner_user_id` — long. Имя/аватар берите из user-кеша (тот же, что для отправителей сообщений) или через `UsersApi.GetById` если не закеширован.

`pinned_at` — UTC timestamp; форматируйте по локали пользователя.

### 6.2. Где взять имя `pinner_user_id`

Тот же путь, что для `Message.sender_id`:
- Локальный user-кеш по userId.
- Fallback: `UsersApi.GetById(userId)` (см. [[Backend/Users]] proto `users_api.proto`).
- Подписка на `user-changed-name-*` для обновления, как для обычных сообщений.

---

## 7. Тест-чеклист для клиента

- [ ] Закрепить → плашка появилась, в ленте появилось системное сообщение
- [ ] Закрепить уже закреплённое (двойной тап) → no-op, нет дубля
- [ ] Открепить → плашка скрылась/обновилась, в ленте системное сообщение
- [ ] Открепить не закреплённое (race-condition) → no-op без ошибки
- [ ] Закрепить 100 + 1-е → toast «Достигнут лимит» (`TooManyPinnedMessagesException`)
- [ ] Удалить закреплённое сообщение через DeleteMessage → исчезает и из ленты, и из закрепов
- [ ] Войти в чат, где уже есть закрепы (закрепил другой пользователь) → плашка отрисована
- [ ] Получить `MessagePinnedEvent` от другого устройства → закреп появляется без явного вызова
- [ ] `UnpinAll` с подтверждением → все закрепы исчезли, одно системное сообщение в ленте
- [ ] Тап на плашку с закрепом, который не в текущей загруженной истории → подгрузка через `ListMessages(from_message_id=…)`
- [ ] Кика из чата → закрепы недоступны (`NoAccessToChatException` при попытке `Pin`/`Unpin`/`List`)

---

## 8. Связанные файлы

- Proto: `Shared/BarkFluff.Proto/messages_api.proto`, `Shared/BarkFluff.Proto/shared.proto`
- Бэкенд handlers: `Backend/BarkFluff.Messages/Features/{PinMessage,UnpinMessage,ListPinnedMessages,UnpinAll}/`
- События: `Shared/BarkFluff.Shared.Queue/Messages/{MessagePinnedEvent,MessageUnpinnedEvent,AllMessagesUnpinnedEvent}.cs`
- Исключение: `Shared/BarkFluff.Shared.Exceptions/Messages/TooManyPinnedMessagesException.cs`
- Документация: [[Backend/Messages]], [[Backend/Updates]], [[Backend/Messages-Metrics]]
