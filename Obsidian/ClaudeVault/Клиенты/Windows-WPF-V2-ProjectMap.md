# BarkFluff.ClientV2.WPF — Project Map

Parent: [[Клиенты/Windows-WPF-V2]]

| Путь | Класс / ресурс | Назначение |
|---|---|---|
| `App.xaml(.cs)` | `App` | WPF UI resources, DI-container, запуск SQLite и стартовый маршрут |
| `MainWindow.xaml(.cs)` | `MainWindow` | FluentWindow-хост текущего onboarding ViewModel |
| `Models/` | `NodeProfile`, `PublicNode`, `NodeConnection` | Модели нод и результата подключения |
| `Services/NodeAddressParser.cs` | `NodeAddressParser` | Валидация и нормализация ручного адреса Beacon |
| `Services/NodeConnectionService.cs` | `NodeConnectionService` | Navigator, Beacon `GetServerInfo`, сохранение текущей сессии |
| `Services/NodeConnectionMapper.cs` | `NodeConnectionMapper` | Маппинг ответов Beacon в endpoint’ы `GlobalParam` |
| `Services/OnboardingNavigationService.cs` | `OnboardingNavigationService` | Переходы welcome / node selection / connected |
| `Infrastructure/Storage/` | `SqliteApplicationDataStore` | База `data/barkfluff.db`, настройки и выбранная нода |
| `Infrastructure/Storage/DpapiSecureSessionStore.cs` | `DpapiSecureSessionStore` | DPAPI-защищённое сохранение и восстановление access/refresh токенов |
| `Infrastructure/Localization/` | `LocalizationService` | Выбор RU/EN и swap XAML dictionary |
| `ViewModels/` | `Welcome`, `SelectNode`, `ConnectedNode`, `Login`, `Registration`, `PasswordRecovery` | Состояние и команды стартовых и auth-экранов |
| `ViewModels/MessengerViewModel.cs` | `MessengerViewModel`, `ChatItemViewModel`, `MessageItemViewModel`, `ForwardedContentViewModel` | Главный экран: список чатов, история выбранного чата и отправка обычных сообщений |
| `ViewModels/MessengerViewModel.MessageActions.cs` | `MessengerViewModel` (partial), `PinnedPreviewViewModel`, `ForwardTargetViewModel` | Действия над сообщением: копирование, закрепы, правка, удаление, ответ и пересылка |
| `Infrastructure/Behaviors/MessageListBehavior.cs` | `MessageListBehavior` | Прокрутка ленты к низу или к сообщению и отчёт о видимых сообщениях |
| `Views/` | `Welcome`, `SelectNode`, `ConnectedNode`, `Login`, `Registration`, `PasswordRecovery`, `Messenger` | XAML-представления на WPF UI |
| `Views/Controls/` | `MessageBubbleControl`, `MessageReadStatusControl`, `MessageQuoteControl`, `ChatListItemControl`, `ChatListSkeletonControl` | Пузырь сообщения с контекстным меню, галочки прочтения, цитата ответа/пересылки и элементы списка чатов |
| `Resources/Styles/Controls.xaml` | — | Общие стили контролов V2 |
| `Resources/Localization/Strings.*.xaml` | — | Синхронизированные английские и русские строки |
| `docs/Architecture.md` | — | Правила разработки V2 |
| `Tests/BarkFluff.ClientV2.WPF.Tests/` | xUnit | Тесты parser, mapper, SQLite и ViewModel |
