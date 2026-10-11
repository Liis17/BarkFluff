# Barkfluff.WebServer

Публичный HTTP-сервер. Порт: **64641** (.NET 10, ASP.NET Core MVC).
Раздаёт HTML-страницы, статику и файлы, предоставляет REST API профилей, версий клиентов и чат поддержки через Telegram-бота.

Расположение: `Backend/Barkfluff.WebServer/`

> 📂 Подробная карта всех файлов и классов: [[Backend/WebServer-ProjectMap]]

## Сборка

```bash
dotnet build Barkfluff.WebServer.csproj
dotnet publish Barkfluff.WebServer.csproj -c Release -r linux-x64 --self-contained
```

## Controllers

| Controller | Маршрут | Описание |
|------------|---------|----------|
| `HomeController` | `GET /`, `GET /about` | Отдаёт `html/barkfluff.html` и `html/about.html` |
| `InstallController` | `GET /install.ps1`, `GET /installbeta.ps1`, `GET /install.sh`, `GET /installbeta.sh` | Legacy-скрипты установки, оставлены для совместимости; на главной больше не показываются |
| `DownloadController` | `GET /download/installer` | `Barkfluff.Updater.CLI.exe` |
| `FallbackController` | `GET /{**catchAll}` | `legal/*` → LegalPageService; `selfhosted` → LegalPageService; иначе → UserPageService; пусто в ответе → `html/404.html` с кодом 404 |
| `UserApiController` | `GET /api/user/{username}` | REST API профиля |
| `VersionApiController` | `GET /api/versions` | Версии Android, WinUI и macOS по каналам `release`, `beta`, `dev`, `nightly` |
| `SupportChatController` | `POST /api/support/send`, `GET /api/support/messages/{chatId}` | Чат поддержки |
| `AssetsController` | `GET /assets/{filename}` | Ассеты (изображения для магазинов и превью) |
| `FaviconController` | `GET /favicon.ico` | Иконка сайта |

**Важно**: `FallbackController` перехватывает все необработанные пути — специфичные маршруты ASP.NET Core расставляет в приоритет автоматически.

## Services

- **`UserProfileService`** — gRPC запросы профилей, кеш 30 мин (не найденных — 5 мин, `IMemoryCache`). Возвращает `UserProfileData` включая `ProfilePosterUrl`.
- **`UserPageService`** — читает `html/userpage.html`, заменяет `%%username%%`. Специальная обработка: если username == `li_is` (без учёта регистра), отдаётся `html/UniqueUsers/paws.page.html` с анимированными лапками на фоне.

> **Валидация пути.** До подстановки путь проверяется регексом `^[a-zA-Z0-9_]{3,32}$` — тем же, что `UsernameFormatValidator` в [[Backend/Users]]. Не подошло (слэши, кавычки, теги, длина) → пустая строка → `FallbackController` отдаёт 404. Подстановка дополнительно проходит `HtmlEncoder` — `%%username%%` попадает не только в разметку, но и в JS-строку `const USERNAME = "…"` шаблона, поэтому одной валидации мало, если формат username когда-нибудь расширят.
>
> Существующий username от несуществующего страница **не отличает** — оба дают 200 с шаблоном, факт регистрации выясняет уже клиентский `fetch('/api/user/…')`. Это осознанно: по коду ответа нельзя перебрать список аккаунтов. 404 отдаётся только на то, что username быть не может в принципе, поэтому о пользователях он ничего не сообщает.

> **Кнопка «Написать в браузере» (`#webChatBtn`)** в обоих шаблонах (`userpage.html` + `paws.page.html`): при клике пишет cookie `bf_open_chat=<username>` (`domain=.barkfluff.com`, `max-age=300`, `SameSite=Lax`) и редиректит на `https://web.barkfluff.com`. Веб-мессенджер [[Backend/Web]] после загрузки читает эту cookie и открывает чат с пользователем (см. `maybeOpenChatFromCookie` в `main.js`). Логика повторяет deep-link Android (`bf://user-username=<username>`).
>
> Кнопка показывается **на всех устройствах**. До этого её гасил `@media (hover: none), (pointer: coarse)` — правило существовало с момента добавления кнопки и скрывало её не только на телефонах, но и на планшетах и тач-ноутбуках. Это расходилось с карточкой «Веб — Любой современный браузер» на главной (`barkfluff.html`), которая ведёт на `web.barkfluff.com` без ограничений по устройству.
- **`LegalPageService`** — файлы из `html/legal/` по имени страницы; также обрабатывает `selfhosted.html`
- **`SupportChatService`** — in-memory сессии чата (`ConcurrentDictionary`)
- **`TelegramService`** — Telegram-бот для уведомлений чата поддержки. Ответ администратора — reply, первая строка — GUID чата
- **`VersionStore`** — thread-safe in-memory хранилище актуальных версий Android, WinUI и macOS для каналов `release`, `beta`, `dev`, `nightly`
- **`VersionPollingService`** — `BackgroundService`, каждые 10 минут опрашивает `barkfluffkotlin`, `barkfluffwinui` и `barkfluffmacos` во всех четырёх каналах и обновляет `VersionStore`

## Главная и каналы сборки

Ссылка `Self-hosted` в шапке главной открывает руководство `/selfhosted`.
Значок в шапке берётся из `/favicon.ico`, как и в [[Клиенты/Developers-Web]].

`GET /` отдаёт реальную страницу `html/barkfluff.html`. На ней переключатель `Nightly / Dev / Release` меняет ссылки native-клиентов, отображаемые версии и тему всей страницы. По умолчанию выбран `Release`. Главная сохраняет композицию t3.codes: тонкая сетка в hero, центрированный крупный заголовок и большие отступы между разделами. Release использует прежнюю светлую палитру из `files/site-theme.css`: бело-голубой фон, тёмный текст и тёплая основная кнопка; Dev и Nightly — почти чёрный фон с белой кнопкой. Типографика заголовков основана на cubicle.fooxboy.net: Montserrat Black (900) с кириллицей, прописные буквы, плотный интерлиньяж и отрицательный tracking; последняя строка hero выделена плашкой акцентного цвета. Основной текст остаётся Manrope, служебные подписи — JetBrains Mono. Акцент Release — тёплый, Dev — голубой, Nightly — лиловый. Поверхности карточек, превью, иконки, статусы и чат поддержки следуют палитре выбранного канала. Web-клиент вынесен отдельным блоком над переключателем каналов и всегда ведёт на `https://web.barkfluff.com`, независимо от выбранного канала. Заголовок раздела загрузок одинаков для всех каналов; для меняющегося описания зарезервированы две строки, чтобы переключение веток не сдвигало платформы. Превью занимает ширину страницы: Windows / Android / Web выбираются кнопками под ним, карточками платформ или стрелками клавиатуры. Изображения показываются целиком (`object-fit: contain`), подсказка скрывается на мобильной ширине. Клавиатурное переключение и `prefers-reduced-motion` отключают анимацию превью.

Платформы собраны в две цельные панели с тонкими разделителями вместо отдельных карточек: компьютер (Windows WinUI, macOS, Linux — скоро) и телефон (Android, iOS — скоро). Строка содержит иконку в небольшой плашке, крупное название, требования, статус, версию и нейтральную контрастную кнопку скачивания. Выбранная платформа отмечается тонкой акцентной линией; недоступные платформы сохраняют читаемый текст. На ширине до 1000 px панели идут последовательно, на телефоне действие и версия переносятся под описание. Четыре карточки возможностей находятся сразу после hero, перед загрузками. Внизу расположен раздел `#developers`: .proto-файлы для своих клиентов (публичный каталог `Shared/BarkFluff.Proto` на GitHub и портал документации), репозиторий BarkFluff и короткий блок со ссылкой на `/selfhosted`. Все новые подписи локализованы на RU/EN. Рабочие ссылки строятся как `/get/barkfluffwinui/{channel}`, `/get/barkfluffkotlin/{channel}` и `/get/barkfluffmacos/{channel}`. Если версия выбранного канала ещё не опубликована, карточка показывает состояние ожидания.

## Информация о проекте и история домена (2026-10-09)

Главная содержит описание независимого некоммерческого мессенджера с открытым исходным кодом и ссылки на GitHub и `/about` в навигации и подвале, включая мобильную версию. Главная и страница «О проекте» имеют description, canonical и Open Graph/Twitter metadata.

`GET /about` — публичная статическая страница, специфичный маршрут `HomeController` имеет приоритет над профилями `FallbackController`. Тексты RU/EN находятся непосредственно в HTML; язык следует общему `localStorage.bf_lang`, по умолчанию определяется по языку браузера. Содержимое: назначение и стадия проекта, история, команда, MIT, ссылки на исходники и контакт поддержки. Датой начала считается первый коммит `ce2090cd9877c2a4ad73ce51f5a8025c22bf7f92` от 5 апреля 2025 года. Передача домена примерно в мае 2025 года указана как информация нынешнего владельца. Некоммерческий характер проекта не ограничивает права по MIT.

Команда: `li_is` — главный разработчик (`li_is@barkfluff.com`), `fooxboy` — backend и [[Клиенты/iOS]] (`fooxboy@barkfluff.com`), `kotumbus` — [[Клиенты/Linux]] (`kotumbus@barkfluff.com`).

Вверху обеих страниц — закрываемая плашка о том, что проект не связан с прежним магазином товаров для животных. Общие `files/domain-notice.js` и `files/domain-notice.css` раздаются через whitelist `AssetsController` и включены в публикацию. Закрытие сохраняется на origin сайта как `localStorage.bf_domain_notice_v1 = dismissed` и действует на обе страницы. Скрипт следит за изменением `html.lang` для RU/EN. При недоступном localStorage закрытие действует только на текущей странице. Постоянное пояснение об использовании домена магазином в 2022 году остаётся в `/about#domain-history`.

## REST API — `/api/user/{username}`

Возвращает публичный профиль пользователя:

```json
{
  "found": true,
  "firstName": "...",
  "lastName": "...",
  "username": "...",
  "bio": "...",
  "profilePicture": "https://...",
  "profilePosterUrl": "https://..."   // пусто если постер не задан
}
```

`profilePosterUrl` — URL постера/обложки профиля (горизонтальное изображение 3:1, отображается за аватаром в `.pc-cover`).

## gRPC Client

`UsersServerApiClient` создаётся вручную через `GrpcChannel.ForAddress` в `Program.cs` (не через `AddGrpcClient`). `x-auth-token` передаётся вручную в `Metadata` при каждом вызове.

Адрес Users-сервиса и токен читаются из конфигурации (`UsersService:Host` / `UsersService:Token`) в `Program.cs` — без hardcoded-значений.

## Статика

- `html/barkfluff.html` — главная страница
- `html/about.html` — публичная страница «О проекте», RU/EN, история, команда, исходники и пояснение о домене
- `html/404.html` — страница «не найдено» (палитра главной, RU/EN по `localStorage.bf_lang`, шрифт не грузится извне — только системный fallback)
- `html/userpage.html` — шаблон страницы пользователя
- `html/UniqueUsers/paws.page.html` — **специальная** страница для пользователя `li_is`: как userpage, но с анимированными полупрозрачными лапками-следами (SVG, CSS keyframes) на заднем плане
- `html/selfhosted.html` — инструкция по развёртыванию своей ноды, см. ниже
- `html/legal/*.html` — юридические страницы **для сайта** (RU+EN в одном файле через `<article data-lang>`, переключатель на клиенте)
- `html/legal/*.md` — те же документы **для клиентов**, см. ниже
- `files/cookie-notice.js` — баннер об использовании cookie, см. ниже
- `files/domain-notice.js`, `files/domain-notice.css` — общая плашка об истории домена на главной и `/about`
- `html/new/` — **WIP** редизайн главной страницы (Barkfluff Redesign.html, profile.html, стили)
- `files/install.ps1`, `files/installbeta.ps1` — скрипты установки Windows
- `files/install.sh`, `files/installbeta.sh` — скрипты установки Linux
- `files/channel-nightly.webp`, `files/channel-dev.webp`, `files/channel-release.webp` — web-оптимизированные производные фонов macOS Packaging без Finder-зон
- `files/Barkfluff.Updater.CLI.exe` — инсталлятор (не в git)

## Cookie-уведомление (`files/cookie-notice.js`)

Информационный баннер внизу страницы: одна кнопка «Понятно», отказа нет. Сайт ставит только cookie, без которых не работают чат поддержки и переход в веб-клиент; аналитики и рекламы нет, поэтому категорий и тумблеров тоже нет.

Общего layout у `html/` не существует — каждая страница самостоятельна. Поэтому баннер сделан **одним внешним файлом** и подключается строкой `<script src="/assets/cookie-notice.js" defer></script>` перед `</body>`. Раздаётся через whitelist `AssetsController._allowedFiles` (там же прописан `text/javascript`), в `.csproj` добавлен `<Content Include="files\cookie-notice.js">`.

⚠️ Новая страница сайта → добавить эту строку вручную. Сейчас подключено в 10 файлах: `barkfluff.html`, `about.html`, `404.html`, `userpage.html`, `selfhosted.html`, `UniqueUsers/paws.page.html`, все четыре `legal/*.html`. Каталог `html/new/` (WIP-редизайн) не подключён.

- Локализация RU/EN — собственный словарь `S` внутри скрипта, язык берётся из `document.documentElement.lang` / `localStorage.bf_lang` (та же схема, что у `barkfluff.html` и `legal/*.html`). Свой обработчик на `#langToggle`, потому что `applyLang` главной страницы работает по фиксированному списку id.
- Стили инжектит сам скрипт, цвета через `var(--panel, var(--bg-2, …))` и т.п. — наборы CSS-переменных у главной страницы и legal-страниц разные.
- По «Понятно» пишется cookie `bf_cookie_notice=1` (`path=/`, 1 год, `SameSite=Lax`), на `*.barkfluff.com` — ещё и `domain=.barkfluff.com`, чтобы баннер не всплыл повторно на `web.barkfluff.com`. Веб-клиент использует **то же имя и значение** (см. [[Backend/Web]]).
- Версия уведомления — константа `VERSION` в скрипте. Изменился состав cookie → бампнуть, и баннер покажется заново.

## Cookie сайта

| Cookie | Где ставится | Срок |
|---|---|---|
| `barkfluff_chat_id` | `barkfluff.html`, сессия чата поддержки | 1 год |
| `bf_open_chat` | `userpage.html` / `paws.page.html`, переход в веб-клиент | 300 c |
| `bf_cookie_notice` | `files/cookie-notice.js` | 1 год |

Все три перечислены в разделе «10. Cookies и локальное хранение» Политики конфиденциальности — вместе с cookie веб-клиента (`bf_theme`, `bf_legal_accepted`). При добавлении новой cookie раздел надо обновить **и** в `privacy-policy.html`, **и** во всех пяти `PRIVACY_POLICY.*.md`.

## Юридические документы — два формата

### Оформление публичных документов

`/selfhosted` и четыре `/legal/*` используют общий `files/site-theme.css`: светлая палитра канала Release главной, Manrope/JetBrains Mono, фон `channel-release.webp` с сеткой, логотип из favicon, единая навигация и светлые карточки. Главная использует светлую палитру Release из этого файла; её собственные стили задают тёмную базу Dev/Nightly и поверхности интерфейса по каналам. Светлое оформление документов задаётся общим файлом. CSS раздаётся whitelist `AssetsController` и включён в публикацию приложения.

Стиль документации ограничен классом `document-page`; руководство имеет `selfhosted-page`, документы — `legal-page`. Таблицы legal находятся в прокручиваемых `.scroll-x`, примеры команд в self-hosted прокручиваются внутри `pre`. Сохраняются RU/EN и `bf_lang` у legal, русский язык руководства, копирование команд и общее cookie-уведомление. Редизайн меняет только оформление и навигацию; тексты, даты редакций и Markdown для клиентов не менялись.

`html/legal/` содержит каждый документ в двух видах, и они обслуживают разных потребителей:

| Формат | Кто использует | Локализация |
|--------|----------------|-------------|
| `*.html` (kebab-case: `privacy-policy.html`) | сайт через `LegalPageService` | RU+EN в одном файле, `<article data-lang>` + `localStorage.bf_lang` |
| `*.md` (`PRIVACY_POLICY.<lang>.md`) | мобильные клиенты и веб-клиент, попадают в сборку | отдельный файл на локаль |

Markdown-версии:

- `TERMS_OF_SERVICE.{ru,en,de,es,zh-CN}.md`, `PRIVACY_POLICY.{ru,en,de,es,zh-CN}.md`
- `ACCOUNT_DELETION.md`, `ENCRYPTION.md` — только RU, в онбординге клиентов не показываются
- **`.ru.md` — оригинал**, остальные локали переведены с него; во всех не-русских версиях стоит оговорка о преимущественной силе русской версии
- структура заголовков, нумерация разделов и таблицы совпадают 1:1 между локалями — это нужно, чтобы сверять их при обновлении документа
- дата «Последнее обновление» в шапке используется клиентами как **версия редакции**: изменилась дата — согласие запрашивается заново
- в `.csproj` включены маской `html\legal\*.md`, новая локаль не требует правки проекта

⚠️ При правке документа надо обновить **и** `.html` (сайт), **и** соответствующие `.md` (клиенты) — автоматической синхронизации между форматами нет.

Android-клиент забирает `.md` на этапе сборки gradle-таском `copyLegalDocs`, см. [[Клиенты/Android]]. Веб-клиент — MSBuild-таргетом `CopyLegalDocs` в `BarkFluff.Web.csproj`, см. [[Backend/Web]]. Оба читают отсюда, копий-источников нет.

### Модель ответственности (редакция от 29.07.2026)

Документы переписаны под федеративную схему ([[Backend/Federation]]):

- **оператор персональных данных — администратор ноды**, на которой создан аккаунт; разработчик публикует ПО, чужие ноды не хостит, доступа к их данным не имеет и оператором не является;
- тексты написаны от лица ноды **barkfluff.com**; администратор другой ноды публикует свою редакцию;
- контакты разделены: `privacy@`/`support@`/`legal@` — администратор ноды, `security@` — разработчик (уязвимости и протокол);
- в Privacy добавлен раздел «Федеративный обмен данными» (что уходит на другую ноду и когда), в ToS — «Роли и зона ответственности» + «Что означает федерация для вас», в Account Deletion — «Границы удаления в федерации», в Encryption — «Защита канала между нодами» и сводка «кто что видит».

Утверждения, которые надо пересматривать при развитии федерации, — они сверены с кодом на дату редакции:

| Утверждение в документах | Основание |
|---|---|
| Федеративные чаты только 1-к-1, групп нет | roadmap: федеративные группы — «Дальше» |
| Федеративные сообщения **не** E2E; private/secret — только внутри ноды | roadmap: федеративные E2E — «Дальше» |
| Удаление аккаунта по федерации **не** распространяется | этап 2.9 не сделан: `ProfileChanged`/`UserDeactivated` → `EventStatus.Retry` в `FederationS2SApiService` |
| Файлы не реплицируются, отдаются потоком по запросу | этап 3.2, `FetchFile` |
| Федерация выключена по умолчанию | `Federation:Enabled = false` |
| Можно запретить входящие федеративные ЛС | `DenyFederatedDm` в [[Backend/Users]] |

## Страница self-hosted (редакция от 29.07.2026)

`html/selfhosted.html` — руководство администратора ноды. Раздаётся `LegalPageService` по пути `/selfhosted`, RU-only (в отличие от `legal/*.html` — переключателя языка нет).

Содержимое выведено из кода, а не из общих слов; при изменениях в этих местах страницу надо править:

| Блок страницы | Источник |
|---|---|
| `docker-compose.yml` | `docker/backend/docker-compose-dev-backend.yml`, образы без `-dev` (реальное имя релизного образа — `image:` в `.github/workflows/build-backend-*.yml`) |
| `.env` | `docker/backend/sample-backend.env` |
| Таблица субдоменов | `server_name` в `docker/nginx/*.conf` + каталог Settings |
| «Остаются пустыми» (`Email`, `ServerProps`, `ServerColor`, S3/LiveKit credentials) | каталог Settings; эти значения вводятся через Setup UI |
| «Автозаполнение поставило значение» | `SettingsCatalog`: `ExternalEndpoint:Host` → `*.example.com`, S3/LiveKit credentials остаются пустыми, `NavigatorUrl` → `http://navigator:7010` |
| Федерация | `Federation:ServerName`/`ExternalEndpoint`/`TlsSpkiSha256` пустые по замыслу, `Enabled=false` — см. [[Backend/Federation]] |

Отличия релизного compose на странице от dev-варианта: образы без `-dev`; порт postgres не публикуется; `seq`/`minio` без проброса портов; `minio` раскомментирован; у `livekit` 7880 не публикуется (за nginx); убраны сервисы и переменные, специфичные для инфраструктуры barkfluff.com (`developers`, `Mail__*`, `TELEGRAM_PROXY_*`). Удалённые SSH-серверы настраиваются в AdminPanel и не относятся к Compose/.env.

Настройки внутренних адресов теперь задаются через `SETTINGS_SERVICE_HOST=http://settings:7003`; старые deployment-примеры не следует копировать.

## Proto

- `users_api.proto` — Client
- `shared.proto` — None

## Зависимости

- `Telegram.Bot 22.10.0.1`
# Метрики

WebServer отправляет в [[Backend/GrpcServer]] HTTP-запросы и необработанные HTTP-ошибки, успешные скачивания installer и сохранённые обращения в support-чат (`support_requests`). Эти показатели доступны в [[Backend/AdminPanel]].
