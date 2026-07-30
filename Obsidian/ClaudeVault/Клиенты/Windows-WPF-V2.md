# BarkFluff.ClientV2.WPF

Parent: [[Index]]

## Назначение

Новый Windows-клиент BarkFluff. Реализуется независимо от замороженного [[Клиенты/Windows-WPF]]: WPF .NET 10, MVVM, DI и WPF UI.

Расположение: `Windows/BarkFluff.ClientV2.WPF/`  
Target framework: `net10.0-windows10.0.26100.0`

## Сборка и тесты

```bash
dotnet build Windows/BarkFluff.ClientV2.WPF/BarkFluff.ClientV2.WPF.csproj
dotnet test Tests/BarkFluff.ClientV2.WPF.Tests/BarkFluff.ClientV2.WPF.Tests.csproj
```

Требуется SDK из корневого `global.json`.

## Архитектура

- `Views` — XAML-представления без UI-логики в code-behind.
- `ViewModels` — состояние и команды через CommunityToolkit.Mvvm.
- `Services` — навигация, подключение к ноде и сессия; `OnboardingNavigationService` хранит последнее ViewModel, поэтому стартовый маршрут не зависит от порядка создания окна. [[Клиенты/Windows-WebApiCore]] используется только через `INodeConnectionService`.
- `Infrastructure/Storage` — SQLite `data/barkfluff.db` рядом с exe: настройки онбординга/языка и выбранная нода.
- `Infrastructure/Localization` — смена `ResourceDictionary`; `Resources/Localization/Strings.ru.xaml` и `Strings.en.xaml` имеют одинаковые ключи, в XAML используются `DynamicResource`.
- `Resources/Styles/Controls.xaml` — единая точка общих стилей интерфейса поверх WPF UI: Fluent-контролы, поверхности, карточки и типографика.

## Визуальный стиль

- Интерфейс следует Windows 11 Fluent: `ApplicationThemeManager` включает светлую тему, Mica и системный accent; `MainWindow` содержит `TitleBar` WPF UI.
- Для интерактивных элементов использовать только `ui:*`-контролы WPF UI, не нативные WPF `Button`, `TextBox`, `ListBox` или `ProgressBar`.
- Экраны онбординга используют общую типографику `Segoe UI Variable Text`, карточки, мягкие поверхности и короткую декларативную анимацию появления.

## Темы и вход

- `Resources/Colors/Light.xaml`, `Dark.xaml` и `BarkFluffDark.xaml` содержат семантические палитры. `ApplicationThemeService` применяет режим из SQLite: `System`, `Light`, `Dark` или фирменный `BarkFluffDark` с accent `#81341E`; первые три используют системный Windows accent, а `SystemThemeWatcher` отслеживает изменения Windows в запущенном приложении.
- После `GetServerInfo` `NodeServiceConfiguration` сохраняет endpoints Beacon, Identity, Users, Files, Messages, Updates, Onliner, FastAuth и Calls без токенов. На следующем запуске конфигурация восстанавливает `IClientSession` и открывает вход.
- `LoginViewModel` общается только с `IAuthenticationService`: обычный вход поддерживает login/email, пароль и 2FA. Код OTP — шесть полей, полная вставка в первом поле заполняет их все и запускает проверку. FastAuth создаёт анонимную QR-сессию, слушает server-stream статусов; `SCANNED` показывает ожидание, `ACCEPTED` применяет токены, а `REJECTED`/`EXPIRED` автоматически выпускают новый QR. При уходе с экрана стрим отменяется.
- `RegistrationViewModel` реализует минимальный серверный флоу `CreateAccount → ConfirmAccount → SetPassword`; `PasswordRecoveryViewModel` — `ResetPassword → ConfirmResetPassword → SetPassword`. Все строки экранов находятся в синхронизированных RU/EN resource dictionaries.
- Access/refresh токены сохраняются в SQLite только как DPAPI (`CurrentUser`) blob. При следующем запуске клиент восстанавливает выбранную ноду, обновляет access token через refresh token и, если проверка профиля успешна, сразу открывает главный экран; повреждённая или недействительная сессия удаляется.

## Мессенджер

- `MessengerViewModel` — основной маршрут после авторизации: двухпанельный экран со списком чатов, областью выбранного чата, загрузкой истории и composer. Обычные сообщения отправляются кнопкой или `Enter`; `Shift+Enter` оставляет перенос строки. Заголовок чата и заглушка «выберите чат» переключаются через `HasSelectedChat` / `IsChatPlaceholderVisible`: `StringToVisibilityConverter` работает только со строками и для `ChatItemViewModel` не подходит. Заглушка лежит в строке ленты, поэтому центрируется по всей свободной области.
- `Views/Controls/ChatListItemControl` показывает аватар, имя/фамилию собеседника в личном диалоге, время последней активности и счётчик непрочитанных. В отсутствие или при ошибке загрузки аватара остаются инициалы; превью обычного чата нормализуется до одной строки и 20 текстовых символов. Во время `IsLoading` список перекрывается пятью `ChatListSkeletonControl`.
- `Views/Controls/MessageBubbleControl` выравнивает исходящие и входящие сообщения, показывает выделяемый текст без поверхности внутреннего `TextBox`, фото и видео; одно медиа занимает облачко без внутреннего отступа, 2+ — сетку, а документы отображаются списком. У исходящих сообщений `MessageReadStatusControl` рисует галочки: одна — доставлено, вторая накладывается при прочтении другим участником (подсказки `Messenger_Delivered`/`Messenger_Read`). Отредактированное сообщение помечается «изменено».
- `MessengerService` инкапсулирует [[Клиенты/Windows-WebApiCore]] для `MessengerViewModel`: чаты, историю, отправку, правку, удаление, закрепы, файлы и read API. Лента — `ScrollViewer` с `ItemsControl` и `VerticalAlignment="Bottom"`, поэтому короткая переписка прижата к нижней кромке; UI-виртуализация сознательно не используется (страница истории ограничена 50–100 сообщениями). Если непрочитанных нет, View прокручивает ленту вниз; иначе загружает страницу вокруг `FirstUnreadMessageId` и центрирует его. `MessageListBehavior` привязан к `ItemsControl`, поднимается по дереву до `ScrollViewer` и сообщает VM о сообщениях, видимых минимум на 50%; обычные чаты отмечаются через `MarkMessageAsRead`, private — через `MarkPrivateMessagesAsRead`.

### Действия над сообщением

- Контекстное меню пузыря повторяет веб-клиент: «Ответить», «Переслать», «Копировать текст», «Копировать изображение», «Закрепить»/«Открепить», «Изменить», «Удалить». Команды живут в `ViewModels/MessengerViewModel.MessageActions.cs`, условия видимости — вычисляемые свойства `MessageItemViewModel` (`CanUseActions`, `CanModify`, `CanCopyText`, `CanCopyImage`). `ContextMenu` находится вне визуального дерева, поэтому берёт контекст из `PlacementTarget.DataContext` и обращается к родительской VM через `MessageItemViewModel.Owner`. Для системных сообщений и приватных E2E-чатов меню целиком отключается через `ContextMenuService.IsEnabled`.
- «Ответить» и «Переслать» используют один серверный механизм — `OutgoingMessage.forwarded_message_id`. Ответ отправляется обычным `SendMessage` со ссылкой на оригинал; пересылка открывает модалку с мультивыбором чатов и необязательным комментарием и отправляет в каждый выбранный чат по очереди. Приватные чаты в список получателей не попадают: у них отдельный шифрованный путь отправки. Пересылка уже пересланного сообщения указывает на оригинал.
- Ответ и пересылка приходят обратно вложением `FORWARDED_MESSAGE`; оно исключается из медиа/файловых списков и рисуется `MessageQuoteControl` — кликабельной цитатой ответа, если оригинал есть среди загруженных сообщений, и блоком пересылки иначе.
- Панель над полем ввода обслуживает оба режима — правку и ответ. В режиме правки `SendCommand` вызывает `EditMessage` вместо отправки; отмена ответа не стирает набранный текст. Удаление подтверждается модалкой на скриме. Ошибки показываются строкой над полем ввода текстом, который отдаёт [[Клиенты/Windows-WebApiCore]].
- Плашка закреплённого сообщения (`Grid.Row="1"` в `MessengerView`) показывает счётчик `N/M` при нескольких закрепах, имя автора оригинала, превью на 80 символов и крестик открепления. Клик переходит к текущему закрепу и перелистывает на следующий по кругу. Закрепы загружаются вместе с историей через `ListPinnedMessages` и обновляются локально из ответов `PinMessage`/`UnpinMessage` — контракт см. [[Backend/Messages-PinnedMessages-ClientGuide]].
- `RealtimeMessengerService` запускает отменяемые `SubscribeToReadReceipts` и `SubscribeToPrivateMessagesRead`, а после refresh токена пересоздаёт их. События применяются на UI-потоке.
- `DpapiSecureSessionStore` изолирует токены, а `DpapiPrivateChatKeyStore` хранит выведенный AES-ключ accepted private-чата как DPAPI blob, изолированный по ноде, пользователю и чату. Если ключа нет, `MessengerView` запрашивает passphrase, локально вызывает `UnlockPrivateChat` и затем использует отдельные private API истории/отправки. Private API пока текстовый: защищённые вложения не реализованы.

Подробные правила написания кода: `Windows/BarkFluff.ClientV2.WPF/docs/Architecture.md`.

Матрица функций всех клиентов и бэклог V2 по этапам: `docs/clients-feature-matrix.md`.

Полная карта классов и ресурсов: [[Клиенты/Windows-WPF-V2-ProjectMap]].

## Первый реализованный маршрут

```
First run: Welcome → Select node → Connected node
Next runs: Select node → Connected node
```

`SelectNodeViewModel` получает публичные ноды из Navigator и позволяет вручную подключиться к Beacon. Адреса `http(s)://host[:port]` и `host[:port]` поддерживаются; домен без порта использует HTTPS/443, IP требует явно указанный порт. Перед каждым `GetServerInfo` [[Клиенты/Windows-WPF-V2-ProjectMap|NodeConnectionService]] пересоздаёт Beacon-клиент для выбранного адреса, поэтому возврат со входа и смена ноды не используют старый канал. `NodeConnectionMapper` переносит endpoint’ы сервисов в параметры сессии.

## Важные ограничения

- Кеш сообщений и локальные таблицы для него пока не реализованы.
- Realtime-синхронизация правок, удалений и закрепов между устройствами не подключена: `IRealtimeMessengerService` слушает только read-receipt'ы. Состояние обновляется локально после своего действия и подтягивается при открытии чата.
- Полный список закреплённых и «Открепить все» не реализованы: `UnpinAll` есть в proto, но не обёрнут в [[Клиенты/Windows-WebApiCore]].
- Переход к закрепу работает только если сообщение есть в загруженной истории; догрузка через `ListMessages(from_message_id)` не реализована.
- Панель ответа показывает текст оригинала без имени автора: имя отправителя на `MessageItemViewModel` не резолвится.
- Composer остаётся видимым, когда чат не выбран; отправка при этом просто ничего не делает.
- При добавлении токенов использовать отдельную миграцию SQLite и DPAPI; открытое хранение запрещено.
- Каталог рядом с exe должен быть доступен для записи; установка в `Program Files` требует отдельного решения прав доступа.
