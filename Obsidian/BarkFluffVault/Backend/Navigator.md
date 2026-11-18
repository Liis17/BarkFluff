# BarkFluff.Navigator

Публичный каталог нод. gRPC/HTTP2 default port 7010, отдельный HTTP/1 port 7011; appsettings и NAVIGATOR_PORT/NAVIGATOR_HTTP_PORT задают runtime. Program явно конфигурирует Kestrel, поэтому launch profile с 5260/7137 не задаёт фактические listeners. Nightly nginx-конфиг проксирует HTTP на 64647 и gRPC на 64646, поэтому используемый .env должен совпадать с этими upstream-портами.

Navigator самостоятельный: SQLite, Serilog, metrics, XAuth и shared health; он не вызывает LoadConfiguration. Код: Backend/BarkFluff.Navigator/. Контракт: Shared/BarkFluff.Proto/navigator_api.proto.

## Публичный контракт

gRPC: ListServers, RegisterServer, GetServerByName; HTTP /api/servers — публичный список для сайта, /admin/api/* — cookie-auth React админка. Админ cookie __Host-navigator-admin — Secure, HttpOnly, SameSite Strict, sliding expiry 8 часов. Публичный каталог возвращает автоматические регистрации, активные за 10 минут, и pinned manual entries; ключи хранятся локально. RegisterServer обновляет запись по ServerName, а для legacy — Name+BeaconHost+Port; регистрация throttled (default 2 минуты). GetServerByName ищет активный ServerName.

Нода автоматически регистрируется из Beacon каждые 5 минут. Manual записи не истекают и удаляются только через admin endpoint.

## Persistence и федерация

SQLite создаётся через EnsureCreated; EF migrations в проекте нет. Для существующей базы startup вручную добавляет WebEndpoint, FilesMediaEndpoint и IsManual — новые поля следует учитывать в EnsureServersColumn. Валидация федеративной ServerName/ключей выполняется при регистрации.

Публичные RPC не имеют общей Authorize policy в NavigatorApiService; XAuth middleware в host не равен обязательной аутентификации для каждого метода. Reflection включается только в Development.
