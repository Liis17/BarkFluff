# Barkfluff.WebServer — карта проекта

Назначение и контракт: [[Backend/WebServer]]. Исходники: Backend/Barkfluff.WebServer/.

- Program.cs задаёт HTTP listener, MVC и UsersServerApi client.
- Controllers/ — главная/about, fallback для legal/selfhosted и user pages, REST профиля/версий/support, legacy install/download.
- Services/UserPageService.cs валидирует username и экранирует HTML; UserProfileService.cs вызывает Users по service token и кеширует ответы 30 минут (отрицательный результат — 5 минут).
- SupportChatService.cs хранит локальную историю поддержки в памяти процесса, без кода периодической очистки; перезапуск теряет её. TelegramService.cs пересылает сообщения администратору; оператор сообщает об автоматической очистке Telegram-чата поддержки раз в неделю.
- VersionPollingService.cs опрашивает ClientStorage по 12 версиям Android, WinUI и macOS каждые 10 минут; REST выдаёт эти 3 платформы.
- html/ и files/ — статические страницы, юридические HTML и assets; LegalPageService отдаёт четыре фиксированные legal HTML-страницы, неизвестный slug даёт 404. Markdown privacy/terms используются Web- и Android-клиентами как локализованные документы согласия; версия согласия берётся из заголовка TERMS_OF_SERVICE.ru.md, а версия cookie notice задаётся отдельно.
- html/barkfluff.html — главная без галереи скриншотов клиентов; удалённые изображения клиентов не публикуются csproj.
- files/site-theme.css — общая палитра и оформление /selfhosted и legal; files/site-header.css — общая шапка .home-nav для /about, /selfhosted и /legal/*.

Runtime-порт задаётся WEBSERVER_PORT, по умолчанию 64641; локальный launch profile использует 5186/7208. Docker/nightly/website/docker-compose.yml подставляет WEBSERVER_PORT.
