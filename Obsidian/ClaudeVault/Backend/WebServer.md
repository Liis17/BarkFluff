# Barkfluff.WebServer

Публичный HTTP/MVC сайт, порт WEBSERVER_PORT или fallback 64641. Program явно настраивает Kestrel; launchSettings объявляет 5186/7208, но не задаёт фактический listener. Docker/nightly/website/docker-compose.yml задаёт runtime port через environment. Код: Backend/Barkfluff.WebServer/, карта: [[Backend/WebServer-ProjectMap]].

## Страницы и API

HomeController отдаёт / и /about. FallbackController обслуживает /legal/{privacy-policy,terms-of-service,account-deletion,encryption}, /selfhosted и публичные profile pages; прочие пути завершаются HTML 404. Profile path допускает только ^[a-zA-Z0-9_]{3,32}$ и HTML-экранируется перед подстановкой. /api/user/{username} возвращает публичные данные через UsersServerApi, с 30-минутным кешем найденных профилей и 5-минутным negative cache.

Другие endpoints: /api/versions (Android, Windows/WinUI и macOS, четыре release channels), /api/support/send и /api/support/messages/{chatId}. Support API принимает до 4000 символов и сохраняет локальную историю; пересылка в Telegram может не пройти, при этом сообщение остаётся сохранено. /install*.ps1/.sh и /download/installer сохраняют legacy установочные маршруты.

## Клиенты и содержимое

UsersServerApi client вручную прикладывает plain x-auth-token service token. VersionPollingService опрашивает ClientStorage каждые 10 минут по Android Kotlin, WinUI и macOS для release/beta/dev/nightly; iOS не входит в /api/versions.

LegalPageService отдаёт существующие HTML-файлы из html/legal; Markdown языковые файлы в этой папке напрямую этим маршрутом не читаются. Cookie notice находится в files/cookie-notice.js. Статические страницы и юридические тексты лежат в html/.

/about, /selfhosted и /legal/* используют шапку главной (.home-nav) из files/site-header.css, подключаемого после files/site-theme.css; оба CSS входят в whitelist AssetsController и публикацию csproj. /selfhosted двуязычна: фрагменты продублированы элементами lang="ru"/lang="en", лишний язык скрывает site-theme.css, выбор берётся из bf_lang; при правке текста меняются обе версии.

Сборка: dotnet build Backend/Barkfluff.WebServer/Barkfluff.WebServer.csproj.
