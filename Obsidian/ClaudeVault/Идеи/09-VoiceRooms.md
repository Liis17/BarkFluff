# 🎙️ Голосовые комнаты (BarkFluff Spaces)

> Категория: Коммуникации
> Приоритет: 🟡 Средний
> Сложность: ⭐⭐⭐⭐

---

## Описание

**Голосовые комнаты** — открытые аудио-пространства, к которым может подключиться любой желающий (или только приглашённые). Аналог Twitter/X Spaces и Clubhouse. Несколько спикеров, слушатели могут поднять руку и попросить слово.

---

## Ключевые возможности

- Создание голосовой комнаты (публичная / по ссылке / только для подписчиков канала)
- Роли: Host, Co-Host, Speaker, Listener
- «Поднять руку» — запрос на право говорить
- Приглашение слушателя на сцену (в спикеры)
- Выключить микрофон участника (модерация)
- Запись комнаты (с уведомлением участников)
- Live счётчик слушателей
- Поделиться ссылкой-приглашением в комнату
- Комнаты в группах/каналах ([[06-Channels]])
- Emoji-реакции в реальном времени (анимация «летящих» эмодзи)

---

## Архитектура

```
BarkFluff.Spaces (новый сервис, порт 7050)
     │
     ├── SFU-сервер (LiveKit / mediasoup) — аудио микширование
     ├── PostgreSQL — метаданные комнаты (создатель, участники, статус)
     ├── Redis — список онлайн участников (sorted set по join_time)
     └── RabbitMQ — события (UserJoined, UserLeft, HandRaised, SpeakerAdded)
```

### Жизненный цикл

```
Host создаёт комнату → open room
Участники подключаются (WebRTC audio-only)
Host завершает → комната удаляется → опционально публикуется запись
```

### Совместное использование с звонками

Повторно использует SFU-инфраструктуру из [[01-Calls]] — разница только в логике управления ролями и масштабе (100+ участников).

### gRPC методы

```protobuf
rpc CreateSpace(CreateSpaceRequest) returns (SpaceResponse);
rpc JoinSpace(JoinSpaceRequest) returns (JoinSpaceResponse);   // возвращает WebRTC credentials
rpc LeaveSpace(LeaveSpaceRequest) returns (Empty);
rpc RaiseHand(RaiseHandRequest) returns (Empty);
rpc PromoteToSpeaker(PromoteToSpeakerRequest) returns (Empty);
rpc GetActiveSpaces(GetActiveSpacesRequest) returns (SpacesListResponse);
```

---

## Изменения в существующих сервисах

| Сервис | Изменение |
|--------|-----------|
| [[../Backend/Updates]] | События: SpaceStarted, UserJoinedSpace, SpaceEnded |
| [[../Backend/CloudMessaging]] | Push «Комната началась» подписчикам канала |
| [[../Shared/Proto]] | `spaces.proto` |

---

## UI

- Карточка активной комнаты в верхней части списка чатов / в ленте канала
- Экран комнаты: аватары спикеров крупно, слушатели — мелкая сетка снизу
- Кнопки: 🎤 Микрофон / ✋ Рука / 🚪 Выйти
- Анимированные волны вокруг аватара говорящего
- Уведомление «Идёт прямой эфир» в профиле хоста
