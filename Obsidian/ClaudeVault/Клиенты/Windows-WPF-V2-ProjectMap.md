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
| `Infrastructure/Localization/` | `LocalizationService` | Выбор RU/EN и swap XAML dictionary |
| `ViewModels/` | `Welcome`, `SelectNode`, `ConnectedNode` | Состояние и команды трёх стартовых экранов |
| `Views/` | `Welcome`, `SelectNode`, `ConnectedNode` | XAML-представления на WPF UI |
| `Resources/Styles/Controls.xaml` | — | Общие стили контролов V2 |
| `Resources/Localization/Strings.*.xaml` | — | Синхронизированные английские и русские строки |
| `docs/Architecture.md` | — | Правила разработки V2 |
| `Tests/BarkFluff.ClientV2.WPF.Tests/` | xUnit | Тесты parser, mapper, SQLite и ViewModel |
