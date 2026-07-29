# Функции клиентов BarkFluff — матрица реализации

Дата составления: 2026-07-29.

Цель документа — свести в одно место, **какая функция в каком клиенте реализована**, чтобы сформировать
объём работ для нового Windows-клиента `BarkFluff.ClientV2.WPF`.

## Источники

Документ собран по Obsidian-хранилищу (актуальная база знаний проекта) + точечной проверке кода:

| Клиент | Расположение | Документация |
|---|---|---|
| Android V1 | `Android/Barkfluff.Client.Android` (`:app-v1` + `:core`) | `Obsidian/ClaudeVault/Клиенты/Android.md` |
| Android V2 | `Android/Barkfluff.ClientV2.Android` (`:app-v2`) | `Клиенты/Android-V2.md` |
| iOS | `iOS/Barkfluff` | `Клиенты/iOS.md` |
| macOS | `Mac/Barkfluff` | `Клиенты/macOS.md` |
| Web | `Backend/BarkFluff.Web/wwwroot` (vanilla JS) | `Клиенты/Web.md` |
| WPF V1 | `Windows/BarkFluff.Client.WPF` | `Клиенты/Windows-WPF.md` |
| **WPF V2** | `Windows/BarkFluff.ClientV2.WPF` | `Клиенты/Windows-WPF-V2.md` |
| Linux Qt | `Linux/` | `Клиенты/Linux-Qt.md` |

Общий gRPC-слой Windows — `Windows/BarkFluff.WebApi.Core` (`Клиенты/Windows-WebApiCore.md`).

## Статус клиентов

- **Android V1** — эталонный клиент, самый полный по функциям.
- **macOS / iOS** — общие пакеты `BFCore`/`BFNetworking`/`BFCalls`, второй по полноте контур.
- **Web** — vanilla-JS SPA, единственный с полноценными звонками «из коробки» + приватные чаты.
- **WPF V1** — **заморожен**, развитие не ведётся. Используется как источник UX-решений, не как код-база.
- **WPF V2** — текущая разработка (эта задача).
- **Android V2** — заброшенный тестовый Compose-проект, в матрицу не включён (см. `Клиенты/Android-V2.md`).
- **Linux Qt** — данные ограничены: документация архитектурная, не пофункциональная. Ячейки отмечены `?`, где по документации нельзя утверждать.

## Легенда

| Символ | Значение |
|---|---|
| ✅ | Реализовано |
| 🟡 | Реализовано частично / с ограничениями |
| ❌ | Не реализовано |
| ? | По документации определить нельзя |
| — | Неприменимо для платформы |

---

## Матрица функций

### Онбординг и авторизация

| Функция | Android | iOS | macOS | Web | WPF V1 | **WPF V2** | Linux |
|---|:--:|:--:|:--:|:--:|:--:|:--:|:--:|
| Welcome-экран первого запуска | ✅ | ✅ | ✅ | — | ✅ | ✅ | ? |
| Выбор ноды (Navigator + ручной Beacon) | ✅ | ✅ | ✅ | — | ✅ | ✅ | ✅ |
| Юр. документы + модалка согласия (revision-based) | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ |
| Вход login/email + пароль | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| 2FA / OTP при входе | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ? |
| Регистрация (многошаговая: имя, аватар, bio, 2FA) | ✅ | ✅ | ✅ | ✅ | ✅ | 🟡 минимальный флоу | ✅ |
| Восстановление пароля | ✅ | ❌ | ✅ | ✅ | ✅ | ✅ | ? |
| FastAuth: генерация QR (вход на новом устройстве) | ❌ | ❌ | ✅ | ✅ | ✅ | ✅ | ❌ |
| FastAuth: сканер QR (подтверждение с телефона) | ✅ | ✅ | ❌ | ❌ | 🟡 генерация | ❌ | ❌ |
| Восстановление сессии по refresh-токену | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| PIN-код на запуск приложения | ❌ | ❌ | ❌ | — | ✅ | ❌ | ✅ |
| Logout: серверный `Logout` + полный локальный wipe | ✅ | ✅ | ✅ | ✅ | 🟡 | ❌ | ? |

### Список чатов

| Функция | Android | iOS | macOS | Web | WPF V1 | **WPF V2** | Linux |
|---|:--:|:--:|:--:|:--:|:--:|:--:|:--:|
| Список чатов + пагинация | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| Счётчик непрочитанных / бейджи | ✅ | ✅ | ✅ | ✅ | ✅ | ❌ | ✅ |
| Сортировка по времени последнего сообщения | ✅ | ✅ | ✅ | ✅ | ✅ | ❌ | ? |
| Папки чатов (табы, создание, порядок, бейджи) | ✅ | ✅ | ✅ | ✅ | ❌ | ❌ | ❌ |
| Настройки папок (компактные / исключать из «Все чаты») | ✅ | ✅ | ✅ | ❌ | ❌ | ❌ | ❌ |
| Поиск пользователей → новый чат | ✅ | ✅ | ✅ | ✅ | ✅ | ❌ | ✅ |
| Создание группового чата | ✅ | ✅ | ✅ | ✅ | ❌ | ❌ | ❌ |
| Управление группой (название/аватар/участники) | ✅ | 🟡 | 🟡 | ✅ | ❌ | ❌ | ❌ |
| Offline-first кеш списка чатов | ✅ (SQLCipher Room) | ✅ (GRDB) | ✅ (GRDB) | ❌ | ✅ (LiteDB) | ❌ | ? |
| Skeleton/плейсхолдеры при загрузке | ✅ | ✅ | ✅ | ❌ | ❌ | ❌ | ? |
| Per-chat mute | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ |

### Сообщения

| Функция | Android | iOS | macOS | Web | WPF V1 | **WPF V2** | Linux |
|---|:--:|:--:|:--:|:--:|:--:|:--:|:--:|
| Отправка/приём текста | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| История + пагинация вверх | ✅ | ✅ | ✅ | ✅ | ✅ | 🟡 (загрузка есть, догрузка вверх — нет) | ✅ |
| Read-квитанции («прочитано») | ✅ | ✅ | ✅ | ✅ | ✅ | ❌ | ✅ |
| Разделители дат | ✅ | ✅ | ✅ | ✅ | ✅ | ❌ | ✅ |
| Разделитель непрочитанных | ✅ | ? | ? | ? | ✅ | ❌ | ? |
| Редактирование сообщения + метка «изменено» | ✅ | ✅ | ✅ | ✅ | ✅ | ❌ | ❌ |
| Удаление сообщения | ✅ | ✅ | ✅ | ✅ | ✅ | ❌ | ❌ |
| Ответ (reply) | ✅ (+ свайп) | ✅ | ✅ | ✅ | ✅ | ❌ | ❌ |
| Пересылка (forward, мультивыбор чатов) | ✅ | 🟡 | ✅ | ✅ | ✅ | ❌ | ❌ |
| Закреплённые сообщения | ✅ | ❌ | ❌ | ✅ | ❌ | ❌ | ❌ |
| Контекстное меню сообщения | ✅ | ✅ | ✅ | ✅ | ✅ | ❌ | ? |
| Копировать текст / изображение | ✅ | ✅ | ✅ | 🟡 | ✅ | ❌ | ? |
| Сохранить изображения / документы на диск | ✅ | ✅ | ✅ | ✅ | ✅ | ❌ | ? |
| Markdown в облачках | ✅ | ❌ | ❌ | ✅ | ❌ | ❌ | ❌ |
| Оптимистичный UI отправки + прогресс | ✅ | ✅ | ✅ | 🟡 | ✅ | ❌ | ✅ |
| Поиск по сообщениям | ? | ? | ? | ? | ✅ | ❌ | ? |

### Медиа и вложения

| Функция | Android | iOS | macOS | Web | WPF V1 | **WPF V2** | Linux |
|---|:--:|:--:|:--:|:--:|:--:|:--:|:--:|
| Изображения (одиночные + сетка) | ✅ | ✅ | ✅ | ✅ | ✅ | ❌ | ✅ |
| Правильный размер плейсхолдера (`image_width/height`) | ✅ | ? | ? | ? | ✅ | ❌ | ? |
| Видео + встроенный плеер | ✅ | ✅ | ✅ | ✅ | ✅ | ❌ | ✅ |
| GIF | ✅ | 🟡 | ✅ | ✅ | ✅ | ❌ | ? |
| Аудио-вложения (плеер + waveform) | ✅ | ✅ | ✅ | ✅ | ✅ | ❌ | ? |
| Голосовые сообщения — запись | ✅ | ❌ | ❌ | ❌ | ✅ | ❌ | ❌ |
| Голосовые сообщения — воспроизведение с waveform | ✅ | ✅ | ✅ | ✅ | ✅ | ❌ | ? |
| Документы (иконка, размер, скачивание) | ✅ | ✅ | ✅ | ✅ | ✅ | ❌ | ✅ |
| Стикеры + стикер-пикер | ✅ | ✅ | ✅ | ✅ | ✅ | ❌ | 🟡 эмодзи |
| Клиентское сжатие изображений перед отправкой | ✅ | ✅ | ✅ | ❌ | ✅ | ❌ | ? |
| Pre-upload дедупликация по SHA-256 (`CheckFileHash`) | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ |
| Drag & drop файлов в окно | — | — | ✅ | ✅ | ✅ | ❌ | ? |
| Вставка изображения из буфера (paste) | — | — | ? | ? | ✅ | ❌ | ? |
| Кропер изображений (аватар 1:1 / постер 3:1) | ✅ | ✅ | ✅ | ✅ | ✅ | ❌ | ? |
| Редактор изображения перед отправкой | ✅ | ❌ | ❌ | ✅ | ❌ | ❌ | ❌ |
| Обрезка/сжатие видео перед отправкой | ✅ | ❌ | ❌ | ❌ | ✅ (FFmpeg) | ❌ | ❌ |
| Полноэкранные вьюверы (изображение/видео) | ✅ | 🟡 | ✅ | ✅ | ✅ | ❌ | ✅ |
| Рефреш протухших presigned-ссылок | ✅ | ✅ | ✅ | ✅ | 🟡 | ❌ | ? |

### Real-time

| Функция | Android | iOS | macOS | Web | WPF V1 | **WPF V2** | Linux |
|---|:--:|:--:|:--:|:--:|:--:|:--:|:--:|
| Стрим новых сообщений + reconnect/backoff | ✅ | ✅ | ✅ | ✅ | ✅ | ❌ | ✅ |
| Стримы edited / deleted | ✅ | ✅ | ✅ | ✅ | ❌ | ❌ | ❌ |
| Стрим read-квитанций | ✅ | ✅ | ✅ | ✅ | ✅ | ❌ | ? |
| Онлайн-статусы (`Onliner`) | ✅ | ✅ | ✅ | ✅ | ✅ | ❌ | ✅ |
| Typing-индикатор («печатает…») | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ |
| Пересоздание стримов после refresh токена | ✅ | ✅ | ✅ | ✅ | ✅ | ❌ | ? |

### Звонки

| Функция | Android | iOS | macOS | Web | WPF V1 | **WPF V2** | Linux |
|---|:--:|:--:|:--:|:--:|:--:|:--:|:--:|
| Сигнализация (`Calls` gRPC + `SubscribeCallEvents`) | ✅ | ✅ | ✅ | ✅ | ❌ | ❌ | ❌ |
| Медиа через LiveKit SDK | ✅ | ✅ | ✅ | ✅ | ❌ | ❌ | ❌ |
| Аудио 1-на-1 | ✅ | ✅ | ✅ | ✅ | ❌ | ❌ | ❌ |
| Видео | ✅ | ✅ | ✅ | ✅ | ❌ | ❌ | ❌ |
| Групповые звонки (сетка участников) | ✅ | ✅ | ✅ | ✅ | ❌ | ❌ | ❌ |
| Демонстрация экрана | ✅ | 🟡 in-app | ✅ | ✅ | ❌ | ❌ | ❌ |
| Экран входящего звонка + рингтон | ✅ | ✅ | ✅ | ✅ | ❌ | ❌ | ❌ |
| История звонков (вкладка «Звонки») | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ |
| Выбор устройств / качество аудио и видео | ✅ | 🟡 | 🟡 | ✅ | ❌ | ❌ | ❌ |
| Системная интеграция (Telecom/CallKit) | ✅ | ❌ | — | — | — | — | — |

### Настройки

| Раздел | Android | iOS | macOS | Web | WPF V1 | **WPF V2** | Linux |
|---|:--:|:--:|:--:|:--:|:--:|:--:|:--:|
| Профиль (имя / username / bio / аватар) | ✅ | ✅ | ✅ | ✅ | ✅ | ❌ | ✅ |
| Общие (тема light/dark/system) | ✅ | ✅ | ✅ | ✅ (3 темы) | ✅ | ✅ (+ фирменная `BarkFluffDark`) | ✅ |
| Уведомления и звук | ✅ | 🟡 | ✅ | ? | ✅ | ❌ | ? |
| Язык интерфейса | ✅ (5 языков) | ✅ RU/EN | ✅ RU/EN | ? | ✅ RU/EN | ✅ RU/EN | ? |
| Безопасность (пароль, 2FA TOTP, PIN) | ✅ | ✅ | ✅ | ✅ | ✅ | ❌ | ✅ |
| Приватность (видимость профиля/поиска/онлайна) | ✅ | ✅ | ✅ | ? | ✅ | ❌ | ❌ |
| Персонализация (постер, пузыри, фон, blur/dim) | ✅ | ✅ | ✅ | ✅ | ✅ | ❌ | ❌ |
| Облако (серверное хранилище по типам) | ✅ | ✅ | ✅ | ✅ | ✅ | ❌ | ✅ |
| Локальный кеш (объём + очистка) | ✅ | ✅ | ✅ | ❌ | ✅ | ❌ | ✅ |
| Активные сессии / устройства | ✅ | ✅ | ✅ | ✅ | ✅ | ❌ | ✅ |
| О приложении | ✅ | ✅ | ✅ | ? | ✅ | ❌ | ✅ |
| О сервере (микросервисы, пинг) | ✅ | ✅ | ✅ | ? | ✅ | ❌ | ? |
| Папки чатов (управление) | ✅ | ✅ | ✅ | ✅ | ❌ | ❌ | ❌ |
| Раздел «Тестирование» (dev-флаги) | ✅ | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ |
| Виджеты (управление) | ✅ | ❌ | — | — | — | ❌ | — |

### Приватность и E2E

| Функция | Android | iOS | macOS | Web | WPF V1 | **WPF V2** | Linux |
|---|:--:|:--:|:--:|:--:|:--:|:--:|:--:|
| Приватные чаты (passphrase, Argon2id + AES-GCM) | ✅ | ❌ | ❌ | ✅ | ❌ | ❌ | ❌ |
| Инвайт-флоу приватного чата | ✅ | ❌ | ❌ | 🟡 | ❌ | ❌ | ❌ |
| Секретные чаты (libsignal Double Ratchet) | 🟡 (блокер Kyber в proto) | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ |
| Prekey-bundle регистрация | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ |

> `BarkFluff.WebApi.Core` уже содержит `WebApiPrivateChatManager` (Argon2id + AES-256-GCM) и транспорт секретных чатов —
> для WPF V2 приватные чаты стоят дешевле, чем на других платформах. Double Ratchet/X3DH в Core **не реализованы**.

### Платформенная интеграция

| Функция | Android | iOS | macOS | Web | WPF V1 | **WPF V2** | Linux |
|---|:--:|:--:|:--:|:--:|:--:|:--:|:--:|
| Push-уведомления | ✅ FCM | ❌ | — | — | — | — | — |
| Системные уведомления ОС | ✅ | ❌ | ✅ | ? | ✅ WinRT toast | ❌ | ✅ D-Bus |
| In-app toast-уведомления | ❌ | ❌ | ❌ | ✅ | ✅ Erida | ❌ | ? |
| Трей / сворачивание в трей | — | — | — | — | ✅ | ❌ | ✅ |
| Deep links (`bf://`, `bfdev://`) | ✅ | ❌ | ❌ | — | ✅ | ❌ | ❌ |
| Single instance | — | — | — | — | ✅ (mutex + named pipe) | ❌ | ? |
| Системное «Поделиться» (share-in) | ✅ | ❌ | ❌ | — | ❌ | ❌ | ❌ |
| Виджеты рабочего стола | ✅ | ❌ | ❌ | — | ❌ | ❌ | ❌ |
| Авто-обновление клиента | ✅ | ❌ | ❌ | — | ✅ (BITS + PS1) | ❌ | ❌ |
| Сохранение размера/позиции окна | — | — | ? | — | ✅ | ❌ | ? |

### Хранилище и безопасность локальных данных

| Функция | Android | iOS | macOS | Web | WPF V1 | **WPF V2** | Linux |
|---|:--:|:--:|:--:|:--:|:--:|:--:|:--:|
| Шифрованное хранение токенов | ✅ EncryptedSharedPrefs | ✅ Keychain | ✅ Keychain | 🟡 localStorage | ✅ AES-256 от PIN | ✅ DPAPI | ✅ AES-256 |
| Персистентный кеш сообщений | ✅ Room/SQLCipher | ✅ GRDB | ✅ GRDB | ❌ | ✅ LiteDB | ❌ | ? |
| Файловый кеш медиа | ✅ | ✅ | ✅ | ❌ | ✅ | ❌ | ✅ |
| Кеш presigned-URL | ✅ | ✅ | ✅ | ✅ | ✅ | ❌ | ? |
| Локальные настройки в БД/prefs | ✅ | ✅ | ✅ | ✅ | ✅ JSON | ✅ SQLite | ✅ QSettings |

---

## Что уже реализовано в `BarkFluff.ClientV2.WPF`

Текущее состояние (см. `Клиенты/Windows-WPF-V2.md` и `Windows-WPF-V2-ProjectMap.md`):

**Инфраструктура**
- WPF .NET 10 (`net10.0-windows10.0.26100.0`), MVVM (CommunityToolkit.Mvvm), DI-контейнер, WPF UI (Fluent).
- SQLite `data/barkfluff.db` рядом с exe — настройки онбординга, язык, выбранная нода.
- DPAPI (`CurrentUser`) для access/refresh токенов — `DpapiSecureSessionStore`.
- Локализация RU/EN через swap `ResourceDictionary` + `DynamicResource`.
- Темы: `System` / `Light` / `Dark` / фирменная `BarkFluffDark` (accent `#81341E`), Mica, `SystemThemeWatcher`.
- Единые стили `Resources/Styles/Controls.xaml`, только `ui:*`-контролы WPF UI.
- Тесты: `Tests/BarkFluff.ClientV2.WPF.Tests` (parser, mapper, SQLite, ViewModel).

**Функции**
- Welcome → выбор ноды (Navigator + ручной адрес) → `GetServerInfo` → сохранение endpoints.
- Вход: login/email + пароль + 2FA (6 полей OTP с полной вставкой).
- FastAuth: генерация QR, server-stream статусов, авто-перевыпуск на `REJECTED`/`EXPIRED`.
- Регистрация: минимальный флоу `CreateAccount → ConfirmAccount → SetPassword`.
- Восстановление пароля: `ResetPassword → ConfirmResetPassword → SetPassword`.
- Автовосстановление сессии при следующем запуске (refresh token → проверка профиля → главный экран).
- Мессенджер (MVP): двухпанельный экран, `GetChats`, `GetMessagesWithOffset`, `SendMessage`, `Enter`/`Shift+Enter`.

---

## Что реализовать в WPF V2 — план по этапам

Приоритет выставлен по принципу «сначала то, без чего клиент не является мессенджером».
Референс UX для Windows — WPF V1 и macOS-клиент; референс полноты функций — Android V1.

### Этап 1. Довести мессенджер до рабочего минимума

Без этого клиентом нельзя пользоваться каждый день.

| Задача | Референс | API / контракт |
|---|---|---|
| Realtime-стрим новых сообщений + reconnect с backoff | `RealtimeUpdateService` (WPF V1) | `WebApiUpdateManager` → `IAsyncEnumerable` |
| Пересоздание всех стримов по событию `TokenRefreshed` | `Windows-WebApiCore.md` | `webApi.StartAutoRefresh` + `TokenRefreshed` |
| Read-квитанции (отправка + приём) | `MessageReadController` | `Updates` read-стрим |
| Счётчики непрочитанных в списке чатов | WPF V1 / Android | `ChatData.countUnread` |
| Пагинация истории вверх | `ChatHistoryController` | `GetMessagesWithOffset` |
| Разделители дат + разделитель непрочитанных | WPF V1 | клиентская логика |
| Онлайн-статусы в списке и шапке чата | `OnlineStatusService` (WPF V1), `OnlineStatusService` (macOS) | `Onliner.SubscribeToOnlineStatus` |
| Поиск пользователей → создание/открытие ЛС | WPF V1 `SearchElement` | `SearchUsers` + `GetPersonChatId` |
| Logout: серверный `Logout` + wipe SQLite/DPAPI/кешей, сохранить адрес ноды | macOS `performLocalWipe` | `Identity.Logout` |

### Этап 2. Вложения и медиа

| Задача | Референс | Примечание |
|---|---|---|
| Отправка изображений (сетка + одиночное) | WPF V1 `MultiImageGrid` / `ImageMessageContent` | использовать `image_width`/`image_height` для плейсхолдера без «прыжка» |
| Клиентское сжатие изображений | `ImageProcessor` (Core, ImageSharp, q=90, 4:2:0) | уже есть в Core |
| Видео + встроенный плеер | WPF V1 `VideoMessageContent`/`VideoPlayer` | |
| Аудио и голосовые (плеер + waveform) | WPF V1 `AudioMessageContent`/`VoiceMessage`, `AudioAnalyzer` | |
| Запись голосовых сообщений | WPF V1 `RecordButton`, Android hold-to-record | |
| Документы (иконка, размер, скачивание) | WPF V1 `DocumentMessageContent` | |
| Прогресс загрузки + оптимистичный UI | Android `MediaSendService`, WPF V1 `UploadingAttachmentItem` | |
| Полноэкранные вьюверы изображений/видео | WPF V1 `ImageViewer`/`VideoPlayer` | |
| Drag & drop и вставка из буфера | WPF V1 `MessengerPage.DragDrop.cs` | сильная сторона десктопа, не терять |
| Файловый кеш + кеш presigned-URL + рефреш протухших ссылок | WPF V1 `FileCacheService`, Web `bindResilientMedia` | |
| Стикеры + стикер-пикер | WPF V1 `StickerPicker` | |

### Этап 3. Действия над сообщениями

| Задача | Референс | API |
|---|---|---|
| Контекстное меню сообщения | macOS `MessageBubbleView.contextMenu`, WPF V1 `MessageBubble` | набор пунктов по условиям |
| Редактирование + метка «изменено» | Android/macOS | `EditMessage` + `SubscribeMessagesEdited` |
| Удаление | Android/macOS | `DeleteMessage` + `SubscribeMessagesDeleted` |
| Ответ (reply) с превью над инпутом | Android `replyPreviewBar` | `OutgoingMessage.forwarded_message_id` |
| Пересылка с мультивыбором чатов + комментарий | Android `ForwardChatPickerBottomSheet`, macOS `ForwardChatPickerView` | тот же `forwarded_message_id` |
| Копировать текст / изображение | macOS `MediaActions` | `Clipboard` |
| Сохранить изображения / документы | macOS `saveImages`/`saveDocuments` | |
| Закреплённые сообщения | Android `PinnedMessagesActivity`, Web | контракт — `Backend/Messages-PinnedMessages-ClientGuide.md` |

> В протоколе **нет отдельного reply**: и ответ, и пересылка идут через `forwarded_message_id`.
> Различие чисто визуальное — эвристика Android: если оригинал есть в загруженной истории, рисуем компактный reply-блок, иначе полный forward-блок.

### Этап 4. Группы, папки, профили

| Задача | Референс |
|---|---|
| Создание группового чата | Web `newchat.js`, Android `CreateGroupChatActivity` |
| Инфо группы: название, аватар, участники, add/kick | Android `GroupInfoActivity`, Web `#groupOverlay` |
| Профиль пользователя (панель) + shared media | WPF V1 `Profile`, iOS `UserProfile` |
| Папки чатов: табы над списком + управление в настройках | Android, macOS, `Backend/Users-ChatFolders-ClientGuide.md` |
| Настройки папок (компактные / исключать из «Все чаты») | Android `PersonalizationSettingsActivity` |

### Этап 5. Настройки

Целевой набор — 12 разделов WPF V1 (собраны по образцу macOS), плюс папки:

Профиль · Общие · Уведомления и звук · Язык · Безопасность · Приватность · Персонализация · Облако · Кеш · Активные сессии · О приложении · О сервере · **Папки чатов**.

Из них в V2 сейчас закрыты только тема и язык.

Отдельно:
- **Персонализация** — постер профиля, скругление пузырей (0–20), фон чата, blur (1–25), dim (0–100 %). Серверная часть: `GetPersonalization`/`UpdatePersonalization` **полностью перезаписывает** `UserPersonalizationData` — при апдейте фонов обязательно передавать текущий `ProfilePosterFileId`, иначе постер обнулится.
- **Безопасность** — смена пароля (3 шага), 2FA TOTP (setup QR + confirm + disable). PIN-код — решить, переносим ли из V1 (см. открытые вопросы).

### Этап 6. Локальный кеш и offline

| Задача | Референс |
|---|---|
| Персистентный кеш сообщений и чатов | WPF V1 LiteDB, macOS/iOS GRDB, Android Room/SQLCipher |
| Stale-first: показать кеш мгновенно, ревалидировать в фоне | macOS/iOS `loadChats`/`revalidateChats` |
| Оффлайн-баннер и повтор синхронизации | macOS `ErrorBannerView`, Android |
| Настройки кеша: объём по типам + очистка | WPF V1 `CacheSettingsPage` |

> В V2 таблиц кеша сообщений пока нет — их вводить **отдельной SQLite-миграцией**; токены остаются только в DPAPI, открытое хранение запрещено.

### Этап 7. Платформенная интеграция Windows

| Задача | Референс |
|---|---|
| WinRT toast-уведомления + режимы (Disabled/HiddenContent/SenderOnly/FullWithPreview) | WPF V1 `NotificationManager` |
| In-app toast (Erida-подобный) | WPF V1 `Services/Erida` |
| Трей + сворачивание в трей | WPF V1 `MainWindow` |
| Single instance (mutex + named pipe) | WPF V1 `BFSingleInstance` |
| Deep links `bf://` / `bfdev://` | WPF V1 `ProtocolRegistrar` |
| Сохранение размера/позиции окна | WPF V1 `WindowStateService` |
| Авто-обновление (BITS + PS1-скрипт, SHA-256) | WPF V1 `UpdateService` + `LaunchUpdater` |

### Этап 8. Звонки

Полный контур: сигнализация через `WebApiCallsManager` (`InitiateCall`/`Accept`/`Reject`/`Join`/`End`/`SetCallAudioQuality`/`SubscribeCallEvents`/`ListCallHistory`), медиа — **LiveKit .NET/нативный SDK** (в Core медиа-плоскости нет).

Референс UI — Web `calls-ui.js` (ближе всего к десктопу): ринг-оверлей, полноэкранный экран звонка, сетка плиток, камера и демонстрация экрана как равноправные блоки, выбор устройств, ползунки качества.

`CallsAvailable` может быть `false` — сервер без сервиса звонков это нормальный сценарий, кнопки звонка скрывать.

### Этап 9. Опционально / низкий приоритет

| Задача | Комментарий |
|---|---|
| Markdown в облачках | Есть только в Android V1 и Web. Нужен собственный парсер + strip для превью |
| Typing-индикатор | Только Android V1. Контракт `Onliner` готов |
| Приватные чаты (passphrase E2E) | `WebApiPrivateChatManager` уже в Core — относительно дёшево |
| Секретные чаты (libsignal) | Требует Double Ratchet/X3DH в приложении, в Core нет. Плюс блокер: `PrekeyBundle` без Kyber |
| FastAuth-сканер (подтверждение чужого входа) | На десктопе сомнительно — нужна камера |
| Per-chat mute | Только Android |
| Pre-upload дедупликация SHA-256 (`CheckFileHash`) | Экономит трафик, кросс-клиентно совместимо |
| Редактор изображений перед отправкой | Есть в Android и Web |
| Обрезка/сжатие видео (FFmpeg) | Было в WPF V1 |
| Юр. документы + модалка согласия | Только Android. Если требуется юридически — поднять приоритет |

---

## Ключевые решения, которые нужно принять по V2

1. **PIN-код.** В WPF V1 весь `GlobalParam.json` шифровался AES-256 ключом от PIN. V2 использует DPAPI и PIN не требует. Нужно ли возвращать PIN как отдельную фичу «блокировка приложения»?
2. **Кеш сообщений.** LiteDB (как V1) или SQLite поверх уже существующей `barkfluff.db`? Второе даёт одно хранилище и одну схему миграций.
3. **Звонки.** Нужен ли на Windows-клиенте LiveKit вообще в первой версии, и какой SDK (нативный/WebView2 с web-реализацией)?
4. **Markdown.** Делать паритет с Android/Web или оставить plain-текст, как в WPF V1?
5. **Объём паритета с V1.** WPF V1 заморожен — считаем ли обязательным перенос всего (включая FFmpeg-конвертацию видео, ProfileShare, DevTools), или V2 сознательно уже.

---

## Связанные документы

- `Obsidian/ClaudeVault/Клиенты/DesignDocument.md` (источник `docs/dd.md`) — UI/UX спецификация всех экранов
- `Obsidian/ClaudeVault/Backend/Messages-PinnedMessages-ClientGuide.md` — контракт закреплённых сообщений
- `Obsidian/ClaudeVault/Backend/Users-ChatFolders-ClientGuide.md` — контракт папок чатов
- `Obsidian/ClaudeVault/Архитектура.md` — tech stack, порты, XAuth, gRPC-клиент
- `docs/Android-iOS-feature-comparison.md` — сравнение Android и iOS (2026-07-05)
- `Windows/BarkFluff.ClientV2.WPF/docs/Architecture.md` — правила разработки V2
