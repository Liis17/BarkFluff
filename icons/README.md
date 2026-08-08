# BarkFluff — иконки

Векторные SVG-иконки платформы (микросервисы, действия с сообщениями, типы устройств и папки чатов) в стиле **Material 3 Expressive Iconography** (см. [`docs/material-3-expressive-guidelines.md`](../docs/material-3-expressive-guidelines.md), раздел 16).

## Стиль (Material Symbols)

Следуем спецификации M3 для собственных иконок (раздел «Designing icons»):

- **Pixel grid**: `viewBox="0 0 24 24"`, padding 2dp от края — глиф не выходит за пределы safe-área.
- **Stroke**: 2dp (`stroke-width="2"`), `stroke-linecap`/`stroke-linejoin: round` — вариант **Rounded** из трёх стилей Material Symbols (Outlined / Rounded / Sharp).
- **FILL по умолчанию 0** (outline): заливка `currentColor` используется только точечно — для мелких деталей, которые нечитаемы контуром (точки-индикаторы, стрелки, узлы сети), как это делает сам набор Material Symbols.
- **Монохром**: никаких цветов внутри файла — только `currentColor`. Иконка наследует цвет из CSS/темы (`on-surface`, `on-surface-variant` и т.п. — по контексту использования).
- **Оптический, не геометрический центр** — глиф визуально сбалансирован в границах 24×24, а не просто отцентрирован по bounding box.
- **Без встроенного контейнера.** В M3 иконка и контейнер — разные сущности (Iconography vs Containment-тактика, разделы 2.5 и 16). Плитку/заливку вокруг иконки добавляет компонент, в который она вставляется (см. ниже), а не сам SVG-файл.

### Контейнер (containment) — на уровне компонента, не в SVG

Если иконке нужен фон (список сервисов, карточка, аватар), собирай его снаружи из M3-токенов, а не запекай в файл:

- Форма: `corner.full` (круг) для иконки-аватара/бейджа сервиса, либо `corner.medium`/`corner.large` (12–16dp) для плиток в сетке — см. раздел 13 (Shape).
- Цвет: нейтральный `surface-container-high` + иконка `on-surface-variant` для монохромного варианта; `*-container`/`on-*-container` пары — если решите присвоить сервису акцентный цвет (пока не делаем, см. историю проекта).
- Размер контейнера: 40dp для leading-иконок в списках (раздел 17, Lists).

## Структура

```
icons/
  services/
    <service>.svg
  message-actions/
    <action>.svg
  devices/
    default-<device>.svg
  folders/
    <folder-purpose>.svg
  settings/
    <setting-category>.svg
  chat/
    <chat-purpose>.svg
```

Каждая категория — своя подпапка, имя файла (kebab/lowercase) = имя сервиса/действия/устройства, системный ключ назначения папки, категории настроек или назначения элемента чата.

## Сервисы и символика

| Файл | Сервис | Идея глифа |
|---|---|---|
| `configuration.svg` | Configuration — централизованная конфигурация | ползунки настроек |
| `beacon.svg` | Beacon — точка входа клиентов | маяк с сигнальными дугами |
| `navigator.svg` | Navigator — реестр серверов | компас со стрелкой |
| `identity.svg` | Identity — auth, JWT, 2FA, сессии | щит с галочкой |
| `users.svg` | Users — профили, устройства, бейджи | силуэт + значок-бейдж |
| `messages.svg` | Messages — чаты, сообщения, вложения | пузырь чата с точками |
| `files.svg` | Files — файлы, S3, стикеры | папка |
| `updates.svg` | Updates — real-time стриминг событий | круговые стрелки обновления |
| `onliner.svg` | Onliner — онлайн-статусы | точка присутствия с кольцами |
| `notification.svg` | Notification — email-уведомления | колокольчик |
| `fastauth.svg` | FastAuth — QR-авторизация устройств | QR-рамка с молнией |
| `adminpanel.svg` | AdminPanel — веб-дашборд администратора | столбчатая диаграмма |
| `cloudmessaging.svg` | CloudMessaging — push-уведомления (Firebase) | облако со стрелкой push |
| `web.svg` | Web — gRPC-Web прокси + статика | глобус |
| `webserver.svg` | WebServer — публичный HTTP-сервер | серверная стойка |
| `clientstorage.svg` | ClientStorage — хранилище клиентских приложений | архивная коробка |
| `developers.svg` | Developers — портал документации | код-скобки `</>` |
| `calls.svg` | Calls — звонки (аудио/видео) | телефонная трубка |
| `bots.svg` | Bots — Bot API | голова робота |
| `federation.svg` | Federation — межсерверная федерация (S2S) | связанные узлы-серверы |

Не включены: `GrpcServer` (shared-библиотека, не самостоятельный сервис), `Users.Rust` (экспериментальный drop-in порт Users, та же роль) и `Nginx` (инфраструктурный reverse proxy, не сервис приложения).

## Действия с сообщением и символика

`reply`/`forward`/`copy-plain`/`copy-image`/`pin`/`edit`/`delete` — **1:1 те же иконки**, что в контекстном меню сообщения веб-клиента (`Backend/BarkFluff.Web/wwwroot/messenger.html`, `#bf-icon-*` symbols, `copy-plain` = их `bf-icon-copy`). Там же 18×18 при viewBox 24×24, `stroke-width:2`, `round` caps/joins, `stroke: currentColor` — тот же язык, что и у сервисных иконок, просто более плотный рисунок (это и делает их «красивее»). Остальные (`copy-markdown`, `download`, `properties`, `select`, `more`) в веб-меню нет — добавлены в том же визуальном весе.

| Файл | Действие | Идея глифа |
|---|---|---|
| `reply.svg` | Ответить | изогнутая влево-вверх стрелка |
| `forward.svg` | Переслать | зеркало reply, вправо-вверх |
| `copy-plain.svg` | Скопировать как обычный текст | два перекрывающихся прямоугольника |
| `copy-markdown.svg` | Скопировать как Markdown | рамка с бейджем «M↓» (как у логотипа Markdown) |
| `copy-image.svg` | Скопировать изображение | рамка-картинка с точкой и диагональю |
| `download.svg` | Скачать | стрелка вниз в лоток |
| `pin.svg` | Закрепить | канцелярская кнопка |
| `unpin.svg` | Открепить | та же кнопка, перечёркнутая по диагонали — веб-клиент переиспользует `pin` и меняет только подпись, здесь для отдельного файла нужен визуально отличимый вариант (аналог Material Symbols `keep_off`) |
| `edit.svg` | Изменить | карандаш с подчёркиванием |
| `delete.svg` | Удалить | мусорная корзина с крышкой и рёбрами |
| `properties.svg` | Свойства сообщения | кружок с «i» (info) |
| `select.svg` | Выделить (мультивыбор) | круг с галочкой |
| `more.svg` | Другие действия (overflow-меню) | три точки по вертикали |

## Устройства и символика

В папке есть брендовые иконки платформ, а также дефолтные (плейсхолдер) иконки типов устройств для списков сессий/устройств пользователя (`Users`/`Identity`), когда нет специфичной иконки конкретной модели. Префикс `default-` явно маркирует иконку как заглушку, а не как иконку бренда. Тот же язык M3: `viewBox 0 0 24 24`, `stroke-width:2`, `round` caps/joins, `currentColor`, точечная заливка только для мелких деталей (камера, dynamic island).

| Файл | Устройство | Идея глифа |
|---|---|---|
| `android.svg` | Android | робот Android |
| `apple.svg` | Apple | логотип Apple |
| `default-phone.svg` | Телефон (обобщённый) | корпус-таблетка, точка камеры сверху, полоса снизу |
| `default-phone-android.svg` | Телефон на Android | корпус с более прямыми углами, punch-hole камера сбоку от центра, боковые кнопки громкости |
| `default-iphone.svg` | iPhone | сильно скруглённый корпус, «остров» Dynamic Island сверху, полоса home-indicator снизу |
| `default-laptop.svg` | Ноутбук (обобщённый) | экран + трапециевидное основание с клавиатурой |
| `default-desktop.svg` | Десктоп (обобщённый ПК) | монитор на T-образной подставке |
| `default-desktop-monitor.svg` | Монитор | широкий монитор на подставке |
| `default-desktop-dual-monitor.svg` | Два монитора | пара мониторов на отдельных подставках |
| `default-desktop-tower.svg` | Десктоп с системным блоком | монитор и отдельная башня системного блока |
| `default-desktop-mac.svg` | Десктоп Mac (iMac) | моноблочный корпус с более скруглённой рамкой, точка камеры сверху, тонкая ножка |
| `default-macbook.svg` | MacBook | клиновидный корпус экрана + суженное к передней кромке основание, точка камеры сверху |

## Иконки папок чатов

Иконки в `folders/` — самостоятельные семантические глифы для выбора пользователем в папке чатов. Имя файла без `.svg` — системный ключ, который можно передавать в `folder_icon` (например, `work` или `travel`).

| Ключ | Файл | Назначение |
|---|---|---|
| `inbox` | `inbox.svg` | входящие / общий рабочий список |
| `favorites` | `favorites.svg` | избранные чаты |
| `important` | `important.svg` | важные чаты |
| `unread` | `unread.svg` | непрочитанные |
| `muted` | `muted.svg` | чаты без уведомлений |
| `archive` | `archive.svg` | архив |
| `personal` | `personal.svg` | личные чаты |
| `family` | `family.svg` | семья и близкие |
| `friends` | `friends.svg` | друзья |
| `groups` | `groups.svg` | групповые чаты |
| `work` | `work.svg` | работа и проекты |
| `study` | `study.svg` | учёба |
| `travel` | `travel.svg` | поездки |
| `gaming` | `gaming.svg` | игры |
| `music` | `music.svg` | музыка |
| `media` | `media.svg` | фото, видео и медиа |
| `shopping` | `shopping.svg` | покупки |
| `finance` | `finance.svg` | финансы |
| `channels` | `channels.svg` | каналы и трансляции |
| `bots` | `bots.svg` | боты |
| `home` | `home.svg` | дом и быт |
| `events` | `events.svg` | события |
| `private` | `private.svg` | приватные чаты |
| `health` | `health.svg` | здоровье |
| `all-chats` | `all-chats.svg` | все чаты |
| `mentions` | `mentions.svg` | упоминания |
| `replies` | `replies.svg` | ответы |
| `drafts` | `drafts.svg` | черновики |
| `snoozed` | `snoozed.svg` | отложенные чаты |
| `scheduled` | `scheduled.svg` | запланированные сообщения |
| `pinned` | `pinned.svg` | закреплённые чаты |
| `verified` | `verified.svg` | проверенные контакты |
| `support` | `support.svg` | поддержка |
| `podcast` | `podcast.svg` | подкасты |
| `code` | `code.svg` | разработка |
| `design` | `design.svg` | дизайн |
| `science` | `science.svg` | наука |
| `sports` | `sports.svg` | спорт |
| `food` | `food.svg` | еда и рецепты |
| `pets` | `pets.svg` | питомцы |
| `nature` | `nature.svg` | природа |
| `location` | `location.svg` | места и локации |
| `language` | `language.svg` | языки и международные чаты |
| `weather` | `weather.svg` | погода |
| `books` | `books.svg` | книги |
| `movies` | `movies.svg` | кино |
| `voice` | `voice.svg` | голосовые сообщения |
| `goals` | `goals.svg` | цели |
| `news` | `news.svg` | новости |

Пустой `folder_icon` по-прежнему означает отсутствие пользовательской иконки; `inbox` — рекомендуемый явный вариант для входящих.

## Иконки настроек

Иконки в `settings/` соответствуют категориям настроек iOS/macOS и могут использоваться как общий каталог для всех клиентов. Полный контракт и рекомендации по размеру находятся в [`settings/README.md`](settings/README.md).

| Файл | Категория | Идея глифа |
|---|---|---|
| `edit-profile.svg` | Редактирование профиля | профиль с карандашом |
| `general.svg` | Общие настройки | шестерёнка |
| `language.svg` | Язык | глобус |
| `notifications.svg` | Уведомления | колокольчик |
| `security.svg` | Безопасность | замок |
| `privacy.svg` | Приватность | глаз с перечёркиванием |
| `personalization.svg` | Персонализация | кисть и искра |
| `chat-folders.svg` | Папки чатов | две папки |
| `cloud.svg` | Облако | облако |
| `cache.svg` | Кэш | стопка накопителей |
| `active-sessions.svg` | Активные сессии | ноутбук и телефон |
| `about-app.svg` | О приложении | окно приложения |
| `about-server.svg` | О сервере | серверная стойка |
| `testing.svg` | Тестирование | молоток |

## Иконки чата

`chat/` содержит иконки кнопок композера и компактные глифы для превью последнего сообщения в списке чатов. Для превью рекомендуется размер около `15×15dp`, сохраняя исходный `24×24` viewBox. Контракт типов вложений описан в [`chat/README.md`](chat/README.md).

| Файл | Назначение | Идея глифа |
|---|---|---|
| `send.svg` | Отправить сообщение | стрелка вверх в круге |
| `attach-file.svg` | Прикрепить файл | скрепка |
| `stickers.svg` | Открыть стикеры | улыбающееся лицо |
| `image.svg` | Изображение | рамка с фотографией |
| `video.svg` | Видео | видеокамера |
| `gif.svg` | GIF | фоторамка с искрой |
| `document.svg` | Документ | лист с загнутым углом |
| `audio.svg` | Аудио | музыкальные ноты |
| `voice.svg` | Голосовое сообщение | микрофон |
| `sticker.svg` | Стикер в превью | стикер с отрывом и искрой |
| `forwarded-message.svg` | Пересланное сообщение | сообщения со стрелкой |
| `unknown-attachment.svg` | Неизвестное вложение | лист с вопросительным знаком |
