# План: Общие файлы чата (Shared Media)

## Обзор

Функция просмотра общих файлов чата - медиа (фото, видео) и документы. Отображается в профиле пользователя/группы.

## Связанные планы

- **USER_PROFILE_PLAN.md** - Просмотр профиля (интеграция Shared Media)
- **MESSENGER_PLAN.md** - Мессенджер

---

## Статус: В РАЗРАБОТКЕ

Базовая структура виджетов реализована. Требуется подключение к backend API.

---

## Этап 1: Backend API

### Proto (messages_api.proto)

Требуется добавить endpoint для получения вложений чата:

```protobuf
// Получить общие медиа/файлы чата
rpc GetSharedMedia(GetSharedMediaRequest) returns(GetSharedMediaResponse);

message GetSharedMediaRequest {
  string chat_id = 1;
  SharedMediaFilter filter = 2;  // MEDIA / DOCUMENTS
  int32 offset = 3;
  int32 size = 4;  // макс 50
}

message GetSharedMediaResponse {
  repeated SharedMediaItem items = 1;
  bool has_more = 2;
}

message SharedMediaItem {
  string message_id = 1;
  google.protobuf.Timestamp sent_at = 2;
  MessageAttachment attachment = 3;  // Существующий тип
}

enum SharedMediaFilter {
  MEDIA = 0;      // Фото, видео
  DOCUMENTS = 1;  // Файлы
}
```

---

## Этап 2: Qt Client

### 2.1 Модели

```cpp
// Models/SharedMediaItem.h
enum class SharedMediaFilter {
    Media,
    Documents
};

struct SharedMediaItem {
    QString messageId;
    QDateTime sentAt;
    MessageAttachment attachment;
};

struct SharedMediaResult {
    QList<SharedMediaItem> items;
    bool hasMore = false;
};
```

### 2.2 Client

```cpp
// Connection/MessagesClient.h
SharedMediaResult getSharedMedia(
    const QString& chatId,
    SharedMediaFilter filter,
    qint32 offset,
    qint32 size,
    const QString& accessToken
);
```

### 2.3 Виджеты

#### SharedMediaSection
- Заголовок "Вложения"
- Segmented control: [Медиа] [Файлы]
- Сетка превью или список файлов

#### SharedMediaGridView
- QGridLayout с превью медиа (3 колонки)
- Ленивая загрузка при скролле
- Клик открывает FullScreenMediaViewer

#### SharedDocumentsListView
- QListWidget с иконками файлов
- Имя файла, размер, дата
- Клик для скачивания/открытия

---

## Этап 3: UI Layout

```
┌─────────────────────────────────────┐
│ Вложения                            │
│ ┌─────────┬─────────┐               │
│ │  Медиа  │  Файлы  │  ← Tab Bar    │
│ └─────────┴─────────┘               │
├─────────────────────────────────────┤
│ ┌─────┐ ┌─────┐ ┌─────┐             │
│ │     │ │     │ │     │  ← Grid     │
│ │     │ │     │ │     │             │
│ └─────┘ └─────┘ └─────┘             │
│ ┌─────┐ ┌─────┐ ┌─────┐             │
│ │     │ │     │ │     │             │
│ │     │ │     │ │     │             │
│ └─────┘ └─────┘ └─────┘             │
│                                     │
│ Загрузка...                         │
│                                     │
└─────────────────────────────────────┘
```

---

## Чек-лист

### Backend
- [ ] Добавить GetSharedMedia в messages_api.proto
- [ ] Реализовать сервис в Messages микросервисе

### Qt Client
- [ ] Модель SharedMediaItem
- [ ] MessagesClient::getSharedMedia()
- [ ] SharedMediaSection (контейнер)
- [ ] SharedMediaGridView (превью)
- [ ] SharedDocumentsListView (файлы)
- [ ] Пагинация при скролле
- [ ] Интеграция в UserProfileView

---

## Файлы

### Новые
- `BarkFluffQt/src/Models/SharedMediaItem.h`
- `BarkFluffQt/src/UI/Widgets/SharedMediaSection.h`
- `BarkFluffQt/src/UI/Widgets/SharedMediaSection.cpp`
- `BarkFluffQt/src/UI/Widgets/SharedMediaGridView.h`
- `BarkFluffQt/src/UI/Widgets/SharedMediaGridView.cpp`
- `BarkFluffQt/src/UI/Widgets/SharedDocumentsListView.h`
- `BarkFluffQt/src/UI/Widgets/SharedDocumentsListView.cpp`

### Изменяемые
- `BarkFluffQt/src/Connection/MessagesClient.h`
- `BarkFluffQt/src/Connection/MessagesClient.cpp`
- `BarkFluffQt/src/UI/UserProfileView.cpp` - интеграция