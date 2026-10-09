# Barkfluff.WebServer — карта проекта

Назначение и контракт: [[Backend/WebServer]]. Исходники: Backend/Barkfluff.WebServer/.

- Program.cs задаёт HTTP listener, MVC и UsersServerApi client.
- Controllers/ — главная/about, fallback для legal/selfhosted и user pages, REST профиля/версий/support, legacy install/download.
- Services/UserPageService.cs валидирует username и экранирует HTML; UserProfileService.cs вызывает Users по service token и кеширует ответы 30 минут (отрицательный результат — 5 минут).
- SupportChatService.cs хранит сообщения поддержки локально; TelegramService.cs пересылает их администратору.
- VersionPollingService.cs опрашивает ClientStorage по 12 версиям Android, WinUI и macOS каждые 10 минут; REST выдаёт эти 3 платформы.
- html/ и files/ — статические страницы, юридические HTML и assets; LegalPageService отдаёт статические HTML шаблоны.
- files/site-theme.css — общая палитра и оформление /selfhosted и legal; files/site-header.css — общая шапка .home-nav для /about, /selfhosted и /legal/*.

Runtime-порт задаётся WEBSERVER_PORT, по умолчанию 64641; локальный launch profile использует 5186/7208. Docker/nightly/website/docker-compose.yml подставляет WEBSERVER_PORT.
