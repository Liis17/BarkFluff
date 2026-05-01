# 🔍 Умный поиск по всему мессенджеру

> Категория: Поиск
> Приоритет: 🟡 Средний
> Сложность: ⭐⭐⭐⭐

---

## Описание

**Глобальный поиск** по всему контенту мессенджера: сообщениям, файлам, пользователям, чатам, каналам. С поддержкой **full-text search** (русский + английский морфологический разбор), фильтрацией и умными подсказками.

---

## Ключевые возможности

- Поиск по тексту сообщений (в конкретном чате или глобально)
- Поиск по имени пользователя / @username / display name
- Поиск файлов (по имени, типу: фото / видео / документы)
- Поиск по каналам и группам
- Фильтры: тип контента, дата, конкретный чат
- Поиск с опечатками (fuzzy search)
- История поиска (локально)
- Переход к нужному сообщению в контексте чата (scroll to message)

---

## Архитектура

```
Новый микросервис: BarkFluff.Search (порт 7040)
     │
     ├── Elasticsearch / Meilisearch (self-hosted) — индекс сообщений
     ├── PostgreSQL full-text (pg_trgm + tsvector) — для простого варианта
     └── Redis — автокомплит пользователей и чатов
```

### Индексирование

- При создании сообщения → RabbitMQ событие `MessageCreated` → Search consumer индексирует
- При удалении → `MessageDeleted` → удаление из индекса
- Индекс сообщений: `message_id`, `chat_id`, `sender_id`, `text`, `created_at`
- **E2EE сообщения [[04-E2EEncryption]] не индексируются** (только клиентский поиск)
- Индекс файлов: `file_id`, `original_name`, `mime_type`, `chat_id`, `uploaded_at`

### gRPC методы

```protobuf
rpc GlobalSearch(SearchRequest) returns (SearchResponse);
rpc SearchInChat(SearchInChatRequest) returns (SearchInChatResponse);
rpc SearchUsers(SearchUsersRequest) returns (SearchUsersResponse);
rpc SearchFiles(SearchFilesRequest) returns (SearchFilesResponse);
```

---

## Изменения в существующих сервисах

| Сервис | Изменение |
|--------|-----------|
| [[../Backend/Messages]] | Публикует `MessageCreated`/`MessageDeleted` в RabbitMQ (если ещё нет) |
| [[../Backend/Files]] | Публикует метаданные файла для индексирования |
| [[../Shared/Proto]] | `search.proto` |
| [[../Shared/Queue]] | `MessageIndexEvent`, `FileIndexEvent` |

---

## UI

- Android: строка поиска в шапке `MainActivity` → полноэкранный `SearchActivity`
- WPF: `Ctrl+F` — поиск в чате, `Ctrl+K` — глобальный поиск
- Результаты разбиты по категориям: «Сообщения», «Люди», «Медиа», «Файлы»
- Подсветка найденного слова в результате
- Infinite scroll результатов
