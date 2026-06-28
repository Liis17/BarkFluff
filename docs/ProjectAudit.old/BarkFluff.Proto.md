# Аудит проекта: BarkFluff.Proto

> **Дата:** 2026-05-18
> **Последняя проверка:** 2026-05-18
> **Ревьюер:** GitHub Copilot (BarkfluffAgent)
> **Область:** `Shared\BarkFluff.Proto\` — все `.proto`-файлы контрактов gRPC-сервисов
> **Версия proto:** proto3
> **Target Framework:** net9.0

---

## 🔴 Безопасность

---



### SEC-02 — `GenerateTestTokenRequest` находится в production-контракте

> ⚠️ **Статус (2026-05-18):** Частично актуальна. Сообщения GenerateTestTokenRequest/Response остаются в identity_api.proto:225-231, но RPC-метод не объявлен ни в IdentityApi, ни в IdentityServerApi — мёртвый код.

**Проблема:**
В `identity_api.proto` объявлено сообщение `GenerateTestTokenRequest` / `GenerateTestTokenResponse`. Это тестовый RPC, позволяющий получить токен для **любого** `user_id` без авторизации. Если сервер не закрывает этот метод на уровне middleware/authorization policy — это критическая уязвимость повышения привилегий.

**Файл:** `Shared\BarkFluff.Proto\identity_api.proto` : строки 225–231

```protobuf
message GenerateTestTokenRequest {
  int64 user_id = 1; // ⚠️ любой user_id → любой токен без аутентификации
}

message GenerateTestTokenResponse {
  string token = 1;
}
```

**Варианты решения:**

1. **Удалить** сообщение и реализацию целиком в production-сборке

### 

### SEC-05 — `ip_address` приходит из клиентских заголовков в `FastAuth` и `CreateSessionForUserServer`

> ✅ **Статус (2026-05-18):** Актуальна.

**Проблема:**
В `CreateSessionForUserServerRequest` поле `ip_address` передаётся как аргумент вызывающего сервиса. Если Identity не валидирует и не перезаписывает IP из gRPC peer — возможна подмена IP для обхода geo-блокировок или rate-limit политик.

**Файл:** `Shared\BarkFluff.Proto\identity_api.proto` : строки 80–87

```protobuf
message CreateSessionForUserServerRequest {
  int64 user_id = 1;
  string device_id = 2;
  string device_name = 3;
  string operation_system = 4;
  string app_name = 5;
  string ip_address = 6; // ⚠️ доверяем вызывающему — можно подделать
}
```

**Варианты решения:**

1. В реализации Identity: всегда брать IP из `ServerCallContext.Peer`, а не из поля сообщения.
2. Поле `ip_address` в proto — оставить как опциональное для случаев, когда Identity сам не может определить IP (например, WebApi → Identity цепочка), но документировать это явно.
3. Добавить комментарий-контракт: сервер обязан валидировать или игнорировать это поле.

```protobuf
message CreateSessionForUserServerRequest {
  int64 user_id = 1;
  string device_id = 2;
  string device_name = 3;
  string operation_system = 4;
  string app_name = 5;
  // Передаётся вызывающим сервисом (WebApi), т.к. на уровне gRPC peer
  // виден адрес WebApi, а не оригинального клиента.
  // Identity обязан валидировать формат (IPv4/IPv6), но не должен слепо доверять значению.
  string ip_address = 6;
}
```

---

## 🟡 Оптимизация

---

### OPT-01 — `GetUserAllMessages` возвращает все сообщения разом без пагинации/стриминга

> ✅ **Статус (2026-05-18):** Актуальна.

**Проблема:**
Метод экспорта `MessagesServerApi.GetUserAllMessages` возвращает **все** сообщения пользователя и **все** чаты за одну унарную gRPC-операцию. При большом числе сообщений (тысячи/десятки тысяч) это приводит к огромным сообщениям gRPC (дефолтный лимит 4 MB), Out of Memory на сервере при сборке ответа и длительному ожиданию клиента.

**Файл:** `Shared\BarkFluff.Proto\messages_api.proto` : строки 452–463

```protobuf
// ⚠️ Унарный вызов, возвращает всё одним куском — OOM-риск
rpc GetUserAllMessages(GetUserAllMessagesRequest) returns(GetUserAllMessagesResponse);

message GetUserAllMessagesResponse {
  repeated ExportMessage messages = 1; // может быть 100k+ сообщений
  repeated ExportChat chats = 2;
}
```

**Варианты решения:**

1. Заменить на server-side streaming — сервер стримит батчами по N сообщений.
2. Добавить пагинацию: `page_token` / `offset` + `limit`.

```protobuf
// Вариант 1 — server-side streaming (рекомендуется для экспорта)
rpc GetUserAllMessages(GetUserAllMessagesRequest) returns (stream ExportBatch);

message ExportBatch {
  repeated ExportMessage messages = 1; // батч по 100–500 сообщений
  repeated ExportChat chats = 2;       // возвращаются один раз в первом батче
  bool is_last = 3;                    // признак последнего батча
}

// Вариант 2 — пагинация
message GetUserAllMessagesRequest {
  int64 user_id = 1;
  int32 page_size = 2;    // макс 500
  string page_token = 3;  // cursor-токен следующей страницы
}

message GetUserAllMessagesResponse {
  repeated ExportMessage messages = 1;
  repeated ExportChat chats = 2;
  string next_page_token = 3; // пустой — данных больше нет
}
```

---



### OPT-04 — `ListMessages` содержит устаревшее поле `count` вместе с новыми `offset_before`/`offset_after`

> ✅ **Статус (2026-05-18):** Актуальна.

**Проблема:**
В `ListMessagesRequest` есть поле `count` с пометкой `deprecated` в комментарии, но оно не помечено как `[deprecated = true]` в proto. Это означает, что кодогенератор не предупреждает клиентов об устаревании, а оба механизма пагинации могут работать одновременно с непредсказуемым приоритетом.

**Файл:** `Shared\BarkFluff.Proto\messages_api.proto` : строки 230–241

```protobuf
message ListMessagesRequest {
  int64 from_message_id = 1;
  int32 count = 2;          // deprecated, use offset_before — но НЕ помечено [deprecated=true]
  string chat_id = 3;
  int32 offset_before = 4;
  int32 offset_after = 5;
}
```

**Варианты решения:**

Пометить поле официальным атрибутом deprecated в proto3 (через опцию или резервирование) и задокументировать поведение приоритета.

```protobuf
message ListMessagesRequest {
  int64 from_message_id = 1;

  // Устарело. Используйте offset_before.
  // При наличии offset_before это поле игнорируется.
  int32 count = 2 [deprecated = true];

  string chat_id = 3;

  // Количество сообщений ПЕРЕД from_message_id (старше), макс 50.
  int32 offset_before = 4;

  // Количество сообщений ПОСЛЕ from_message_id (новее), макс 50.
  int32 offset_after = 5;
}
```
