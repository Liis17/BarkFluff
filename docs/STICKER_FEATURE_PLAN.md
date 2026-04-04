# План реализации поддержки стикеров в WPF-клиенте

## Ветка
`copilot/add-sticker-button-in-chat` (от `dev`)

## Цель
Добавить полноценную поддержку стикеров в мессенджер WPF-клиента BarkFluff:
- просмотр и выбор стикеров из панели
- отправка стикера в чат
- корректное отображение входящих и исходящих стикер-сообщений

---

## Шаги выполнения

### 1. API-слой — `WebApiFileManager.cs` / `WebApi.cs`
- Добавлен метод `ListStickerPacksAsync` — получает постраничный список стикерпаков через gRPC `FilesApi.ListStickerPacks`
- Добавлен метод `GetStickerPackAsync` — получает стикерпак вместе с его стикерами через `FilesApi.GetStickerPack`
- Методы проксированы через `WebApi.cs` аналогично остальным файловым операциям

### 2. Отображение стикеров — `StickerMessageContent.xaml/.cs`
- Создан новый UserControl `StickerMessageContent`
- Отображает стикер 300×300 пикселей через существующий `CachedImage` (с кешированием)
- Прозрачный фон — поддерживает стикеры с прозрачностью (WebP, PNG)

### 3. Тип сообщения и стиль — `MessageBubble.xaml/.cs`
- Добавлено значение `Sticker` в enum `MessageType`
- Добавлен стиль `StickerMessageStyle`: прозрачный фон, без рамки, без тени
- Добавлен метод `SetupStickerContent` — подключает `StickerMessageContent` к пузырю без «облачка»
- Метод `ThemedConfirm` обновлён: для стикеров применяется прозрачный стиль

### 4. Маппинг типов — `MessengerPage.xaml.cs`
- В методах `GetMessageType`, `DetermineAttachmentType`, `GetMessageTypeFromAttachment` добавлен кейс `Sticker`
- Входящие стикер-сообщения теперь корректно определяются и отображаются

### 5. Панель выбора стикеров — `StickerPicker.xaml/.cs`
- Создан новый UserControl `StickerPicker` (310×500 px, всплывающая панель)
- Содержит `ScrollViewer` с вертикальным списком секций по стикерпакам
- Каждая секция: иконка обложки + название пака, ниже сетка 3×N с кнопками стикеров
- Паки загружаются лениво при первом открытии (результат кешируется в памяти)
- При нажатии на стикер генерируется событие `StickerSelected` с `FileId`, `FileUrl`, `PreviewUrl`

### 6. Интеграция в интерфейс — `MessengerPage.xaml`
- В грид панели ввода добавлена новая колонка
- Между полем ввода текста и кнопкой отправки добавлена кнопка стикеров (символ `Sticker16`)
- К кнопке привязан `Popup`, внутри которого размещён `StickerPicker`

### 7. Обработчики событий — `MessengerPage.xaml.cs`
- `StickerButton_Click` — переключает видимость popup, запускает ленивую загрузку паков
- `OnStickerSelected` (async) — закрывает панель, вызывает `SendStickerAsync`
- `SendStickerAsync` — немедленно добавляет стикер-пузырь в UI (оптимистичный рендеринг), затем отправляет сообщение через существующий `SendMessage` API; ошибки выводятся через `ErideMessage`

---

## Затронутые файлы

| Файл | Изменение |
|------|-----------|
| `Windows/BarkFluff.WebApi.Core/Managers/WebApiFileManager.cs` | Новые методы `ListStickerPacksAsync`, `GetStickerPackAsync` |
| `Windows/BarkFluff.WebApi.Core/WebApi.cs` | Проксирование новых методов |
| `Windows/BarkFluff.Client.WPF/UserControls/MessageContent/StickerMessageContent.xaml` | **Новый файл** — отображение стикера |
| `Windows/BarkFluff.Client.WPF/UserControls/MessageContent/StickerMessageContent.xaml.cs` | **Новый файл** — код-бихайнд |
| `Windows/BarkFluff.Client.WPF/UserControls/MessageBubble.xaml` | Стиль `StickerMessageStyle` |
| `Windows/BarkFluff.Client.WPF/UserControls/MessageBubble.xaml.cs` | `MessageType.Sticker`, `SetupStickerContent`, обновлён `ThemedConfirm` |
| `Windows/BarkFluff.Client.WPF/UserControls/StickerPicker.xaml` | **Новый файл** — разметка панели |
| `Windows/BarkFluff.Client.WPF/UserControls/StickerPicker.xaml.cs` | **Новый файл** — логика панели |
| `Windows/BarkFluff.Client.WPF/Pages/MessengerPage.xaml` | Кнопка стикеров + Popup |
| `Windows/BarkFluff.Client.WPF/Pages/MessengerPage.xaml.cs` | Маппинг типов, обработчики, `SendStickerAsync` |

---

## Результат сборки
Проект `BarkFluff.Client.WPF` успешно собирается без ошибок.
