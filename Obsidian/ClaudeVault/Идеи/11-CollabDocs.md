# 📝 Совместное редактирование документов (BarkFluff Docs)

> Категория: Продуктивность
> Приоритет: 🟡 Средний
> Сложность: ⭐⭐⭐⭐⭐

---

## Описание

Встроенный **коллаборативный текстовый редактор** прямо в мессенджере — без необходимости переходить в Google Docs или Notion. Несколько участников чата могут одновременно редактировать документ, видя изменения в реальном времени.

---

## Ключевые возможности

- Создание документа из чата (кнопка «Прикрепить документ»)
- Real-time совместное редактирование (CRDT или OT алгоритм)
- Курсоры разных участников с разными цветами и именами
- Базовый Markdown (жирный, курсив, заголовки, списки, таблицы, код)
- История версий (кто что изменил, когда)
- Экспорт в PDF / .md / .docx
- Документ прикреплён к чату и доступен через вкладку «Файлы чата»
- Упоминания участников @username внутри документа
- Встраивание изображений (из Minio)
- Комментарии к выделенному тексту

---

## Архитектура

```
Новый микросервис: BarkFluff.Docs (порт 7055)
     │
     ├── PostgreSQL — метаданные документов и история версий
     ├── Redis — CRDT состояние активного документа (Y.js / Automerge структура)
     ├── Minio — хранение снапшотов документа (JSON)
     └── gRPC streaming — синхронизация операций между клиентами
```

### Алгоритм синхронизации

**CRDT (Conflict-free Replicated Data Type)** через `Y.js` протокол:
- Каждое изменение = `YOperation` (insert/delete с позицией и метаданными)
- Сервер как relay: получает операцию от клиента A → рассылает всем остальным участникам через gRPC stream
- Снапшот сохраняется каждые 30 операций или 60 секунд

### gRPC методы

```protobuf
rpc CreateDocument(CreateDocumentRequest) returns (DocumentResponse);
rpc OpenDocument(OpenDocumentRequest) returns (stream DocumentSyncEvent);
rpc SendOperation(SendOperationRequest) returns (Empty);   // client → server
rpc GetDocumentHistory(GetDocumentHistoryRequest) returns (DocumentHistoryResponse);
rpc ExportDocument(ExportDocumentRequest) returns (ExportResponse);
```

---

## Изменения в существующих сервисах

| Сервис | Изменение |
|--------|-----------|
| [[../Backend/Messages]] | Новый тип вложения `DOCUMENT_SHARED` со ссылкой на документ |
| [[../Backend/Files]] | Хранение изображений внутри документов и экспортируемых PDF |
| [[../Shared/Proto]] | `docs.proto` |

---

## Клиентская реализация

- Android: `RichTextEditor` на базе Compose/Spannable; WebSocket/gRPC streaming
- WPF: кастомный richtextbox или интеграция Monaco Editor через WebView2
- Web: стандартный `Y.js` + CodeMirror / ProseMirror (для клиента `BarkFluff.Web`)

---

## UX

- Вкладка «Docs» внутри группового чата
- Иконка документа в списке сообщений (как превью вложения)
- Полноэкранный режим редактора
- Панель инструментов: B / I / U / H1-H3 / Список / Код / Таблица / Изображение
- Индикаторы присутствия: аватары коллег в шапке редактора
- «N пользователей сейчас редактируют» — живой счётчик
