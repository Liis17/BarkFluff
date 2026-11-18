# BarkFluff.Client.WPF — карта исходников

Legacy WPF-клиент; обзор и основные контракты: [[Clients/Windows-WPF]].

Исходники: Windows/BarkFluff.Client.WPF/

| Путь | Ответственность |
|---|---|
| App.xaml.cs | Запуск, глобальное состояние, сохранение GlobalParam, single instance и регистрация сервисов |
| MessengerWindows/MainWindow.xaml(.cs) | Окно, трей и размещение MessengerPage |
| Pages/SetupPages/ и Pages/PinCode/ | Выбор сервера, вход/регистрация, восстановление пароля и PIN |
| Pages/MessengerPage.xaml(.cs) | Сборка страницы чатов и её контроллеров |
| Pages/Messenger/Controllers/ | Загрузка чатов и истории, отправка/прочтение, вложения, присутствие и уведомления |
| Services/App/RealtimeUpdateService.cs | Стрим новых сообщений и квитанций чтения |
| Services/App/OnlineStatusService.cs | Стрим и кэш статусов присутствия |
| Services/App/Caching/ | LiteDB-кэш истории и файлов |
| Services/App/Update/ | Проверка обновлений; запуск скрипта из MessengerPage.Viewers.cs |
| Services/Notification/ и Services/Erida/ | Windows toast и уведомления внутри окна |
| UserControls/SettingsPages/ | Настройки аккаунта, приватности, уведомлений, кэша и локального интерфейса |
| Resources/ | Стили, темы, RU/EN локализация, иконки и placeholders |

gRPC-фасад и модели находятся в [[Clients/Windows-WebApiCore]].
