# BarkFluff.Web — карта проекта

ASP.NET Core-хост браузерного клиента с режимами Node, Shell и Proxy. Пути `Program.cs`, `Infrastructure/`, `wwwroot/` и другие пути компонента ниже отсчитываются от `Backend/BarkFluff.Web/`; `Shared/` и `Docker/` — от корня репозитория.

## Сервер

- `Program.cs` настраивает YARP-маршруты: Node связывает gRPC-Web и HTTP upload с сервисами ноды; Shell оставляет API Navigator для каталога и выбора ноды; Proxy передаёт запросы Node-хосту и релеирует разрешённые media-хосты.
- Node создаёт `/ping/{service}` для внутренних health probes. `/ping/navigator` использует отдельный `NavigatorService:HealthHost`; Shell не создаёт ping-маршруты, Proxy передаёт их на Node.
- `Infrastructure/GrpcWebResponseTrailersFeature.cs` сохраняет trailers gRPC-Web. `nginx/web.conf` — конфигурация nginx рядом с приложением; внешний Proxy-деплой описан в `Docker/nightly/proxy/docker-compose.yml` и `Docker/nightly/proxy/nginx/sites/proxy.conf`; основной ingress ноды — `Docker/nightly/nginx/sites/web.conf`.
- `appsettings*.json`, переменные среды и Settings задают `Web:Mode`, адреса сервисов, CORS, `Web:Proxy:Target` и allowlist media-хостов. Shell и Proxy пропускают загрузку Settings и используют локальную конфигурацию.

## Browser client

- `wwwroot/index.html`, `wwwroot/messenger.html`, `wwwroot/legal/` и `wwwroot/offline.html` — страницы и оболочка приложения; `wwwroot/css/`, `wwwroot/icons/`, `wwwroot/js/proto/` — стили, ресурсы и сгенерированный proto-бандл.
- `wwwroot/js/app/` разделён по областям: авторизация, API и соединение, сообщения и realtime, файлы, presence и звонки, папки и закреплённые сообщения, профиль, настройки и поиск.
- `scripts/build-app.js` собирает клиент; `scripts/generate-proto.*` обновляют proto-бандл из `Shared/BarkFluff.Proto`.

← [[Backend/Web]]
