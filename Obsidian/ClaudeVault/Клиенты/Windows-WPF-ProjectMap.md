# BarkFluff.Client.WPF — Карта файлов проекта

Детальное описание каждого файла WPF-клиента.
Общая архитектура и описание проекта → [[Клиенты/Windows-WPF]]

Расположение: `Windows/BarkFluff.Client.WPF/`

---

## Корень проекта

| Файл | Описание |
|------|----------|
| `App.xaml` | Точка входа приложения, подключение ресурсных словарей (темы, стили) |
| `App.xaml.cs` | Логика запуска: Mutex single-instance, named pipe listener, инициализация сервисов, навигация при старте. Хранит глобальные статические свойства: `ServerCommunication`, `GParam`, `CacheManager`, `FileCacheService`, `ErideMessage`, `WindowStateService`, `OnlineStatusService`, `NotificationManager`, `DeadTokenMode` |
| `AssemblyInfo.cs` | Атрибуты сборки (тема Windows, DPI awareness) |

---

## MessengerWindows/

| Файл | Описание |
|------|----------|
| `MainWindow.xaml` | XAML главного окна |
| `MainWindow.xaml.cs` | Логика главного окна: минимизация в трей, управление окном, хостинг `MessengerPage` |

---

## Pages/

| Файл | Описание |
|------|----------|
| `MessengerPage.xaml` | XAML основной страницы мессенджера (список чатов + область сообщений) |
| `MessengerPage.xaml.cs` | Инициализация всех контроллеров, точка сборки страницы |
| `PasswordReset.xaml / .cs` | Страница сброса пароля |
| `TestControl.xaml / .cs` | Тестовый контрол (DEV) |

### Pages/Messenger/ (partial-классы MessengerPage)

| Файл | Описание |
|------|----------|
| `MessengerPage.DragDrop.cs` | Обработка drag & drop файлов на окно мессенджера |
| `MessengerPage.Input.cs` | Обработка ввода текста, горячих клавиш, Enter/Shift+Enter |
| `MessengerPage.Panels.cs` | Управление боковыми панелями, профилями, overlay-панелями |
| `MessengerPage.Search.cs` | Поиск по чатам и сообщениям |
| `MessengerPage.Viewers.cs` | Просмотрщики (изображения, видео, документы), запуск `LaunchUpdater` (обновление через PS1-скрипт) |
| `MessageTypeMapper.cs` | Маппинг Proto `MessageAttachmentType` → внутренний тип UI |
| `ScrollAnimationBehavior.cs` | Анимация прокрутки списка сообщений |

### Pages/Messenger/Controllers/

Контроллеры отвечают за отдельные аспекты логики мессенджера:

| Файл | Описание |
|------|----------|
| `AttachmentController.cs` | Выбор, preview, загрузка и отправка вложений; drag & drop и paste изображений/файлов |
| `ChatHistoryController.cs` | Загрузка истории сообщений чата, пагинация, рендеринг пузырей |
| `ChatListController.cs` | Управление списком чатов: загрузка, сортировка, обновление последнего сообщения |
| `ChatOpeningController.cs` | Открытие чата: инициализация состояния, загрузка истории, сброс непрочитанных |
| `MessageReadController.cs` | Отправка read-квитанций, обработка входящих `ReadReceiptReceived` событий |
| `MessageSendController.cs` | Отправка текстовых и медиа-сообщений через gRPC |
| `NotificationController.cs` | Показ уведомлений (Erida / WinRT) при входящих сообщениях |
| `OnlineStatusController.cs` | Подписка на статусы онлайн для открытого чата, обновление UI |
| `RealtimeMessagesController.cs` | Обработка входящих real-time событий `NewMessageReceived` от `RealtimeUpdateService` |

### Pages/SetupPages/

| Файл | Описание |
|------|----------|
| `WelcomPage.xaml / .cs` | Приветственный экран при первом запуске |
| `SelectServer.xaml / .cs` | Выбор/ввод адреса сервера BarkFluff |
| `Login.xaml / .cs` | Форма входа (email + password) |
| `CreateAccount.xaml / .cs` | Начальный шаг регистрации |
| `CompletionRegistration.xaml / .cs` | Завершение регистрации (имя, аватар и т.д.) |
| `Registration/TwoFA.xaml / .cs` | Ввод кода двухфакторной аутентификации при регистрации |

### Pages/PinCode/

| Файл | Описание |
|------|----------|
| `PincodeSecure.xaml / .cs` | Экран ввода PIN при запуске (разблокировка); при невалидном токене — редирект |
| `CreatePinCodePage.xaml / .cs` | Создание/изменение PIN-кода |

---

## Services/App/

| Файл | Описание |
|------|----------|
| `AppVersion.cs` | Константы версии приложения (`Version`, `VersionType`, `VersionName`, `AppName`) |
| `AvatarImageHolder.cs` | Кеш/хранилище загруженных аватаров в памяти |
| `FFPlayHost.cs` | Хост для запуска `ffplay` (воспроизведение аудио/видео через внешний процесс) |
| `OnlineStatusService.cs` | Singleton. gRPC-стриминг онлайн-статусов (`Onliner.SubscribeToOnlineStatus`). Ping каждые 3 сек. События: `OnlineStatusChanged`, `ConnectionStatusChanged`. Дебаунс 500 мс. |
| `RealtimeUpdateService.cs` | Singleton. gRPC-стриминг входящих сообщений (`Updates.JustUpdate`). Автопереподключение (exp. backoff 2с→30с). Дедупликация по `MessageId`. События: `NewMessageReceived`, `ReadReceiptReceived`, `ConnectionStatusChanged` |
| `ThemeRegistryHelper.cs` | Запись выбранной темы в реестр Windows |
| `WindowStateService.cs` | Отслеживание и восстановление состояния окна (размер, позиция, maximized) |

### Services/App/Caching/

| Файл | Описание |
|------|----------|
| `MessageCacheManager.cs` | LiteDB: коллекции `messages`, `files`, `chats`. Методы сохранения/получения сообщений, файлов, метаданных чатов. Макс. 5 парал. загрузок (SemaphoreSlim). Путь: `datas/cache.db` |
| `FileCacheService.cs` | Управление файловым кешем на диске (`datas/file_cache.db`). Загрузка по URL, хранение по типу (аватары, изображения, видео, аудио, документы) |
| `CachedFile.cs` | Модель LiteDB записи кешированного файла |
| `CachedMessage.cs` | Вспомогательная модель кешированного сообщения |
| `FileType.cs` | Enum типов файлов в кеше |

### Services/App/Converter/

| Файл | Описание |
|------|----------|
| `AudioAnalyzer.cs` | Анализ аудио (waveform для визуализации голосовых сообщений) |
| `Base64ToBitmapSource.cs` | Конвертер Base64-строки → `BitmapSource` для WPF |
| `DoubleToCornerRadiusConverter.cs` | IValueConverter: double → CornerRadius для XAML биндинга |
| `FFmpegService.cs` | Обёртка над `ffmpeg` CLI: перекодирование аудио/видео |
| `VideoCompressor.cs` | Сжатие видео перед отправкой (через FFmpeg) |

### Services/App/Update/

| Файл | Описание |
|------|----------|
| `UpdateService.cs` | Проверка обновлений каждые 2 ч: `GET /get/barkfluffwindows/{channel}/version`. Событие `UpdateAvailable`. Хранит `LatestDownloadUrl`, `LatestVersion` |

---

## Services/Erida/

In-app уведомления (toast overlay внутри окна):

| Файл | Описание |
|------|----------|
| `DropMessage.cs` | Логика показа/скрытия Erida-уведомлений (StackPanel overlay, auto-dismiss) |
| `EridaMessage.xaml / .cs` | XAML и код одного Erida-сообщения |
| `MessageType.cs` | Enum типов Erida-сообщений (Info, Warning, Error, Success) |

---

## Services/Notification/

Windows Toast (WinRT) уведомления:

| Файл | Описание |
|------|----------|
| `AppIdHelper.cs` | Получение `AppUserModelId` для WinRT |
| `NotificationData.cs` | Модель данных для уведомления |
| `NotificationManager.cs` | Singleton. Управление WinRT-уведомлениями, настройки (Disabled/HiddenContent/SenderOnly/FullWithPreview) |
| `ShortcutHelper.cs` | Создание ярлыка в Start Menu (обязательно для WinRT toast) |
| `SystemNotificationHelper.cs` | Вспомогательные методы Windows shell для уведомлений |
| `ToastNotificationService.cs` | Высокоуровневый сервис формирования и отправки toast-уведомлений |

### Services/Notification/System/

| Файл | Описание |
|------|----------|
| `BFSingleInstance.cs` | Named pipe (имя `bf_pipe_instance`): слушатель первого экземпляра + отправка args из второго |
| `ProtocolHelper.cs` | Проверка регистрации протокола `bf://` / `bfdev://` в реестре |
| `ProtocolRegistrar.cs` | Регистрация URI-протоколов `bf://` / `bfdev://` в реестре Windows |
| `SystemInfo.cs` | Информация о системе (ОС, версия, etc.) |

---

## Services/QR/

| Файл | Описание |
|------|----------|
| `RoundedQrGenerator.cs` | Генерация QR-кода с скруглёнными углами (для быстрой авторизации устройства) |

---

## Services/Registration/

| Файл | Описание |
|------|----------|
| `RegistrationStateService.cs` | Сохранение промежуточного состояния многошаговой регистрации между страницами |

---

## Services/

| Файл | Описание |
|------|----------|
| `ImageColorAnalyzer.cs` | Анализ цвета изображения (вынесен в Services/, используется в App) |

---

## Reactive/

Лёгкие обёртки для XAML-биндинга без MVVM:

| Файл | Описание |
|------|----------|
| `ReactiveBool.cs` | `INotifyPropertyChanged` обёртка над `bool` |
| `ReactiveLong.cs` | `INotifyPropertyChanged` обёртка над `long` |
| `ReactiveString.cs` | `INotifyPropertyChanged` обёртка над `string` |

---

## Validators/

| Файл | Описание |
|------|----------|
| `EmailValidator.cs` | Валидация email-адреса |
| `UsernameValidator.cs` | Валидация имени пользователя (длина, символы) |
| `PasswordValidator.cs` | Валидация сложности пароля |
| `PersonalInfoValidator.cs` | Валидация имени/фамилии |
| `VerificationCodeValidator.cs` | Валидация OTP/кода подтверждения |
| `ValidationTypes.cs` | Enum или константы типов валидации |
| `IRegistrationStep.cs` | Интерфейс шага регистрации (метод `Validate`) |

---

## UserControls/

Переиспользуемые UI-компоненты:

| Файл | Описание |
|------|----------|
| `AttachmentPreviewOverlay.xaml / .cs` | Overlay предпросмотра выбранных вложений перед отправкой |
| `CachedAvatar.xaml / .cs` | Аватар с кешированием из `FileCacheService`; поддержка размеров `AvatarSize`, типов `AvatarType` |
| `CachedImage.xaml / .cs` | Изображение с ленивой загрузкой и кешированием |
| `ChatItem.xaml / .cs` | Элемент списка чатов (имя, аватар, последнее сообщение, счётчик непрочитанных) |
| `CropImage.xaml / .cs` | Кадрирование изображения (выбор аватара) |
| `DateHeaderControl.xaml / .cs` | Разделитель дат в списке сообщений |
| `ImageViewer.xaml / .cs` | Полноэкранный просмотр изображений |
| `MessageBubble.xaml / .cs` | Пузырь сообщения: текст, время, статус прочтения, выбор типа контента |
| `MultiSegmentProgressUserControl.xaml / .cs` | Индикатор прогресса с несколькими сегментами |
| `PostUpdateMessage.xaml / .cs` | Баннер/сообщение об обновлении после установки новой версии |
| `PreviewUser.xaml / .cs` | Карточка предпросмотра профиля пользователя |
| `Profile.xaml / .cs` | Полная панель профиля пользователя |
| `ProfileShare.xaml / .cs` | UI для шаринга профиля (QR + ссылка) |
| `ProgressUserControl.xaml / .cs` | Стандартный индикатор прогресса загрузки |
| `RecordButton.xaml / .cs` | Кнопка записи голосового сообщения (hold-to-record) |
| `SearchElement.xaml / .cs` | Элемент результата поиска |
| `ServerItem.xaml / .cs` | Элемент списка серверов на экране выбора сервера |
| `Settings.xaml / .cs` | Контейнер настроек с навигацией по разделам |
| `SideBar.xaml / .cs` | Боковая панель: список чатов, поиск, кнопки навигации |
| `StickerPicker.xaml / .cs` | Пикер стикеров для отправки в чат |
| `UnreadSeparatorControl.xaml / .cs` | Разделитель непрочитанных сообщений |
| `VideoEditor.xaml / .cs` | Редактор/обрезчик видео перед отправкой |
| `VideoPlayer.xaml / .cs` | Встроенный видеоплеер |
| `VoiceMessage.xaml / .cs` | Воспроизведение голосового сообщения с waveform |
| `AvatarSize.cs` | Enum размеров аватара (Small, Medium, Large) |
| `AvatarType.cs` | Enum типов аватара (User, Group) |

### UserControls/MessageContent/

Рендереры содержимого конкретных типов вложений:

| Файл | Описание |
|------|----------|
| `AudioMessageContent.xaml / .cs` | Аудио-вложение (плеер + waveform) |
| `DocumentMessageContent.xaml / .cs` | Документ-вложение (иконка, имя, размер, скачать) |
| `ImageMessageContent.xaml / .cs` | Одиночное изображение |
| `ImageRow.xaml / .cs` | Горизонтальная строка изображений |
| `MultiDocumentList.xaml / .cs` | Список нескольких документов |
| `MultiImageGrid.xaml / .cs` | Сетка нескольких изображений |
| `MultiVideoGrid.xaml / .cs` | Сетка нескольких видео |
| `StickerMessageContent.xaml / .cs` | Стикер |
| `TextMessageContent.xaml / .cs` | Текстовое сообщение (markdown/plain) |
| `UploadingAttachmentItem.xaml / .cs` | Элемент прогресса загрузки вложения |
| `VideoMessageContent.xaml / .cs` | Одиночное видео-вложение (миниатюра + плеер) |
| `VideoRow.xaml / .cs` | Горизонтальная строка видео |

### UserControls/SettingsPages/

| Файл | Описание |
|------|----------|
| `BaseSettingsPage.cs` | Базовый класс страниц настроек (`Title`, `OnNavigatedTo`) |
| `ProfileSettingsPage.xaml / .cs` | Редактирование профиля (имя, аватар, bio, username) |
| `GeneralSettingsPage.xaml / .cs` | Общие — выбор темы (light / dark / system) |
| `NotificationsSettingsPage.xaml / .cs` | Уведомления и звук (5 режимов NotificationDisplayMode + toggle звука + серверный SetNotificationsEnabled) |
| `LanguageSettingsPage.xaml / .cs` | Выбор языка интерфейса (плейсхолдер) |
| `SecuritySettingsPage.xaml / .cs` | Безопасность (3-step смена пароля, 2FA TOTP с OtpStatus/Disable, PIN) |
| `PrivacySettingsPage.xaml / .cs` | Приватность профиля (видимость аватара/био/email/онлайна) |
| `PersonalizationSettingsPage.xaml / .cs` | Персонализация: постер профиля, скругление пузырей, размытие фона, затемнение, сетка фонов |
| `CloudSettingsPage.xaml / .cs` | Облако — статистика хранилища по типам файлов |
| `CacheSettingsPage.xaml / .cs` | Локальный кеш (очистка по типам, общий объём) |
| `SessionsSettingsPage.xaml / .cs` | Активные сессии (раньше `DevicesSettingsPage`) |
| `AboutAppSettingsPage.xaml / .cs` | О приложении: версия, .NET, ОС, CPU, RAM, архитектура |
| `AboutServerSettingsPage.xaml / .cs` | О сервере: имя/адрес/описание, авто-пинг, список микросервисов с health-статусом |

### UserControls/Classes/

| Файл | Описание |
|------|----------|
| `BadgeInfo.cs` | Модель бейджа пользователя |
| `UserProfile.cs` | Локальная модель профиля для UI-компонентов |

### UserControls/Extensions/

| Файл | Описание |
|------|----------|
| `SidebarExtensions.cs` | Extension-методы для SideBar (анимации, хелперы) |

### UserControls/DevTools/

| Файл | Описание |
|------|----------|
| `Menu.xaml / .cs` | DevTools-меню для отладки в DEBUG-сборке |

---

## ServiceWindows/

| Файл | Описание |
|------|----------|
| `ButtonsLibrary.xaml / .cs` | Библиотека кастомных кнопок/контролов (служебное окно) |

---

## Debugs/

Только в DEBUG-сборке:

| Файл | Описание |
|------|----------|
| `DebugWindow.xaml / .cs` | Отладочное окно (логи, состояние сервисов) |
| `DebugSendMessageUI.xaml / .cs` | UI для ручной отправки тестовых сообщений |
| `ThemeDebugWindow.xaml / .cs` | Окно предпросмотра тем (переключение Light/Dark, F8) |

---

## Resources/

| Путь | Описание |
|------|----------|
| `Resources/Styles/Styles.xaml` | Глобальные стили WPF-контролов |
| `Resources/Styles/Themes/LightTheme.xaml` | Светлая тема (цвета, кисти) |
| `Resources/Styles/Themes/DarkTheme.xaml` | Тёмная тема (цвета, кисти) |
| `Resources/Images/` | PNG-логотипы приложения |
| `Resources/Icons/` | ICO-иконки (чёрная/белая) |
| `Resources/Placeholders/` | Заглушки: аватар, аудио, документ, изображение, видео, сохранённое |

## Fonts/

| Файл | Описание |
|------|----------|
| `AdwaitaSans-Regular.ttf` | Шрифт Adwaita Sans |
| `Neucha-Regular.ttf` | Шрифт Neucha (декоративный) |
| `NotoSansDisplay-VariableFont.ttf` | Noto Sans Display (variable) |
| `OpenSans-VariableFont.ttf` | Open Sans (variable, основной) |
| `OpenSans-Italic-VariableFont.ttf` | Open Sans Italic (variable) |

---

## Properties/

| Файл | Описание |
|------|----------|
| `launchSettings.json` | Профили запуска (VS) |

---

*Последнее обновление: актуально для версии `0.0.0.2085`, .NET 10, target `net10.0-windows10.0.26100.0`*
