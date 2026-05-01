# 🌟 BarkFluff Stories — временный контент

> Категория: Контент
> Приоритет: 🟡 Средний
> Сложность: ⭐⭐⭐

---

## Описание

**Stories** — временные публикации, которые автоматически исчезают через 24 часа. Пользователь публикует фото, видео или текст со стикерами — друзья и подписчики видят их в горизонтальной полосе вверху экрана. Механика знакома из Instagram и Telegram.

---

## Ключевые возможности

- Публикация фото и видео (до 60 сек) как Story
- Текстовые Story (цветной фон + текст)
- Стикеры на Story: @упоминания, опросы, эмодзи, ссылки
- Приватность: все / только контакты / выбранные люди
- Просмотр кто видел Story (список с аватарами)
- Ответить на Story → создаёт личный чат с автором
- Архив Stories (личный, недоступен публично)
- Музыкальный трек на Story (если будет интеграция с музыкой)
- Выделенные Stories (Highlights) — не исчезают, закреплены в профиле

---

## Архитектура

```
Новый микросервис: BarkFluff.Stories (порт 7045)
     │
     ├── PostgreSQL — метаданные (user_id, media_id, expires_at, views)
     ├── Minio (Files сервис) — хранение медиа Story
     └── Redis — кеш «непросмотренных» историй
```

### Жизненный цикл Story

```
Создание → хранится 24 часа → BackgroundService удаляет после expires_at
                            → медиафайл удаляется из Minio (или архивируется)
```

### gRPC методы

```protobuf
rpc CreateStory(CreateStoryRequest) returns (StoryResponse);
rpc GetStoriesFeed(GetStoriesFeedRequest) returns (StoriesFeedResponse);
rpc MarkStoryViewed(MarkStoryViewedRequest) returns (Empty);
rpc DeleteStory(DeleteStoryRequest) returns (Empty);
rpc GetStoryViewers(GetStoryViewersRequest) returns (StoryViewersResponse);
```

---

## Изменения в существующих сервисах

| Сервис | Изменение |
|--------|-----------|
| [[../Backend/Files]] | Загрузка медиа Story (временный флаг, чистка после удаления Story) |
| [[../Backend/Updates]] | Событие `NewStoryPublished` → подписчики видят кружок у аватара |
| [[../Shared/Proto]] | `stories.proto` |

---

## UI

- Горизонтальная полоса аватаров с кольцом (непросмотренное = градиентное кольцо) вверху экрана чатов
- Просмотр Story — вертикальный fullscreen, свайп влево/вправо между Story разных людей
- Прогресс-бар вверху (N сегментов = N историй пользователя)
- Нажать и удержать — пауза
- Редактор Story: Canvas с наложением стикеров, текста, рисования

---

## Хранение и очистка

- `expires_at = created_at + 24h`
- BackgroundService в Stories сервисе удаляет по расписанию (каждые 5 мин)
- Для архива: отдельная таблица `story_archives` без удаления
