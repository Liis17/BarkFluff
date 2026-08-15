# BarkFluff.Client.Android

Kotlin + gRPC-OkHttp клиент. Activity-based архитектура.

Расположение: `Android/Barkfluff.Client.Android/`
Package: `com.barkfluff.client`

> Полная карта файлов и внутреннего строения: [[Android-ProjectMap]]
> Индекс всех файлов с кратким описанием роли каждого: [[Android-FileIndex]]
> UI/UX спецификация всех экранов и сценариев: [[Клиенты/DesignDocument]] (источник: `dd.md`)

## Версии

- Kotlin 2.2.20, AGP 8.9.1
- gRPC-OkHttp 1.60.0 (NOT grpc-netty)

## Иконка приложения (adaptive, вектор)

Иконка полностью векторная (adaptive icon, минимально доступный API 26; minSdk 31 — растровые фолбэки не нужны).

- `drawable/ic_launcher_background.xml` — full-bleed радиальный градиент `#FFF→#F3F3F3→#EDEDED` (центр 50%/48%, r 72%) на viewport 1536×1536 через `aapt:attr`/`<gradient>`. Без скругления — форму накладывает маска лаунчера.
- `drawable/ic_launcher_foreground.xml` — глиф лого `#111116`; путь из исходной SVG (`app_icon_vector.svg`) обёрнут в `<group>` с pivot (768,768) и scale 0.76: максимальный радиус глифа 616 юнитов × 0.76 = 468 < 469.3 (safe zone 66dp).
- `mipmap-anydpi-v26/ic_launcher.xml` / `ic_launcher_round.xml` — `<adaptive-icon>` со слоями `background` + `foreground` + `monochrome` (тот же foreground; даёт MD3 themed-иконку на Android 13+, tint системный, на API <33 слой игнорируется).
- Растровые WebP `ic_launcher*.webp` из `mipmap-*hdpi` удалены.
- ⚠️ In-app использование (`activity_splash`, `activity_login`, `activity_welcome`, `activity_register`, `step_register_07_bio`, `activity_about` → `@mipmap/ic_launcher(_round)`) пока не менялось: `AdaptiveIconDrawable` в `ImageView` рисуется без маски (полный квадрат с градиентом и уменьшенным глифом). Замена на отдельный in-app drawable — отдельная будущая задача.
- ViewBinding, без Hilt/MVVM

## Архитектура

- Activity-based
- Локальное хранилище: SharedPreferences + EncryptedSharedPreferences для токенов
- Навигация: Welcome → SelectServer → Login → Chats

## Онбординг (Welcome → SelectServer → Login)

Три экрана свёрстаны по референсу `Barkfluff Onboarding 1c2c3c - Final.dc.html` (спека — `Barkfluff Onboarding - Spec.md`, см. [[Клиенты/DesignDocument]]).

### Цвета — только роли темы

Онбординг **не** использует статичную brand-палитру: все цвета берутся через `?attr/colorPrimary`, `?attr/colorOnSurface`, `?attr/colorPrimaryContainer`, `?attr/colorSurfaceContainerLowest` и т.д., как в `RegisterActivity` и подстраницах настроек 2a. Работает Material You и тёмная тема.

Статичными остались только семантические цвета: `onboarding_success_background` / `onboarding_success_text` (чип «Онлайн») и `profile_presence_online`. Остальные `onboarding_*` удалены из `colors.xml`.

⚠️ **Инсеты.** Ни `activity_welcome.xml`, ни `activity_login.xml` не ставят `fitsSystemWindows` на корень. Инсеты применяются к внутреннему `contentPanel` в коде Activity. Иначе корень уезжает вниз, над ним остаётся полоса `windowBackground` другого цвета, а декоративные круги обрезаются по нижней границе статус-бара. Не возвращать `fitsSystemWindows` на корень этих двух экранов.

### Экран 1 — Welcome (макет 1c)

- Композиция: две распорки `layout_weight=1` отжимают hero-блок от верха и прижимают CTA к низу.
- Чипы-фичи — стиль `Widget.Barkfluff.Welcome.FeatureChip`; высота через `chipMinHeight`, а не `layout_height` (фиксированная высота сжимает текст с иконкой).
- Под CTA — только ссылка «Конфиденциальность», открывает legal-лист в режиме чтения. Кнопки «Узнать больше», «О проекте», «Справка» удалены.
- «Начать» → модалка согласия (см. ниже) → `SelectServerActivity`.

### Экран 2 — SelectServer (макет 2c)

- Карточка ноды (`item_server.xml`): чипы «Онлайн» / пинг / регион одной строкой, публичное имя `@handle` отдельной строкой, CTA «Подключиться» 52dp внутри карточки.
- «Своя нода» — кликабельная dashed-строка, разворачивает поле адреса и свою кнопку подключения; шеврон поворачивается на 180°. Свёрнуто по умолчанию.
- Внизу — предупреждение `node_trust_warning`: данные хранятся у владельца ноды, разработчик приложения за них не отвечает.

### Экран 3 — Login (макет 3c)

- Left-aligned hero без карточки-обёртки: круг за верхним краем, логотип 72dp, заголовок 40sp/800.
- Мини-лейблы над полями (`TextAppearance.Barkfluff.Register.FieldLabel`), поля FilledBox с `colorSurfaceContainerHighest`.
- Внизу «Впервые здесь? Создать аккаунт» + ссылка смены ноды.
- Блок ошибки (`errorText`) сам несёт фон `bg_login_error`. Раньше он лежал внутри `errorCard` с `visibility=gone`, которую никто не показывал, — ошибки входа не отображались вообще. Не оборачивать его снова в скрытый контейнер.

### Терминология

UI говорит **«нода»**, не «сервер» — проект перешёл на нодовую систему. Ключи ресурсов (`server_title`, `btn_change_server`, …) намеренно оставлены прежними: поменялся только текст. «Сервер авторизации» в `LoginActivity` — внутренний сервис Identity, а не нода, и переименованию не подлежит.

## Юридические документы и модалка согласия

Источник — `Backend/Barkfluff.WebServer/html/legal/*.md`, тот же, что у сайта (см. [[Backend/WebServer]]).

- **Сборка.** Gradle-таск `copyLegalDocs` (`app/build.gradle.kts`) копирует `TERMS_OF_SERVICE.*.md` и `PRIVACY_POLICY.*.md` в `assets/legal/`. Подключён через `androidComponents.onVariants { ... addGeneratedSourceDirectory(...) }`, а не `preBuild.dependsOn` — Gradle 9 строг к неявным зависимостям с merge-assets. Путь вывода назначает AGP (`build/generated/assets/copyLegalDocs/`), задавать `outputDirectory` вручную бессмысленно. Пустой результат копирования **останавливает сборку**: APK без актуальных соглашений выпускать нельзя. CI `build-client-android.yml` триггерится на `Backend/Barkfluff.WebServer/html/legal/**`.
- **`utils/LegalDocsRepository.kt`** — читает `legal/<DOC>.<lang>.md` по активной локали (маппинг как в `LocaleManager`), fallback — `ru`. Таблицы markdown намеренно разворачивает в списки: consent sheet использует одиночный `TextView`, тогда как нативная сетка таблиц поддержана только внутри bubble сообщений.
- **Редакция.** `revision()` берёт дату «Последнее обновление» из шапки и **всегда из русского файла**: значение уходит в `GlobalParam.acceptedLegalRevision`, и локализованная строка превращала бы смену языка приложения в «новую редакцию». Regex не требует ASCII-двоеточия — в zh-CN шапка использует полноширинное `：`.
- **`LegalConsentBottomSheet`** — два таба (соглашение / конфиденциальность), рендер через `MarkdownRenderer`, чекбокс + «Принять»/«Отмена». В режиме согласия лист неотменяемый (`isCancelable = false`, свайп запрещён) — решение обязательно. Режим `forReading(tab)` — только чтение и «Закрыть».
- **Согласие хранится как редакция, а не флаг** (`GlobalParam.acceptedLegalRevision`): обновили соглашение — согласие запрашивается заново.

## UI — Экран списка чатов (MainActivity + ChatsFragment)

- На телефоне `MainActivity` показывает M3 Expressive floating navigation: pill-группа с морфингом активной вкладки (filled icon + label) и отдельный 64dp squircle FAB. Всегда доступны «Чаты» и «Профиль», а «Звонки» добавляются третьей вкладкой при `mainTabCallsVisible`; FAB виден только в чатах. `ChatsFragment` публикует суммарный счётчик непрочитанных через Fragment Result для badge «Чаты» (значения выше 99 отображаются как `99+`). Wide-layout сохраняет прежний navigation rail и medium FAB.
- `CreateChatBottomSheet` — светлый modal sheet с hero-пунктом обычного чата и равномерной строкой вторичных карточек. «Групповой» есть всегда; «Приватный» и «Секретный» зависят от тестовых `privateChatsEnabled` / `secretChatsEnabled`, поэтому оставшиеся карточки растягиваются на всю строку. Обычный и приватный пути ведут в `SearchActivity`, группа — в `CreateGroupChatActivity`, секретный — в `CreateEncryptedChatActivity` с `EXTRA_INITIAL_TYPE=secret`, чтобы сразу выбрать режим SECRET.
- Групповой flow: `CreateGroupChatActivity` выбирает нескольких пользователей через поиск, требует название, принимает опциональную обложку и вызывает `CreateGroupChat`.
- `ChatData` получает `chatType`, `lastActivityAt`, `privateInviteState`, `privateInviterUserId` и `hasDraft`; `ChatsFragment` подгружает страницы `ListChats` при прокрутке. Для обычного чата с серверным или несинхронизированным локальным черновиком карточка показывает акцентное «Черновик» вместо превью. Приватный чат открывает общий `ChatActivity` (`ChatActivity.privateChatIntent`, `kind=PRIVATE`), имеет lock-бейдж рядом с аватаром и skeleton вместо текста последнего сообщения.
- Инвайт-флоу приватного чата в списке: при `privateInviteState != ACCEPTED` вместо skeleton показывается статус — «Запрос на приватный чат» (приглашённый), «Ожидает подтверждения» (инициатор), «Запрос отклонён». Роль определяется по `privateInviterUserId` vs свой userId. В общем `ChatActivity` логику ведёт `PrivateChatController`: в pending-режиме у приглашённого — скрытый оверлей `e2eInviteContainer` «Принять/Отклонить» (принять → passphrase → `acceptPrivateChatInvite`), у инициатора — баннер `e2eBanner` с заблокированным вводом (разблокируется по `privateChatInviteResolutions`); вход с push без extras (`inviteState=-1`) — fallback-фетч состояния через `getChat`. FCM `type=private_chat_invite` → `NotificationHelper.showPrivateInviteNotification`, тап открывает приватный чат в `ChatActivity` (`EXTRA_IS_PRIVATE_CHAT` в `MainActivity.handleChatIntent`).
- Ввод passphrase приватного чата (создание, инвайт, разблокирование) содержит opt-in «Сохранить пароль». В `EncryptedSharedPreferences` сохраняется только производный ключ; при logout ключи очищаются.

- `activity_main.xml`: `fragmentContainer` растянут на весь экран (`toBottomOf="parent"`). В phone-варианте `floatingNavContainer` центрирует над ним группу вкладок и FAB с отступом 20dp от нижнего inset; старый невидимый `bottomNavigation` оставлен только для общей ViewBinding-совместимости с `layout-w600dp`.
- `fragment_chats.xml`: `RecyclerView` (`chatRecyclerView`) занимает всё пространство фрагмента.

### Редизайн по макету M3E «Вариант 3»

Экран приведён к макету `Мессенджер M3E - Вариант 3.dc.html`. Палитра макета (тёплая оранжевая) намеренно **не** зашита в ресурсы: значения смаплены на роли темы (`#FEF8F6`→`colorSurface`, `#FF6B35`→`colorPrimary`, `#FFDAD0`→`colorPrimaryContainer`, `#F0E2DC`/`#F6EAE5`→`colorSurfaceContainerHigh`), поэтому Material You и тёмная тема продолжают работать. Форма, размеры, типографика и анимации перенесены из макета точно. Нижние вкладки и FAB (`MainActivity`) редизайн **не затрагивает**.

- `AppBarLayout` + `MaterialToolbar` удалены. Вместо них `headerContainer` (LinearLayout): сворачиваемый блок `headerCollapsible` (заголовок «Чаты» 36sp/44 weight 600 + `headerSubtitle` + аватар пользователя 48dp справа), лента папок `foldersRecyclerView`, строка поиска.
- `headerSubtitle` — одна строка на два назначения: статус синхронизации (`chats_sync_updating` / `chats_sync_offline` / `connecting`), иначе счётчик непрочитанных (`plurals/chats_unread_summary`, при нуле `chats_unread_none`). Обновляется из `publishMainUnread()`, анимация смены текста — прежняя fade/slide (`updateHeaderSubtitle`).
- `searchField` — pill-поле (`bg_chats_search_field`, ripple) с иконкой и подписью «Поиск чатов»; тап открывает существующий `SearchActivity`. Инлайн-фильтрации списка нет.
- **Сворачивание по направлению прокрутки** (`ChatsFragment.updateHeaderCollapse` / `setHeaderCollapsed`): прокрутка вниз при offset > 20dp схлопывает `headerCollapsible` (высота → 0 + alpha, 360 мс, `PathInterpolator(0.2,0,0,1)`) и сжимает поле поиска 52→48dp (300 мс); прокрутка вверх возвращает. Порог реакции — 6dp, чтобы состояние не дребезжало. По окончании разворачивания высота возвращается в `WRAP_CONTENT` — иначе блок «залипает» на пиксельном значении при смене контента.
- `item_chat.xml` — один layout на два состояния, всё различие выставляет `ChatAdapter.applyUnreadStyle(isUnread)`: корневая `MaterialCardView` `chatCard` (радиус 20→28dp, фон transparent→`colorPrimaryContainer`, нижний отступ 0→8dp), паддинг строки 12→16dp, `avatarContainer` 50→58dp, заголовок 16sp/w400→17sp/w600, превью w400→w500, вторичный цвет `colorOnSurfaceVariant`→`colorOnPrimaryContainer`, бейдж непрочитанных 26dp pill. Вес шрифта задаётся `Typeface.create(SANS_SERIF, weight, false)` (API 28+).
- `item_chat_skeleton.xml` синхронизирован с новой геометрией строки (74dp, аватар 50dp, паддинг 18dp).
- Перенесены **только** визуал и анимации. Лента закреплённых чатов, свайп-архив, мультивыбор строк и реакции из макета не переносились: это отдельные фичи с серверной частью.
- `ChatAdapter` добавляет прозрачный **footer-спейсер** (126dp = 1.5 × высота элемента чата ≈ 84dp) в конец списка:
  - `VIEW_TYPE_FOOTER` / `FooterViewHolder` — не требует биндинга.
  - `submitList()` переопределён: `ensureFooter()` удаляет все footer-элементы из списка и добавляет один в конец перед каждой отправкой в DiffUtil.
  - Все методы изменения списка (`updateChatWithNewMessage`, `addNewChat`, `updateReadStatus`) фильтруют footer через `!it.isFooter`.
  - `ChatDiffCallback` корректно сравнивает footer-элементы.
  - Layout: `res/layout/item_chat_footer.xml` — прозрачная `View` высотой 126dp.

- grpc-okhttp 1.60.0 (coroutine stubs)
- `MetadataUtils.attachHeaders` не резолвится в grpc-okhttp 1.60.0 — использовать `ClientInterceptor` напрямую

## Экран «Профиль» (`ProfileFragment`)

Экран настроек V1 реализует вариант 2a из [[Клиенты/DesignDocument]]: локальные M3-формы (фон `surface container`, плашки `surface`) получают цвет из активной схемы Material 3, включая системный dynamic color на Android 12+.

- `res/layout/fragment_profile.xml` — заголовок «Профиль», горизонтальный блок идентичности с 72dp squircle-аватаром и четыре группы: «Аккаунт», «Оформление», «Приложение», «О приложении». Все 13 прежних пунктов и их `id` сохранены, поэтому переходы в Activity не менялись.
- Каждая строка — самостоятельная `MaterialCardView` высотой 58dp. Плашки внутри группы разделены фоновым зазором 3dp и используют форму `28/6dp` (верх/середина/низ); ведущие иконки оптически выровнены по общей вертикали, logout остаётся отдельной error-container плашкой с M3 confirmation dialog.
- Строка «Язык» показывает нативное название активной локали. Изображение профиля теперь маскируется формой squircle: `AvatarLoader` принимает `circleCrop`; для `ProfileFragment` он отключён, а при отсутствии фото виден `person` placeholder.
- Телефонная floating-навигация закреплена в `MainActivity`, поэтому фрагмент оставляет нижний 150dp spacer для неё; wide-layout продолжает использовать navigation rail. При показе профиля фрагмент временно перекрашивает корневой контейнер `MainActivity`, поэтому его фон продолжается в прозрачный edge-to-edge status bar; при уходе на другую вкладку исходный фон восстанавливается.

### Подстраницы настроек (MD3 2a)

Двенадцать Activity, открываемых из `ProfileFragment` (аккаунт, безопасность, приватность, устройства, персонализация, папки, уведомления, язык, данные и кеш, обновление, о приложении и тестирование), используют локальную тему `Theme.BarkfluffClientAndroid.Settings2A`. Она не меняет остальные экраны: сохраняет M3 primary/tonal/error-роли, soft-card и split-card формы `28/6dp` с зазором 3dp, а сами роли получает из активной темы. На Android 12+ `DynamicColors` уже применяется ко всему приложению; fallback-ресурсы `profile_*`, `profile_settings_*` и `floating_nav_*` также ссылаются на системную динамическую палитру. Поэтому настройки, floating-навигация и пользовательские/групповые профили следуют за системным акцентом, а не за фиксированным коричневым. Зелёный online и error-роль остаются семантическими исключениями.

Все подстраницы приведены к эталону `fragment_profile.xml` (июль 2026): фон экрана и шапки — `profile_settings_background` (без отдельного цвета AppBar), блоки — сегменты top/middle/bottom (`bg_settings_item_top|middle|bottom|single`, цвет `?attr/colorSurfaceContainerLowest`, скругления 28/6dp, зазор `Space` 3dp), строка — высота/minHeight 58dp, paddingH 20dp, плоская иконка 24dp с tint `profile_settings_icon`, текст BodyLarge, значение/шеврон 20dp справа. Тональные кружки 44dp у пунктов приватности/тестирования убраны, `bg_settings_language_selected` заменён на пары `bg_settings_language_top|middle|bottom` (selector: checked → tonal container той же формы). «О приложении» показывает `@mipmap/ic_launcher_round` вместо тонированного `ic_sticker`; `ic_qr_code` перерисован стандартным Material-вектором.

`StorageSettingsActivity` разделяет серверное и локальное хранилища. GIF входит в серверную категорию «Изображения»; локальный блок показывает отдельно Coil/bitmap «Кеш изображений» и SQLCipher Room «Кеш чатов», строит bar пропорционально их фактическим размерам и очищает ровно эти два источника.

## Сегмент папок над списком чатов

`fragment_chats.xml` содержит горизонтальный `RecyclerView` `foldersRecyclerView` поверх `chatRecyclerView`, скрытый при отсутствии папок. Раньше был `ChipGroup` с одинарными `Chip`-ами; заменён на кастомный адаптер ради иконки + имени + бейджа непрочитанных и поддержки компактного режима.

- `adapter/FolderTabsAdapter.kt` — `RecyclerView.Adapter` с моделью `Item(id, icon, name, unreadCount)`. Метод `submit(items, compact, selected)` обновляет список целиком; `updateSelection(id)` — только подсветку. Иконка по умолчанию для папки «Все чаты» (folderId=null) — `📋`. Бейдж непрочитанных рисуется при `unreadCount > 0`, формат `99+` при переполнении.
- `layout/item_folder_tab.xml` — корневой `LinearLayout` с `bg_folder_tab` selector (state_selected → `bg_folder_tab_selected` с `colorSecondaryContainer`, иначе прозрачный). Иконка (TextView 18sp под эмодзи) + имя (`?attr/textAppearanceLabelLarge`, ellipsize=end) + бейдж (`bg_folder_unread_badge` — rounded rect `colorPrimary` r=10dp, 20dp высота, minWidth=20dp, текст bold 11sp на `colorOnPrimary`).
- `ChatsFragment`:
  - `setupFolderTabs()` инициализирует горизонтальный `LinearLayoutManager` + адаптер.
  - `renderFolderTabs()` собирает `Item`-ы из `folders`, добавляя первой «Все чаты»; передаёт `compact = globalParam.compactFolders`.
  - `computeFolderUnread(chatIds)` суммирует `chat.countUnread` из `allChats`, ограничиваясь `chatIds` папки. Для «Все чаты» — `computeAllChatsUnread()` (см. ниже).
  - Реалтайм-события зеркалятся в `allChats` через `mirrorNewMessageInAllChats` / `mirrorReadInAllChats`, после чего `refreshFolderTabs()` пересчитывает бейджи без перезагрузки чатов.
  - На клик по табу — `selectedFolderId` обновляется, `foldersAdapter.updateSelection` + `applyFolderFilter()`.
  - `onResume()` ререндерит сегмент с актуальной настройкой компактности (чтобы изменение в персонализации применялось при возврате).

### Настройка «Убирать чаты из «Все чаты»» (`excludeFolderChatsFromAll`)

Если включена — чаты, входящие хотя бы в одну пользовательскую папку, исключаются из вкладки «Все чаты» **и** не учитываются в её бейдже непрочитанных. Логика в `applyFolderFilter()` (фильтрация списка) и `computeAllChatsUnread()` (счётчик). Реализовано чисто на клиенте.

### Настройка «Компактные папки» (`compactFolders`)

Скрывает `folderName` (`View.GONE`) в каждом табе, оставляя иконку + бейдж. Передаётся в `FolderTabsAdapter.submit(compact = ...)`.

## Хранение токенов

- EncryptedSharedPreferences (`barkfluff_secure_prefs`)
- При создании gRPC-каналов добавлять `x-auth-token` через interceptor

## TLS и доверие к нодам

Release-вариант запрещает cleartext (`usesCleartextTraffic=false` и `network_security_config`) и использует системный Android trust-store с обычной проверкой hostname для gRPC, presigned HTTP(S), Coil и LiveKit-сигнализации. `http://`/h2c endpoint из [[Backend/Beacon|Beacon]] отклоняется до сохранения в `GlobalParam`; h2c и permissive TLS остались только в `:core` debug source set для локальной разработки.

Самоподписанные ноды поддерживаются через `core/security/`: `TlsCertificateProbe` делает **только TLS-handshake** без HTTP/gRPC-данных и токенов, проверяет hostname, срок действия и self-signature листового сертификата. `SelectServerActivity` показывает subject, срок и копируемый SPKI `sha256/<base64>` fingerprint; после явного «Доверять сертификату» `TlsTrustStore` сохраняет один pin на hostname в `barkfluff_tls_pins`. Pin применяется ко всем клиентам через `TlsTransportFactory`, поэтому сертификат с новым SPKI блокирует соединение до повторного подтверждения, а перевыпуск с прежним ключом проходит без диалога. После Beacon до сохранения ноды выполняется certificate-only preflight её service endpoint'ов и LiveKit; недоступный endpoint pin не получает, а просроченный или несовпадающий по hostname сертификат даёт явную security-ошибку. При refresh Beacon новая или сменившаяся self-signed service certificate не сохраняется: следующий foreground переводит пользователя в тот же trust flow. В ручном поле ноды есть «Удалить доверенный сертификат» для повторного trust flow.

Следствие: публичный endpoint с неполной TLS-цепочкой теперь корректно отклоняется release-клиентом — владелец ноды обязан развернуть fullchain, см. [[Backend/Nginx]].

## Каналы сборки (stable / dev / nightly)

Три product flavor'а по измерению `channel` в `app/build.gradle.kts` — каждый со своим `applicationId`, поэтому сборки разных каналов стоят на устройстве рядом. Стабильный flavor называется `stable`, а не `release`: Gradle запрещает совпадение имени flavor'а с именем buildType.

| Ветка | Flavor | applicationId | Имя в лаунчере | Канал ClientStorage | Имя APK |
|---|---|---|---|---|---|
| `master` | `stable` | `com.barkfluff.client` | Barkfluff | `release` | `Barkfluff-release-X.Y.Z.apk` |
| `dev` | `dev` | `com.barkfluff.dev` | Barkfluff.dev | `dev` | `Barkfluff-dev-X.Y.Z.apk` |
| `nightly` | `nightly` | `com.barkfluff.nightly` | Barkfluff.nightly | `nightly` | `Barkfluff-nightly-X.Y.Z.apk` |

- **`BuildConfig.UPDATE_CHANNEL`** — канал сборки. По нему `UpdateChecker.hasUpdate()` следит только за своим каналом, а `UpdateActivity` показывает кнопку обновления лишь в своей карточке: APK чужого канала не обновит приложение, а встанет вторым. Канал больше **не** определяется суффиксом « beta» в версии — `AppVersion.isBeta` для этого не используется.
- **Ресурсы каналов** — `app/src/dev/res/` и `app/src/nightly/res/` перекрывают `main`: `app_name` во всех пяти локалях и adaptive-иконка. Иконка собрана из двух слоёв (фон-текстура PNG в `drawable-nodpi/`, белый глиф-вектор с обводкой), потому что маска adaptive-иконки показывает только центральные 66% холста и срезала бы готовое изображение по краям.
- **`google-services.json`** содержит client-записи всех трёх пакетов. Без записи под конкретный `applicationId` плагин `com.google.gms.google-services` роняет сборку флейвора.
- **Версия.** `versionName` поднимает только сборка ветки `nightly` (patch + 1 от версии в канале `nightly`); `dev` и `master` переиздают ту же версию как есть. В git `versionName` всегда остаётся `0.0.1` — реальное значение `sed`'ом подставляет CI и обратно не коммитит. Следствие: два пуша в `dev` подряд без промежуточной nightly-сборки дают два APK с одинаковой версией, и клиент не увидит второй как обновление.
- **Незакрытый хвост.** Сайт (`VersionPollingService` в [[Backend/WebServer]]) по-прежнему опрашивает `kotlin/beta`, куда Android больше не публикует, — ссылка на beta-сборку на странице загрузок застыла.

## Система обновлений и её TLS

Сервер обновлений [[Backend/ClientStorage|ClientStorage]] (`storage.barkfluff.com`) отдаётся за Cloudflare Origin CA, которого нет в системном хранилище Android. Сертификат едет в APK строкой `BuildConfig.STORAGE_CA_PEM_B64`: `app/build.gradle.kts` заполняет её из переменной окружения `STORAGE_CA_PEM_B64`, а воркфлоу `build-client-android.yml` подставляет туда секрет `CLOUDFLARE_ORIGIN_CA_BUNDLE_B64` — тот же, которым он ходит на storage через `curl --cacert`. Без переменной строка пустая, и локальные сборки просто работают на системном хранилище (обновления в них не проверяются).

`utils/UpdateServerTls.kt` разворачивает base64 в `X509Certificate` → `KeyStore` → `TrustManagerFactory` → `SSLSocketFactory`. Этот factory подставляется только двум местам: `HttpURLConnection` в `UpdateChecker` (проверка версии) и OkHttp-клиенту загрузки APK в `UpdateActivity`.

Два решения, которые важно не откатить:

- **`network_security_config` намеренно не трогается.** Любой `domain-config` там ломает `PinnedTrustManager` из `core/security/`: он делегирует в hostname-неосведомлённый `checkServerTrusted(chain, authType)`, а платформенный `RootTrustManager` при наличии per-domain конфигураций на такой вызов бросает `CertificateException`. Отвалился бы весь остальной трафик приложения — gRPC, аватары, LiveKit.
- **APK качается своим OkHttp, а не системным `DownloadManager`.** `DownloadManager` работает в отдельном системном процессе: он не читает `network_security_config` приложения и не принимает `SSLSocketFactory`, поэтому до storage за приватным CA он не доходит в принципе.

## Разлогин (LogoutHelper)

`utils/LogoutHelper.kt` — централизованный хелпер полного выхода из аккаунта:
1. `realtimeService.shutdown()` + `callEventsService.shutdown()` — иначе стримы после сброса токенов уходят в бесконечный retry с 401
2. Серверный `Logout` gRPC (удаляет refresh-токен в Identity)
3. `FirebaseMessaging.deleteToken()` — деактивирует push на устройстве
4. `globalParam.clearUserData()` + очистка `firebaseToken`
5. Очистка кешей: `AvatarLoader.clearAllCaches()`, `StickerCache.clear()`, `media_files/`
6. Переход на `LoginActivity` с `FLAG_ACTIVITY_NEW_TASK or FLAG_ACTIVITY_CLEAR_TASK`

Вызывается из:
- `ProfileFragment` → кнопка "Выйти"
- `DevicesActivity` → завершение сессии **текущего** устройства (если `deviceId == globalParam.deviceId`)

`AccountSettingsActivity` → кнопка "Выйти" использует **упрощённый** путь (только `clearUserData()` + переход на `LoginActivity`), мимо `LogoutHelper` — без серверного логаута, удаления FCM-токена и остановки стримов.

## Старт realtime-стримов и авторизация

`RealtimeService.resume()` и `CallEventsService.resume()` выходят сразу, если `globalParam.refreshToken` пуст — до входа сервер отвечает 401 на каждый из 19 стримов, а retry-петля (`streamWithReconnect`) на каждой итерации дёргает `recreateAllClients`, засоряя лог и подтекая gRPC-каналами.

`BarkFluffApplication` вызывает `resume()` из `ProcessLifecycleOwner.onStart` — на экране логина это происходит до появления токенов и второй раз уже не срабатывает. Поэтому `MainActivity.onCreate` → `startRealtimeAfterLogin()` поднимает оба сервиса явно; вызовы идемпотентны (проверяют активный scope), так что при холодном старте залогиненного пользователя ничего не дублируется.


## Звонки (V1)

Стартовая интеграция звонков живёт только в V1 (`Android/Barkfluff.Client.Android/app`) и общем `Android/core`; V2 не менялся.

- `Android/core/src/main/proto/beacon_api.proto` синхронизирован с `Shared/BarkFluff.Proto/beacon_api.proto`: добавлен `Service calls = 14` рядом с `livekit_url = 13`.
- `GlobalParam` хранит `socketCalls` и `livekitUrl`; `SelectServerActivity` сохраняет их из Beacon, `AboutActivity` показывает в диагностике.
- При применении ответа Beacon V1 не превращает пустой/offline Calls endpoint в URL, по умолчанию добавляет `https://` к адресам без схемы и пишет в Logcat raw-диагностику `Beacon calls: has/host/port/tls/livekit`.
- `utils/ServerInfoPrefs.kt` централизует сохранение [[Backend/Beacon|Beacon]] `GetServerInfo` в `GlobalParam`; `SplashActivity` при запуске требует только сохранённый `socketBeacon`, обновляет остальные endpoint'ы из Beacon и продолжает вход только если после refresh есть Identity. `AboutActivity` показывает сохранённый список сервисов, а проверка доступности запускает параллельный анонимный `GET /ping` по каждому настроенному endpoint'у.
- `utils/ServicePingChecker.kt` использует тот же `TlsTransportFactory`: принимает сервис только при `HTTP 200`, `text/plain` и теле `pong`, измеряет время каждого запроса в миллисекундах и показывает в строке `Доступен`/`Недоступен` вместе с временем. HTTPS следует системному trust-store или явному host pin; h2c (`H2_PRIOR_KNOWLEDGE`) доступен только в debug. `LiveKit` отображается только как внешний адрес и не проверяется через endpoint liveness из [[Архитектура]].
- `GrpcManager` умеет создавать `CallsApi` client (`createCallsClient`) и пересоздавать его через `initAllClients`/`recreateAllClients`.
- `core/calls/CallRepository.kt` — тонкая обёртка над `InitiateCall`, `AcceptCall`, `RejectCall`, `JoinCall`, `EndCall`, `SetCallAudioQuality`, `SubscribeCallEvents`, `ListCallHistory`, `GetActiveCalls`.
- `core/calls/CallEventsService.kt` подключается в `BarkFluffApplication` вместе с `RealtimeService`: держит lifecycle-подписку на `SubscribeCallEvents`, публикует raw events через `SharedFlow`, текущее состояние звонка через `StateFlow`, делает reconnect/backoff и auto-reject второго входящего звонка при уже активном звонке.
- В `BarkFluffApplication` есть foreground bridge для `CallEventsService.events`: incoming открывает `IncomingCallActivity` и показывает call notification, accepted/rejected/ended закрывают входящий экран через package-local broadcast и убирают notification. Background/killed сценарий остаётся за FCM payload `incoming_call`/`dismiss_call`.
- `CallsFragment` (вкладка `Звонки`) показывает реальную историю из `ListCallHistory` через `CallHistoryAdapter` (`item_call_history.xml`): direction/missed-иконка, имя/чат, относительное время + длительность. Фильтр `Все`/`Пропущенные` перезагружает список; tap по строке открывает чат (личный — через `getPersonChatId`), кнопка action — повторный звонок (audio/video) через `CallActivity`. Имена резолвятся: личные — `getUserData`, групповые — из списка чатов. Пагинация v1: одна страница (limit 50), `has_more` пока не используется. Бэкенд `GetActiveCalls` доступен в репозитории, join-баннер ещё не нарисован.
- В V1 `ChatActivity` в верхней панели кнопка **только аудиозвонка** (видео-кнопка убрана; видео включается уже внутри `CallActivity`). Запускает сигналинг и открывает `CallActivity` с `livekitUrl/accessToken`.
- **Групповые чаты V1**: клик по шапке группового чата открывает `GroupInfoActivity` (для ЛС — `UserProfileActivity`). `GroupInfoActivity`: смена названия (`updateGroupChat(title)`), смена аватара (UCrop → `uploadFile(CHAT_PICTURE)` → `updateGroupChat(pictureFileId)`), список участников (`listChatMembers` + `getUserData` для аватарок, `GroupMemberAdapter`; fallback preview/full URL → preview/full fileId), тап по участнику открывает `UserProfileActivity`, удаление (`kickUser`), добавление через `AddGroupMemberActivity` (переиспользует search-layout + `UserAdapter`, `addUser`). Аватарки/имена чужих сообщений в группе резолвятся через кэш `groupMemberInfoCache` с тем же URL-first fallback в `ChatActivity` + `senderInfoProvider` в `MessageAdapter`. Сортировка списка чатов: `ChatsFragment.mirrorNewMessageInAllChats`/`applyFolderFilter` пересортировывают `allChats` по `lastMessage.sentAt`. Контекстное меню сообщения (`PopupWindow isFocusable=false`) больше не закрывает клавиатуру.
- FCM service обрабатывает `type=incoming_call` и `type=dismiss_call`. `NotificationHelper` создаёт канал `calls` и показывает `NotificationCompat.CallStyle` для входящего звонка.
- Telecom-интеграция звонков: V1 регистрирует self-managed PhoneAccount через MANAGE_OWN_CALLS и BarkFluffConnectionService, а realtime/FCM incoming_call сначала вызывает TelecomManager.addNewIncomingCall, затем показывает собственный CallStyle/full-screen UI и ringtone. Telecom не проигрывает ringtone сам для self-managed VoIP, но учитывает звонок как системный call для маршрутизации, Bluetooth и конкуренции с другими звонками.
- Для входящего звонка `NotificationHelper.showIncomingCallNotification` дополнительно запускает системный ringtone через `RingtoneManager.TYPE_RINGTONE` + `AudioAttributes.USAGE_NOTIFICATION_RINGTONE` в loop-режиме **после успешной публикации notification** — если `POST_NOTIFICATIONS` отключён, звук не маскирует отсутствие UI. Входящее call-уведомление показывается в отдельном канале `incoming_calls_v2` с `IMPORTANCE_HIGH`, vibration/default vibration и без `setSilent(true)`, чтобы Android мог показать heads-up баннер поверх экрана/lockscreen; ongoing-уведомление активного звонка остаётся в `calls` и `setSilent(true)`, чтобы не было короткого notification-звука поверх ringtone. `IncomingCallAlertPolicy` различает отключённые уведомления, неактивный канал и отключённый full-screen access. `MainActivity` направляет пользователя в системные настройки, если отключены уведомления или (на Android 14+) специальное разрешение `USE_FULL_SCREEN_INTENT`. После accept используется `NotificationHelper.clearIncomingCallAlert`, чтобы остановить ringtone и убрать входящий notification без разрыва активного Telecom `Connection`; полный `NotificationHelper.dismissCall` завершает Telecom connection для realtime/FCM `dismiss_call`, reject/end.
- Добавлены IncomingCallActivity, CallActivity, CallActionReceiver и permissions для микрофона/camera/screen-share/full-screen intent.
- `IncomingCallActivity` показывает аватар звонящего с локальными retry-попытками загрузки (3 попытки с короткой паузой; после провала cached URL запрашивается заново через `ChatRepository.getFileDownloadUrl`) и анимированными ring-pulse кольцами вокруг аватара.
- **Аватар звонящего при killed app** (`calls/IncomingCallPrefetch.kt`): процесс поднимает FCM, а `BarkFluffApplication.onCreate()` создаёт `GrpcManager` **без клиентов** (их создают Splash/Login/Main) — поэтому `getUserData`/`getFileDownloadUrl` падали на `usersClient == null`, и звонок показывался с одними инициалами. Теперь `handleIncomingCall` до показа звонка (`withTimeoutOrNull`, 2.5 с) вызывает `IncomingCallPrefetch.prepareAvatar`: `ensureTokenValid` + точечное создание `users`/`files` клиентов → `avatar_url` из push (fallback — профиль по `caller_user_id`) → fileId → presigned URL через кэши `AvatarLoader` → Bitmap в Coil (`allowHardware(false)`, `CircleCropTransformation`). Готовый Bitmap лежит по `callId` и переиспользуется `NotificationHelper.showIncomingCallNotification` (иконка `Person` вместо placeholder) и `IncomingCallActivity` (рисуется сразу, без ожидания сети); чистится в `clearIncomingCallAlert`. Сервер шлёт `avatar_url` в push давно (`FirebaseService.SendIncomingCallBatchAsync`), клиент его просто игнорировал.
- **TLS для LiveKit-сигнализации:** `LiveKitCallEngine.connect()` передаёт в `LiveKit.create` OkHttp из `TlsTransportFactory`, то есть применяет ту же системную/PIN-политику, что gRPC и HTTP-загрузки. Self-signed LiveKit endpoint должен быть подтверждён в certificate preflight выбранной ноды; `hostnameVerifier` не переопределяется. Медиа-плоскость (WebRTC DTLS) системный trust-store не использует. Неполная публичная цепочка nginx (см. [[Backend/Nginx]]) больше не обходится клиентом и требует server-side fullchain.
- В `:app-v1` подключён LiveKit Android SDK `2.26.0` + `livekit-android-camerax`. `LiveKitCallEngine` управляет room lifecycle, mic/camera/screen-share и **отдаёт UI-модель участников** `StateFlow<List<CallParticipant>>` (камера+экран track, mic/camera enabled, speaking, connection quality) вместо хранения renderer'ов. Движок пересобирает список по событиям Room (`ParticipantConnected/Disconnected`, `TrackPublished/Subscribed/Muted`, `ActiveSpeakersChanged`, `ConnectionQualityChanged`), различает `Track.Source.CAMERA` и `SCREEN_SHARE`. Дополнительно: `flipCamera()` (`LocalVideoTrack.switchCamera`), `selectAudioDevice()` через `AudioSwitchHandler` (динамик/наушник/проводная/Bluetooth), `setRemoteVideoQuality()` (`RemoteTrackPublication.setVideoQuality`).
- **UI-дизайн** (`activity_call.xml` + программная отрисовка плиток). Тёмный иммерсивный экран (`bg_call_root`), edge-to-edge с обработкой `WindowInsets` (верхняя панель не залезает под статус-бар, панель управления — над навигацией). Стиль повторяет веб-референс. Режимы раскладки в `CallActivity.renderTiles`:
  - **single** (1-на-1, нет демонстрации) — `CallTileView.setHero(true)`: крупный аватар (112dp) + имя + waveform по центру, локальный участник — в мини-окне `selfMiniContainer` (100×140, скруглённое) вверху справа;
  - **grid** — сетка плиток до 2 колонок (ячейки через weight, не растягиваются вертикально);
  - **stage** — демонстрация экрана/focus: крупная плитка сверху + полоса камер снизу. Tap по плитке — focus/возврат.
- `CallTileView` (плитка/hero): аватар (картинка через Coil CircleCrop, иначе цветной круг с инициалами через `AvatarLoader`), имя, чип статуса микрофона (зелёный/красный/нейтральный), зелёная обводка говорящего, при включённой камере — `SurfaceViewRenderer` на весь размер. Имена/аватары участников резолвятся в `CallActivity.infoFor` по userId (livekit identity) через `getUserData` с кешем `infoCache` (раньше показывался сырой id без аватара).
- **Палочки голоса** — кастомный `WaveformView` (анимированный эквалайзер с независимыми случайными высотами палочек), активен пока участник говорит (для hero под именем, для плитки вместо «молчит»).
- **MD3 dynamic (адаптация под системную тему)**: весь хром берёт цвета из ролей темы (`Theme.Material3.DayNight` + DynamicColors) — фон-градиент `colorSurface`→`colorSurfaceContainerLow`, плитки `colorSurfaceContainerHigh`, кнопки `colorSurfaceContainerHighest`/`colorOnSurfaceVariant`, текст `colorOnSurface`/`colorOnSurfaceVariant`, панель/бейджи + `colorOutlineVariant`. Активные кнопки — `colorPrimary`/`colorOnPrimary` (`applyButtonState`). Иконки статус-бара переключаются по яркости `colorSurface` (`ColorUtils.calculateLuminance`). Семантические акценты фиксированы: зелёный «говорит» (waveform/обводка/чип), красный mute (`colorError`), красная «Завершить». Аватары — палитра `AvatarLoader.colorForUser`. Так экран следует за светлой/тёмной системной динамикой, а не всегда тёмный.
- Контролы — стеклянная «таблетка» (`bg_call_control_pill`) с круглыми кнопками с подписями: `Микро`, `Камера`, большая красная `Завершить` (`bg_call_btn_end`), `Экран`, `Ещё`. Переворот камеры перенесён в лист `Ещё` (виден при включённой камере) вместе с `Маршрут звука` / `Качество голоса` / `Качество видео собеседника`. Бейдж количества участников в шапке для групповых (>2). Состояния кнопок и foreground service синхронизированы с моделью участников; foreground обновляется только при смене camera/screen.
- Таймер длительности устойчив к reconnect (anchor не сбрасывается). `CallActivity` слушает `CallEventsService.events` по своему `callId`: `ENDED`/`REJECTED` (в т.ч. от собеседника или со второго устройства) закрывают экран, останавливают foreground и гасят notification — работает и для звонящего, где `currentCall` не инициализируется. Завершение идемпотентно (флаг `callEnded`); accept/reject в `IncomingCallActivity` защищены флагом `actionTaken`.
- `CallForegroundService` держит ongoing notification активного звонка с foreground service types `microphone|camera|mediaProjection`; action уведомления завершает активный звонок через `CallActionReceiver`.
- Foreground активного звонка стартует до подключения к LiveKit, поэтому аудиозвонок тоже сразу получает ongoing foreground notification. Runtime type включает `microphone|phoneCall`, при включении камеры добавляется `camera`, при демонстрации — `mediaProjection`.
- `CallActivity` при `RoomEvent.Disconnected` / `FailedToConnect` не завершает звонок сразу: запускает backoff-реконнект (2s, 4s, 8s, 15s, далее 30s, максимум 8 попыток), обновляет LiveKit credentials через `JoinCall(call_id)`, пересоздаёт room и восстанавливает состояние микрофона/камеры. Демонстрация экрана после полного reconnect не восстанавливается автоматически, т.к. `MediaProjection` требует свежего системного consent.
- Пока локально есть активный звонок (`CallTelecomRegistry.hasActiveCall()`), `BarkFluffApplication.onStop()` не ставит `CallEventsService` на паузу — remote `ENDED`/`REJECTED` продолжает доходить в фоне.
- `CallBatteryOptimizationHelper` один раз во время успешного звонка просит Android исключить BarkFluff из battery optimization (`ACTION_REQUEST_IGNORE_BATTERY_OPTIMIZATIONS`), если система ещё не дала исключение. Это дополняет foreground service для агрессивных OEM/Doze-сценариев.

Связанный backend-контекст: [[Backend/Calls]], [[Backend/CloudMessaging]].
## Firebase FCM токен

`utils/FirebaseTokenHelper.kt`:
- `getTokenAndSendToServer()` — берёт существующий или запрашивает токен, отправляет на сервер. Используется в **SplashActivity** при старте (уже залогинен).
- `deleteAndRefreshTokenThenSend()` — удаляет старый токен, получает новый, отправляет. Используется в **LoginActivity** после успешного логина (свежий вход).
- `refreshTokenAndSendToServer()` — принудительное обновление токена (вызывается из `BarkFluffFirebaseMessagingService.onNewToken()`).

## Обработка FCM data-only payload

`BarkFluffFirebaseMessagingService.onMessageReceived` диспатчит payload по полю `type`:

- `type = "new_message"` (по умолчанию, если поле не задано) — строит локальную нотификацию через `NotificationHelper.showMessageNotification` с аватаром и BigPictureStyle при наличии превью.
- `type = "dismiss_chat_notifications"` — вызывает `NotificationHelper.dismissForChat(context, chat_id)`, удаляя нотификацию чата из шторки. Шлётся бекендом ([[Backend/CloudMessaging]] / [[Backend/Updates]]) после прочтения сообщения, чтобы скрыть уведомление на остальных устройствах пользователя. На читавшем устройстве — no-op (нотификации уже нет).



- `MessageAttachmentType`: Unknown, Image, Video, Gif, Document, Audio(4), Voice, Sticker; **AUDIO=5** (добавлен в shared.proto)

## Соотношение сторон изображений в облачках

`MessageAdapter.buildMediaGrid` вычисляет размер ячеек:
- **Одно изображение**: высота ячейки = `cellWidth * imageHeight / imageWidth` (из `MessageAttachment.image_width` / `image_height`), ограничена диапазоном `[cellWidth/3 .. cellWidth*2]`. Если размеры равны 0 — квадратная ячейка как fallback.
- **Несколько изображений**: всегда квадратные ячейки (стандартная сетка).

Это позволяет облачку принять правильный размер **до** загрузки картинки, исключая прыжок высоты после загрузки.

Поля `image_width` (field 8) и `image_height` (field 9) добавлены в `MessageAttachment` в `shared.proto`.
Backend заполняет эти поля при доставке сообщения с вложением-изображением.

### Время на фото и стикерах

Для сообщения, состоящего только из фото/GIF/видео или стикера, `MessageAdapter` показывает время внутри визуального вложения: внизу справа находится компактная тёмная полупрозрачная плашка со светлым временем. У исходящих сообщений рядом применяются существующие векторные иконки статуса доставки. Плашка для медиа добавляется в `attachmentsContainer` из `view_media_time_status.xml`, для стикера она находится в `stickerContainer`; сообщения с текстовой подписью сохраняют обычное размещение времени рядом с текстом.

## Отправка файлов и pre-upload дедупликация (SHA-256)

Перед заливкой файла на S3 клиент проверяет, не существует ли уже такой файл на сервере по SHA-256-хешу — это экономит мобильный трафик при повторных отправках.

**Цепочка отправки** (`MediaSendService.processJob` → `ChatRepository.uploadFile`):

1. `MediaSendService.prepareAttachment` готовит байты (`PreparedAttachment.bytes`):
   - Документы — читаются как есть (`AttachmentSpec.Document`).
   - Картинки — сжимаются через `ImageCompressor.compressImage` (JPEG q=90, max 2500px по длинной стороне).
   - Видео — обрезка/перекодировка через `Transformer` в MP4.
   - Голосовые сообщения — `AttachmentSpec.Voice` читает записанный во внутреннем кеше `.ogg` и отправляет его как `UploadFileType.MESSAGE_ATTACHMENT_VOICE`.
2. `ChatRepository.uploadFile(jpegImageBytes, fileType, ...)` (`repository/ChatRepository.kt`) ДО получения upload URL:
   - Считает SHA-256 от итоговых байт через приватный extension `ByteArray.sha256Hex()` → lowercase hex, 64 символа.
   - Вызывает `grpcManager.checkFileHash(hash)` → `FilesApi.CheckFileHash` (gRPC, см. [[Backend/Files]] и [[Shared/Proto]]).
   - Если сервер вернул непустой `fileId` — `onProgress(100)` и сразу `Result.success(existingFileId)`, никакого HTTP POST. В Logcat: `File already exists on server (hash=..., reusing fileId: ...)`.
   - Если пустой ответ или ошибка вызова — fallback к обычному multipart-upload через `getUploadUrl` + S3 POST. Серверная пост-дедупликация (`UploadFileCommandHandler`) всё равно вернёт существующий `fileId` в JSON-ответе, если контент совпал.
3. `GrpcManager.checkFileHash(fileHash: String): Result<String>` — обёртка над `filesClient.checkFileHash(CheckFileHashRequest)`. На любом исключении (нет filesClient, gRPC error) возвращает `Result.failure` — вызывающая сторона (`uploadFile`) использует `getOrNull()` и тихо переходит к обычной загрузке.

**Что хешируется:** именно те байты, которые были бы залиты на сервер. Для картинки — уже сжатый JPEG, не оригинал из галереи. Это совпадает с тем, что хеширует backend при загрузке (`Backend/BarkFluff.Files/Features/UploadFile/UploadFileCommandHandler.cs`), поэтому дедупликация работает кросс-клиентно: файл, залитый с macOS-клиента, дедуплицируется при отправке с Android и наоборот.

**Не покрывается этим check'ом** (заливают мимо `ChatRepository.uploadFile`): загрузка аватара через `GrpcManager.uploadAvatar`/`uploadProfilePoster` — там свой путь, дедупликация только на сервере.

### Оптимистичный UI и прогресс отправки медиа

При отправке фото/видео `ChatActivity.handleMediaSend` сразу добавляет оптимистичное сообщение (`MessageItem` с `localId`, `uploadProgress`, `localPreviewUris`) и кидает `SendJob` в `MediaSendService`. Сообщение видно мгновенно — с локальным превью медиа и оверлеем прогресса поверх.

- **Локальное превью.** `MessageItem.localPreviewUris: List<Uri>` — URI исходных медиа (RawImage→uri, EditedImage→originalUri, Video→spec.uri; документы/стикеры превью не имеют). `MessageAdapter.buildLocalMediaGrid` рендерит ту же сетку, что и серверные вложения (`determineLayout` + `item_attachment_media_cell`, загрузка через Coil `.load(uri)`). Поэтому количество загружаемых файлов видно сразу как N миниатюр.
- **Прогресс.** `MediaSendService.aggregateProgress(idx, pct)`: при `sendSeparately` прогресс пофайловый (у каждого файла своё сообщение/localId), иначе — агрегированный по всем N файлам одного сообщения `(idx*100+pct)/total`, чтобы бар не сбрасывался в 0 на каждом следующем файле. События идут через `MediaSendService.uploadEvents` (`UploadEvent`: PREPARING/UPLOADING/SENDING/SENT/FAILED + `progress` + `serverMessageId`).
- **Реальная скорость.** `ChatRepository.uploadFile` ставит `connection.setFixedLengthStreamingMode(...)` и флашит каждый чанк — без этого `HttpURLConnection` буферизует тело в память и `onProgress` прыгал бы в 100% ещё до сетевой отправки.
- **Реконсиляция (защита от дубликата/пустого сообщения).** `ChatActivity.addNewMessage` ищет оптимистичный плейсхолдер ДО проверки дубликата, в обоих порядках прихода: realtime-эхо раньше ответа `sendMessage` (матч по контенту + `uploadProgress != null`/`localPreviewUris`, т.к. вложения плейсхолдера ещё пустые) ИЛИ `SENT` раньше эха (матч по уже проставленному `messageId` + `localId`). Без этого эхо с фото добавлялось бы вторым item'ом, а `clearOptimisticUploadProgress` оставлял первый с пустыми вложениями → два item с одним `messageId` → коллизия `DiffUtil.areItemsTheSame` (сравнение по `messageId`) → пустой bubble до переоткрытия чата.

### Голосовые сообщения

В `ChatActivity` правая кнопка ввода переключается между `ic_send_filled` и `ic_mic`: микрофон показывается только когда текст пустой, нет pending-вложений, reply и edit-режимов. При первом удержании запрашивается `RECORD_AUDIO`; после выдачи разрешения пользователь удерживает кнопку ещё раз.

- Запись: `MediaRecorder` пишет OGG/Opus (`OutputFormat.OGG`, `AudioEncoder.OPUS`) во временный файл `cacheDir`.
- Индикация записи (`showVoiceRecordingBar` / `hideVoiceRecordingBar`): на время записи `inputBar` уходит в `INVISIBLE` (кросс-фейд 160 мс), а поверх него, по тем же констрейнтам и с тем же фоном `bg_chat_input_bar`, показывается `voiceRecordBar` — мигающая точка `colorError` (`ValueAnimator` alpha 1↔0.25, REVERSE INFINITE), счётчик `M:SS` (корутина с тиком 200 мс) и подсказка отмены. Поле ввода не остаётся видимым, поэтому состояние записи нельзя спутать с обычным вводом.
- Отправка: отпускание кнопки создаёт оптимистичный `MessageItem` с `uploadProgress=0` и ставит `SendJob(AttachmentSpec.Voice)` в `MediaSendService`; upload идёт как `MESSAGE_ATTACHMENT_VOICE`, backend возвращает `MessageAttachmentType.VOICE` (см. [[Shared/Proto]]).
- Отмена: при удержании кнопку можно потянуть влево до середины экрана (`width * 0.5`); иконка краснеет, подсказка едет за пальцем (0.35 смещения) и меняет текст на «Отпустите для отмены», при отпускании запись удаляется и сообщение не отправляется.
- Cleanup: `onStop()` отменяет активную запись и удаляет временный файл. Слишком короткая запись (`<500ms`) не отправляется.
- Отображение: `MessageAdapter` оставляет обычный `AUDIO` на `SeekBar`, а `VOICE` показывает через `VoiceWaveformView` с палочками-таймлайном; амплитуды берутся из локального файла через `AudioWaveformExtractor` (`MediaExtractor`/`MediaCodec`) и кешируются по `fileId`. Голосовые вложения размером `1..2 МБ` автоматически скачиваются в `FileCache`; более крупные остаются с ручной кнопкой загрузки. Вкладка «Голосовые» в `UserProfileActivity` запрашивает `MessageAttachmentType.VOICE`.

### Вложения в профиле и группе

`UserProfileActivity` и `GroupInfoActivity` используют отдельные панели и `RecyclerView` для «Медиа» (постоянная сетка), «Файлов» и, в профиле, «Голосовых». Поэтому переключение вкладок не меняет `LayoutManager` у уже отображаемого списка и запоздалый ответ прежней вкладки не может показать чужую геометрию. Ответы дополнительно сверяются с текущей вкладкой и версией загрузки.

Во вкладке «Файлы» есть debounce 300 мс по имени документа. `ChatRepository.getChatAttachments(..., fileNameQuery)` передаёт `file_name_query` в `MessagesApi.ListChatAttachments`; при непустом поиске Android запрашивает первые 30 совпадений, без запроса — обычную первую страницу документов. Поле proto синхронизировано с [[Shared/Proto]], а поиск по полной истории и legacy-документам выполняет [[Backend/Messages]].

## Система кеширования

Четыре слоя кеша:
1. **Runtime URL-кэш** — `AvatarLoader.urlCache` (`ConcurrentHashMap<fileId, URL>`, in-memory)
2. **Persistent URL-кэш** — `FileUrlCache` → SharedPreferences `"file_url_cache"` (SHA-256 keys)
3. **gRPC-запрос** — `getFileDownloadUrl(fileId)` к Files API
4. **Coil Image Cache** — memory (25% RAM) + disk `cacheDir/image_cache/` (10% storage), key=fileId

Бинарные файлы (аудио/видео/документы):
- `FileCache` (`utils/FileCache.kt`) — singleton disk cache, путь: `cacheDir/media_files/`
- `AudioPlayerHelper` (`utils/AudioPlayerHelper.kt`) — MediaPlayer singleton, один аудио за раз
- `ImageGridAdapter` (`adapter/ImageGridAdapter.kt`) — квадратная сетка с `SquareImageView`
- `SquareImageView` (`views/SquareImageView.kt`) — `onMeasure` устанавливает height=width
- `AspectRatioImageView` (`views/AspectRatioImageView.kt`) — `onMeasure` устанавливает height=width*3/2 (2:3, для превью фонов)
- `MediaViewerActivity` — ExoPlayer, не fullscreen, swipe-down dismiss
- `ImageViewerActivity` — без fullscreen, swipe-down dismiss
- `ChatRepository.downloadFile()` — HTTP download → FileCache

## ExoPlayer

```
androidx.media3:media3-exoplayer:1.3.1
androidx.media3:media3-ui:1.3.1
```

## Пересылка и ответы (Forward / Reply)

Reply и forward — **разные вещи** и на бэкенде тоже (см. [[Backend/Messages]]). Ответ едет `OutgoingMessage.reply_to_message_id` и приходит обратно полем `Message.reply_to` (`ReplyInfo`); пересылка едет `forwarded_message_ids` (до 20) и приходит вложениями `FORWARDED_MESSAGE`.

**Выбор UI** (в `MessageAdapter.bindQuoteSplit`): reply рисуется, если заполнен `item.replyTo` — компактный блок (вертикальная полоска `?attr/colorPrimary` + автор + 1 строка превью). Пересылки рисуются по вложениям, **по блоку на каждое** (`MaterialCardView` с автором, текстом и медиа-сеткой через `setupAttachmentsContainer`). Reply и forward больше не исключают друг друга.

Прежняя эвристика «оригинал есть в текущей загруженной истории» (`hasMessageInCurrentList`) удалена: из-за неё ответ превращался в пересылку, стоило прокрутить чат. У удалённого оригинала сервер не отдаёт ни текст, ни автора — цитата показывает «Сообщение удалено» и не кликается.

Layout цитаты: `view_message_quote.xml`. Reply — один `<include android:id="@+id/replyQuote">`; пересылки — `LinearLayout` `forwardQuotesContainer`, в который адаптер инфлейтит по копии на каждое пересланное сообщение (схлопнуть пачку в один блок значит потерять всё, кроме первого). Универсальный layout переключается между `replyView` / `forwardView` по visibility.

При рендере основного бабла FORWARDED_MESSAGE-вложения **исключаются** из `setupAttachmentsContainer` (фильтр `displayedAttachments`), чтобы не задвоить.

### Ответ (reply) в открытом чате

- **Action menu**: клик по корневому `FrameLayout` строки сообщения (вне bubble — там, где padding 80dp слева/справа) открывает `PopupWindow` (`popup_message_actions.xml`). Базовые пункты: Ответить / Изменить / Удалить / Переслать / Закрепить. Закрепить — заглушка `Toast "Скоро будет"`. Изменить/Удалить — реализованы, скрываются через `View.GONE` для чужих сообщений.
- **Подсветка выбранного сообщения**: `ChatActivity.showMessageActionMenu` находит `R.id.messageCard` внутри anchor, сохраняет `originalForeground` и устанавливает полупрозрачный `ColorDrawable(?attr/colorPrimary)` (~24% alpha) как `foreground`. На `popup.setOnDismissListener` — восстановление. Анимация не нужна: `popup.isFocusable=true` блокирует прокрутку RecyclerView, поэтому ViewHolder не перепривязывается.
- **Контекстные пункты (по содержимому сообщения)**:
  - **Копировать текст** (`actionCopyText`) — если `item.text.isNotBlank()`. `ClipData.newPlainText`.
  - **Скопировать изображение** (`actionCopyImage`) — если ровно одна картинка (IMAGE/GIF). Скачивание в `FileCache`, `FileProvider.getUriForFile(...)`, `ClipData.newUri`.
  - **Сохранить изображение / Сохранить изображения** (`actionSaveImages`) — если ≥1 картинка. Текст меняется по количеству. Сохраняет в `Pictures/BarkFluff/` через `FileSaveUtils.saveImageToGallery` (видно в Галерее).
  - **Сохранить в загрузки** (`actionSaveDocs`) — если ≥1 документ (DOCUMENT). Сохраняет в `Downloads/BarkFluff/` через `FileSaveUtils.saveToDownloads`.
- **Места сохранения файлов** (`utils/FileSaveUtils.kt`): картинки → `Pictures/BarkFluff` (MediaStore.Images), видео и документы → `Downloads/BarkFluff` (MediaStore.Downloads). На API < Q используется `Environment.getExternalStoragePublicDirectory(...)` + `uniqueFile(...)` для дедуплицирования имён.
- **Вьюверы**: `ImageViewerActivity` содержит нижний M3 Expressive Floating Toolbar: сохранить, копировать и переслать. Сохранение и копирование скачивают/берут оригинальный файл из `FileCache` и сохраняют его формат/качество; сохранение идёт в `Pictures/BarkFluff` через `FileSaveUtils.saveImageToGallery`, копирование кладёт `FileProvider` URI с корректным MIME в системный буфер. Для пересылки `MessageAdapter` и медиагалерея `UserProfileActivity` передают в viewer параллельные `fileNames` и `sourceMessageIds`; текущая фотография пересылается готовым `ForwardChatPickerBottomSheet`. При отсутствии ID сообщения кнопка пересылки недоступна. `MediaViewerActivity` содержит скрываемую по тапу на видео нижнюю M3 Expressive Floating Toolbar: play/pause, таймлайн и скачивание. Таймлайн обновляется одной coroutine раз в 250 мс, не перезаписывается во время drag; скачивание сохраняет исходный файл (включая переданный `cachedPath`) через `FileSaveUtils.saveToDownloads` в `Downloads/BarkFluff`.
- **Long-press на вложениях**: меню картинок удалено целиком (`menu_image_attachment.xml` удалён) — теперь сохранение картинок только через основное меню. У документов осталось только «Удалить из кеша» (показывается лишь если файл закеширован). У аудио — без изменений.
- **Свайп влево**: `ReplySwipeCallback : ItemTouchHelper.SimpleCallback(0, ItemTouchHelper.LEFT)`. При сдвиге `>= 64dp` — haptic + триггер. После отпускания bubble возвращается на место (`onSwiped` пуст, `clearView` сбрасывает `translationX`). Иконка стрелки рисуется справа в `onChildDraw` с alpha по прогрессу.
- **Reply preview bar**: `replyPreviewBar` в `activity_chat.xml` — `MaterialCardView` над `attachmentPreviewBar`, показывается при `pendingReplyMessageId != 0L`. Содержит автора, превью текста (или "📷 N фото" / "📎 N файлов"), кнопку отмены `clearReplyButton`.
- **Отправка**: `ChatRepository.sendMessage(..., replyToMessageId = pendingReplyMessageId)`. После успеха — `clearPendingReply()`.

### Редактирование и удаление сообщений

Реализовано через `EditMessage` / `DeleteMessage` gRPC из [[Backend/Messages]] и стримы `SubscribeMessagesEdited` / `SubscribeMessagesDeleted` из [[Backend/Updates]].

- **Repository** (`ChatRepository.editMessage(messageId, text, fileIds)` / `deleteMessage(messageId)`): обёртки над gRPC-вызовами. Возвращают `Result<Shared.Message>` / `Result<Unit>`.
- **RealtimeService** (`grpc/RealtimeService.kt`): два дополнительных `MutableSharedFlow` — `messageEdited`, `messageDeleted`. Стартует две корутины `streamWithReconnect("MessagesEdited"/"MessagesDeleted")` в `resume()`.
- **ChatActivity состояние**: `pendingEditMessageId: Long`, `pendingEditFileIds: List<String>`. Если != 0 → `sendMessage()` вызывает `sendEdit()` вместо `chatRepository.sendMessage()`.
- **Edit preview bar**: `editPreviewBar` в `activity_chat.xml` — `MaterialCardView` с заголовком «Редактирование сообщения» и превью текста, привязан к `attachmentPreviewBar` (как `replyPreviewBar`). Кнопка отмены `clearEditButton`.
- **Edit-режим**: `setPendingEdit(item)` подставляет текст в `messageEditText`, фокусирует поле, открывает клавиатуру. Сохраняет существующие `file_id` (без FORWARDED_MESSAGE) в `pendingEditFileIds` — backend сохраняет их при правке без изменений. Edit и reply — взаимоисключающие, при входе в edit активный reply сбрасывается.
- **Delete-режим**: `confirmAndDelete(item)` показывает `MaterialAlertDialogBuilder` («Удалить сообщение?» / «Удалить» / «Отмена»). При подтверждении — `chatRepository.deleteMessage()`, при успехе сразу `removeMessageById(messageId)` (UI-обновление до прихода стрима).
- **Применение событий**: `applyEditedMessage(msg)` и `removeMessageById(id)` модифицируют `messageAdapter.currentList` через `submitList`. Вызываются и из обработчика ответа на gRPC (для своих изменений), и из подписок на стримы (для изменений других участников).

#### Перерисовка списка: почему не `notifyDataSetChanged`

`notifyDataSetChanged()` помечает всю структуру невалидной: `RecyclerView` теряет пул холдеров, отбрасывает анимации `MessageItemAnimator` и не гарантирует позицию скролла. Для чата со `stackFromEnd = true` и пагинацией в обе стороны это заметно, поэтому по `messageAdapter` он не используется вовсе.

- `loadGroupMemberInfo()` (подгрузка `groupMemberInfoCache`) меняет только имя и мини-аватар отправителя, поэтому шлёт `notifyItemRangeChanged(0, itemCount, MessageAdapter.PAYLOAD_SENDER_INFO)`. В `MessageAdapter` есть перегрузка `onBindViewHolder(holder, position, payloads)`: при этом payload вызывается только `ReceivedMessageViewHolder.bindSenderInfo(item)` (тот же метод, что и из полного `bind`), при пустом или незнакомом payload — делегирование в `super`, иначе частичные обновления от `ListAdapter`/`ItemAnimator` потеряли бы биндинг. Без этого каждое открытие группового чата давало полный ребинд с перезапуском загрузки вложений.
- `onResume()` отражает изменения настроек (`messageCornerRadiusDp`, `stickerSizeDp`) и состояния `FileCache`. Состояние кэша читается из четырёх разных мест бинда, отдельный payload потребовал бы переработки биндов — поэтому здесь просто `notifyItemRangeChanged(0, itemCount)`: содержимое перерисовывается так же, но структура списка остаётся валидной.
- **Метка «изменено»**: `MessageItem.isEdited: Boolean` пробрасывается в три места создания (`messagesWithDateSeparators`, `appendMessages`, `addNewMessage`). В layouts `item_message_sent.xml` / `item_message_received.xml` рядом со временем — `editedLabelTextView` (italic, alpha 0.6, `?attr/colorOnPrimaryContainer` или `?attr/colorOnSurfaceVariant`). Привязка в обоих ViewHolder: `editedLabelTextView.visibility = if (item.isEdited) VISIBLE else GONE`.
- **Proto**: добавлены `EditMessage` / `DeleteMessage` rpc в `messages_api.proto`, `SubscribeMessagesEdited` / `SubscribeMessagesDeleted` в `updates_api.proto`, `is_edited` (field 7) и `edited_at` (field 8) в `shared.proto:Message`.

### Пересылка в другие чаты (forward)

`ForwardChatPickerBottomSheet` (`dialog/ForwardChatPickerBottomSheet.kt`):
- `BottomSheetDialogFragment` с `STATE_EXPANDED` + `skipCollapsed=true`.
- Загружает чаты через `grpcManager.getChats()` (та же сортировка по `lastMessage.sentAt`, что в `ChatsFragment`).
- `ForwardChatPickerAdapter` — multi-select через `selectedIds: LinkedHashSet<String>`, click тоглит CheckBox и вызывает `notifyItemChanged`.
- Кнопка "Переслать (N)" активируется при `count > 0`. При нажатии — параллельный `async/awaitAll` вызов `chatRepository.sendMessage` для каждого выбранного чата со списком `forwardedMessageIds` и опциональным комментарием. `newInstance` принимает `LongArray`, поэтому пачка уезжает одним сообщением на чат.
- Layouts: `bottom_sheet_forward_chats.xml`, `item_chat_forward_picker.xml`.

## UI — Экран чата (ChatActivity + activity_chat.xml)

- `activity_chat.xml`: ConstraintLayout, слои по z-order (снизу вверх):
  1. `chatBackgroundImage` — фоновое изображение (ImageView на весь экран)
  2. `chatDimOverlay` — оверлей затенения фона (View, `visibility=gone` при dim=0)
  3. `messagesRecyclerView` + панели кнопок (с elevation)
  4. `stickerPreviewOverlay` — поверх всего при предпросмотре стикера

### Редизайн по макету M3E «Вариант 3»

Цвета так же смаплены на роли темы, а не зашиты (см. одноимённый раздел экрана списка чатов).

- **Шапка `chatHeaderBar`** заменила плавающую карточку `chatInfoCard` + отдельные круглые кнопки: плоская панель на `colorSurface` — `[←] [имя + статус] [☎] [⋯] [аватар 44dp]`. `chatInfoCard` остался id кликабельной области «имя + статус» (теперь LinearLayout), поэтому переходы в `UserProfileActivity` / `GroupInfoActivity` не менялись.
- **Порядок в XML важен**: блок шапки объявлен *после* `messagesRecyclerView`, иначе фон чата и лента рисовались бы поверх неё. Лента привязана к шапке через forward reference `@+id/chatHeaderBar`, а сама шапка резервирует верхний inset своим `paddingTop` (раньше это делали три отдельных `topMargin`).
- Когда у чата есть обои (`setupChatBackground`), фон шапки переключается на `TRANSPARENT`, чтобы изображение продолжалось под ней; без обоев возвращается `colorSurface`.
- **Схлопывание инвертировано относительно списка чатов**: прокрутка **вверх** (в историю) уменьшает имя 22→17sp, схлопывает `chatStatusContainer` и поднимает `translationZ` шапки до 4dp; прокрутка вниз возвращает (`updateHeaderCompact` / `setHeaderCompact`, 280 мс). Поле ввода не скрывается никогда.
- **Пузыри группируются по сериям одного отправителя** (`MessageAdapter.groupPositionOf` по соседям в списке; любой не-`MESSAGE` элемент прерывает серию). Форма — `applyBubbleShape` через `ShapeAppearanceModel`: base = `chatMessageCornerRadius` (дефолт поднят 20→**28dp**), середина серии = base/2, «хвостик» последнего сообщения = 8dp. Отступ между сериями 10dp, внутри серии 3dp (`applyGroupSpacing`). Настройка радиуса из персонализации продолжает работать — она задаёт base.
- Текст сообщения 17sp/24, время в пузыре — weight 600. Разделитель дат (`item_message_date_separator.xml`, используется и для разделителя непрочитанных) — pill-чип `colorPrimaryContainer` без линий по бокам.
- **Грядка ввода**: `TextInputLayout` + четыре отдельные круглые кнопки заменены на `inputBar` — pill с минимумом 52dp (`bg_chat_input_bar`) со скрепкой, `EditText` (`messageEditText`, уже не `TextInputEditText`) и стикерами внутри. Поле допускает до 8 строк, `inputBar` растёт по высоте вверх, а нижний padding `messagesRecyclerView` пересчитывается по фактической высоте. `inputRowBarrier` теперь ссылается на `inputBar` и `sendButton`.
- **Морфинг кнопки отправки** (`applySendButtonShape`): пустой ввод — круг 52dp `colorPrimaryContainer` с микрофоном, есть что отправить — компактная pill 68dp/radius 18dp `colorPrimary` со стрелкой. Фон — программный `GradientDrawable` (анимируются ширина, радиус, цвет; 300 мс). Голосовой режим и drag-to-cancel не менялись, но тинт иконки при записи теперь `colorOnPrimaryContainer` / `colorError`.
- Плашки `replyPreviewBar` / `editPreviewBar` / `attachmentPreviewBar` выровнены по грядке (отступ 14dp) и получили форму 28dp сверху / 12dp снизу (`ShapeAppearanceOverlay.Barkfluff.Chat.InputPreviewBar`).
- Реакции на сообщения из макета не переносились — на бэкенде их нет.
- **Затенение фона (`chatBackgroundDim`)**: применяется в `ChatActivity.applyDimOverlay()` при старте. Цвет оверлея — `android.R.attr.colorBackground` (фон окна из темы), что автоматически адаптируется к светлой/тёмной теме. Alpha = `dim% / 100 * 255`.
- Аналогичная логика в превью `PersonalizationSettingsActivity.updatePreviewDim()`.

## Markdown в сообщениях

Бэкенд хранит текст сообщения как обычную строку с символами разметки — интерпретация markdown целиком на клиенте. Только V1. Кастомный рендерер, без сторонних библиотек.

- **`utils/MarkdownRenderer.kt`** (`object`) — line-based парсер `markdown → SpannableStringBuilder`:
  - Блоки: заголовки `#…######` (`RelativeSizeSpan` + bold), маркированные `-/*/+` (`BulletSpan`) и нумерованные `1.` (`LeadingMarginSpan`) списки, цитаты `>` (`QuoteSpan` + приглушённый цвет), горизонтальные линии `---/***/___`, ограждённые блоки кода ` ``` ` (monospace + фон + отступ).
  - Inline: `**bold**`/`__bold__`, `*italic*`/`_italic_`, `~~strike~~`, `` `code` `` (monospace + фон), `[текст](url)` (`URLSpan`). Inline-код защищён от повторного разбора.
  - HTML allowlist из README: `p`/`h1…h6` с `align=left|center|right`, `strong`, `sub`, `a[href]`, `img[src,alt,width,height]`. Ссылки принимают `http(s)`/`mailto`, `src` — только `http(s)`; HTML-картинка строится отдельным `ImageView` через Coil, центрирование применяется и к ней. Относительные/fragment URL не имеют безопасной базы внутри сообщения: ссылка становится обычным текстом, а картинка выводит `alt` как fallback. Другие теги и атрибуты не интерпретируются.
  - Автолинковка «голых» URL через `Patterns.WEB_URL` — вручную, чтобы не затирать markdown-ссылки (в отличие от `Linkify.addLinks`, который стирает существующие `URLSpan`).
  - Цвета code-фона/цитаты — alpha-overlay поверх `textView.currentTextColor` (работает и на sent, и на received пузыре).
  - В bubble сообщений `renderMessageInto` распознаёт GFM-таблицу по шапке и строке-разделителю, строит нативные `TableLayout`-ячейки с уже существующей inline-разметкой, выравниванием `:---`/`:---:`/`---:` и горизонтальной прокруткой для широких таблиц. Парсер учитывает экранированный `\|` и пайп внутри inline-кода; короткие строки дополняет пустыми ячейками. Жест, начатый над прокручиваемой таблицей, не перехватывается swipe-to-reply, поэтому горизонтальный drag листает таблицу, а не открывает ответ.
  - `applyTo(textView, source)` — ставит текст, линкует, включает/**сбрасывает** `movementMethod` (сброс критичен для переиспользования ViewHolder). `strip(source)` — убирает всю разметку в чистый однострочный текст для превью.
- **Рендер (`renderMessageInto`)**: главный пузырь sent/received (`MessageAdapter` ~315/484, покрывает и закреплённые через `PinnedMessagesActivity`). E2E-чаты (приватный/секретный) рендерятся тем же `MessageAdapter` в общем `ChatActivity` (расшифрованный текст мапится в `MessageItem`).
- **`autoLink="web"` убран** из `item_message_sent.xml` / `item_message_received.xml` — заменён Linkify внутри рендерера. Контекстное меню сообщения висит на `binding.root.setOnClickListener` (не long-press), поэтому `LinkMovementMethod` сосуществует с тап-в-меню как и раньше.
- **Strip (чистый текст в превью)**: reply-превью (`buildPreviewLine`), тело пересланного сообщения (`forwardTextTextView`), последнее сообщение в списке чатов (`ChatAdapter`), пуш-уведомление (`NotificationHelper`). Иначе в однострочных превью светились бы символы `**`, `~~`, `` ` ``.
- Вложенные списки не поддерживаются; таблицы и HTML-картинки поддержаны только в bubble сообщений.

## Typing-индикатор («печатает…»)

Клиентская интеграция готового API [[Backend/Onliner]] (typing = relay-модель, см. там же). Только V1; список чатов не затронут — индикатор живёт в шапке открытого чата.

- **RealtimeService** (`core/grpc/RealtimeService.kt`): `typingEvents: SharedFlow<TypingEvent>` + стрим `streamWithReconnect("Typing") { collectTyping() }` в `resume()`; `@Volatile subscribedTypingChatIds` читается при каждом открытии стрима (переживает pause/resume). `changeTypingSubscription(chatIds)` — fire-and-forget `ChangeChatsInTypingSubscription` с одним retry через 2с (гонка `FailedPrecondition`, пока стрим не открыт). `sendTypingStatus(chatId, typing)` — fire-and-forget unary heartbeat.
- **ChatActivity — отправка**: TextWatcher → `onTypingInput(s)`: непустой ввод запускает heartbeat-job (TYPING каждые 4с, стоп при idle ≥5с без CANCELLED); пустое поле / отправка сообщения (программный `text?.clear()` триггерит TextWatcher) / `onStop()` → `stopTypingHeartbeat(sendCancel=true)` → CANCELLED. Флаг `suppressTypingInput` подавляет ложный TYPING при программном `setText()` в `setPendingEdit` (edit-режим).
- **ChatActivity — приём**: collect `typingEvents` в `subscribeToRealtimeEvents()`; фильтр по chatId (case-insensitive) и своему userId; `typingUsers: LinkedHashMap<Long, Job>` — job гашения 6с на каждого печатающего. Подписка: `onCreate()` → `changeTypingSubscription(listOf(chatId))`, `onDestroy()` → пустой список.
- **UI**: `renderTypingIndicator()` пишет в `onlineStatusTextView`. 1:1 — «печатает…» вместо онлайн-статуса, восстановление через `lastStatusText` (хелпер `applyOnlineStatus` не даёт онлайн-статусу перетереть typing-текст). Группа — `VISIBLE` + имена из `groupMemberInfoCache` (недостающие догружаются асинхронно, дедуп через `pendingTypingNameFetches`), plurals `typing_indicator_named`, макс 3 имени; после гашения — `GONE`.
- **Строки**: `typing_indicator` + plurals `typing_indicator_named` в values (RU), values-en/de/es/zh-rCN.
- Private/secret чаты не затронуты (отдельные Activity).

## FastAuth QR-сканер (DevicesActivity → QrScannerActivity → FastAuthConfirmActivity)

Флоу: авторизованный мобильный клиент сканирует QR нового устройства и подтверждает/отклоняет вход.

**Зависимости (app/build.gradle.kts):**
- CameraX 1.4.2: `camera-core`, `camera-camera2`, `camera-lifecycle`, `camera-view`
- ML Kit: `com.google.mlkit:barcode-scanning:17.3.0`

**Файлы:**
- `QrScannerActivity.kt` — CameraX + ML Kit (QR_CODE), при обнаружении QR вызывает `grpcManager.scanFastAuth()`, при успехе переходит в FastAuthConfirmActivity
- `FastAuthConfirmActivity.kt` — отображает метаданные нового устройства (имя, ОС, приложение, IP), кнопки «Подтвердить» / «Отклонить», вызывает `acceptFastAuth` / `rejectFastAuth`
- `views/ScannerOverlayView.kt` — кастомная View с полупрозрачным оверлеем и угловыми уголками (рисуется через Canvas, `LAYER_TYPE_SOFTWARE`)

**GrpcManager — новые поля и методы:**
- `fastAuthClient: FastAuthApiGrpcKt.FastAuthApiCoroutineStub?`
- `createFastAuthClient(address, context, includeDeviceInfo)` — с `AuthInterceptor` + `DeviceInfoInterceptor`
- `suspend fun scanFastAuth(fastAuthId: String): Result<ScanFastAuthResponse>`
- `suspend fun acceptFastAuth(fastAuthId, confirmationCode): Result<Unit>`
- `suspend fun rejectFastAuth(fastAuthId, confirmationCode): Result<Unit>`

**DevicesActivity:**
- `buttonConnectDevice` использует `ActivityResultLauncher<Intent>` → `QrScannerActivity`
- На RESULT_OK — вызывает `loadSessions()` для обновления списка устройств
- Проверяет `CAMERA` permission перед запуском; при отказе — подсказка о настройках

## E2E чаты (приватные + секретные)

Stage 6 плана `messages-crystalline-axolotl.md` — на Android реализован клиентский слой E2E.

### Зависимости (libs.versions.toml + build.gradle.kts)

- `org.signal:libsignal-android:0.86.16` + `org.signal:libsignal-client:0.86.16` — Signal Double Ratchet для секретных чатов. Maven repo: `https://build-artifacts.signal.org/libraries/maven/` добавлен в `settings.gradle.kts`.
- `com.lambdapioneer.argon2kt:argon2kt:1.6.0` — Argon2id для приватных чатов.
- `coreLibraryDesugaring("com.android.tools:desugar_jdk_libs:2.1.4")` + `sourceCompatibility = VERSION_17` — требование libsignal (Java records).
- `packaging.resources.excludes` — `libsignal_jni*.dylib`, `signal_jni*.dll`, `**/libsignal_jni_testing.so` чтобы не тащить native-либы других платформ в APK.

### Crypto-модуль `com/barkfluff/client/crypto/`

- `PrivateChatCrypto.kt` — Argon2id (t=3, m=64MiB, p=4) → 32-байтный ключ → AES-256-GCM (nonce 12 байт, GCM tag 128 бит). HMAC-SHA256(key, "BARKFLUFF_PRIVATE_CHAT_VERIFIER") — passphrase verifier для проверки на стороне приглашённого. AAD = `barkfluff:private:{chatId}`.
- `BarkFluffSignalStore.kt` — реализация `org.signal.libsignal.protocol.state.SignalProtocolStore` поверх `EncryptedSharedPreferences("barkfluff_signal_store")`. Все записи (identity-key, sessions, prekeys, signed prekeys, kyber prekeys, sender keys) сериализуются через `.serialize() → Base64`. `saveIdentity` возвращает `IdentityKeyStore.IdentityChange`, `markKyberPreKeyUsed(int, int, ECPublicKey)` — no-op (Kyber не используется в текущем proto).
- `PrekeyManager.kt` — генерация identity-key (`IdentityKeyPair.generate()`), registration_id (`KeyHelper.generateRegistrationId(false)`), signed prekey (ручной: `ECKeyPair.generate()` + `identityPriv.calculateSignature(pubBytes)`), 100 one-time prekeys (`PreKeyRecord(id, ECKeyPair.generate())`). Метаданные регистрации в `barkfluff_prekey_state` (plain prefs, не sensitive).
- `E2EBootstrap.kt` — `ensurePrekeyBundleRegistered(context)`: при первом логине вызывается из `MainActivity.onCreate` → генерит bundle → `UsersApi.RegisterPrekeyBundle` → `PrekeyManager.persistBundle()`. Идемпотентно. Также `replenishIfNeeded(remaining)`.
- `EncryptedInviteHandler.kt` — слушает 4 стрима (`privateChatInvites`, `privateChatInviteResolutions`, `secretChatInvites`, `secretChatResolutions`) из `RealtimeService` и показывает MaterialAlertDialog. При accept приватного — запрашивает passphrase, валидирует verifier, вызывает `PrivateChatRepository.acceptPrivateChatInvite`. Регистрируется в `MainActivity.onCreate`.

### Repositories `com/barkfluff/client/repository/`

- `PrivateChatRepository.kt` — фасад приватных чатов:
  - `createPrivateChat(peerId, passphrase)`: генерит salt+key+verifier, gRPC, кеширует key в `EncryptedSharedPreferences("barkfluff_private_chat_keys")` ключ=chatId.
  - `acceptPrivateChatInvite(chatId, passphrase, salt, verifier)`: derive → validate → если ОК, gRPC accept + кеширует key.
  - `unlockExistingChat(chat, passphrase)`: для повторного открытия после разлогина или с другого устройства.
  - `sendText/editText/deleteMessage/listMessages/decryptIncoming` — encrypt/decrypt поверх gRPC. Если ключ забыт — `KeyNotAvailableException`.
- `SecretChatRepository.kt` — фасад секретных чатов:
  - `createSecretChat(peerUserId, peerDeviceId, initialPlaintext)`: fetch peer bundle → SessionBuilder.process → SessionCipher.encrypt (PreKeySignalMessage) → SendSecretChatInvite. Метаданные локального чата (id, peer, inviteId, role) в `EncryptedSharedPreferences("barkfluff_secret_chats")`.
  - `acceptIncomingInvite(inviteId, sender, envelope)`: decrypt PreKeySignalMessage → создать локальную SecretChat запись → AcceptSecretChatInvite gRPC.
  - `sendMessage/decryptIncoming/ack` — runtime-операции через SessionCipher.
  - **Лимитация**: libsignal 0.86+ требует Kyber prekey в `PreKeyBundle` (PQXDH), а текущий proto `barkfluff.users.PrekeyBundle` хранит только X25519. Метод `toLibsignal()` бросает `UnsupportedOperationException` с пояснением — требуется расширить proto Kyber-полями + backend Users (в плане как future work). Приватные чаты от этого не зависят и работают полностью.

### gRPC методы (в `GrpcManager.kt`)

Добавлены 16 новых методов:
- Приватные: `createPrivateChat`, `acceptPrivateChat`, `rejectPrivateChat`, `sendPrivateMessage`, `listPrivateMessages`, `editPrivateMessage`, `deletePrivateMessage`, `getChat`.
- Секретные: `sendSecretChatInvite`, `acceptSecretChatInvite`, `rejectSecretChatInvite`, `sendSecretMessage`, `ackSecretMessage`.
- Prekey-bundle: `registerPrekeyBundle`, `fetchPrekeyBundle`, `listPeerDevices`, `replenishOneTimePrekeys`, `rotateSignedPrekey`.

### RealtimeService — 8 новых SharedFlow + collectors

`privateMessages`, `privateMessageEdits`, `privateMessageDeletes`, `privateChatInvites`, `privateChatInviteResolutions` (user-scope) + `secretChatInvites`, `secretChatResolutions`, `secretMessages` (device-scope; маршрутизация на `RecipientDeviceId` из JWT). Подписки запускаются в `RealtimeService.resume()` через `streamWithReconnect("…")` с тем же exponential backoff, что у существующих стримов.

### UI

- `CreateEncryptedChatActivity` (+ `activity_create_encrypted_chat.xml`) — единый экран создания: выбор типа (Private/Secret через MaterialButtonToggleGroup), ввод peer userId, passphrase ИЛИ peer device + initial message. Spinner устройств заполняется через `ListPeerDevices`.
- Приватный и секретный чаты отображаются в **общем `ChatActivity`** (не в отдельных активити): `ChatActivity.onCreate` по `EXTRA_CHAT_KIND` (`KIND_REGULAR`/`KIND_PRIVATE`/`KIND_SECRET`) на E2E-типах уходит в `setupE2eShell()` и делегирует логику контроллеру. Shell переиспользует `activity_chat.xml`/шапку/`MessageAdapter`, но только текст: прячет вложения/стикеры/голос/меню/звонок/закреплённые. Интенты собираются хелперами `ChatActivity.privateChatIntent` / `secretChatIntent`.
- `PrivateChatController` — приватный чат: загрузка через `listPrivateMessages` (расшифровка inline), отправка `sendText`, реалтайм `realtimeService.privateMessages`, машина состояний инвайта (`e2eInviteContainer`/`e2eBanner`). При отсутствии локального ключа — диалог passphrase + `unlockExistingChat`.
- `SecretChatController` — секретный чат, без истории с сервера (не хранит). Только локальный кэш + runtime-сообщения через `realtimeService.secretMessages` + `decryptIncoming` + `ack`. Строковый `messageId` мапится в `Long` для `MessageItem` через стабильный in-session маппинг.
- `ChatsFragment` — кнопка `encryptedChatButton` в toolbar открывает `CreateEncryptedChatActivity`.
- AndroidManifest — отдельные `PrivateChatActivity`/`SecretChatActivity` удалены (слиты в `ChatActivity`); `CreateEncryptedChatActivity` остаётся.

### Storage prefs

| Имя | Содержимое |
|-----|-----------|
| `barkfluff_signal_store` | EncryptedSharedPreferences. Identity-key, sessions, prekeys (libsignal сериализация). |
| `barkfluff_prekey_state` | Plain prefs. Флаги `prekey_registered`, `prekey_next_one_time_id`, `prekey_next_signed_id`. |
| `barkfluff_private_chat_keys` | EncryptedSharedPreferences. chatId → Base64(AES-256 key). |
| `barkfluff_secret_chats` | EncryptedSharedPreferences. secretChatId → `peerUserId\|peerDeviceId\|inviteId\|role\|accepted`. |

## Раздел «Тестирование» (dev/QA-флаги)

В настройках профиля под пунктом «О приложении» есть пункт **Тестирование** (`itemTesting`, иконка `ic_science`), открывающий `TestingSettingsActivity`. Раздел содержит локальные `MaterialSwitch`-флаги, читаемые/записываемые через `data/GlobalParam.kt`. Все ключи — в `barkfluff_prefs` (plain SharedPreferences).

| Свойство `GlobalParam` | Ключ prefs | Что включает |
|---|---|---|
| `showIdsInProfile` | `testing_show_ids_in_profile` | ID-строки в `UserProfileActivity` и ChatId-карточка в `GroupInfoActivity`. В профиле пользователя показывает `UserId: <otherUserId>` и `ChatId: <chatId>`, в профиле группы — `ChatId: <chatId>`. Тап по строке копирует значение в `ClipboardManager` + Toast. |
| `showServerAddressesInAbout` | `testing_show_server_addresses_in_about` | Карточка диагностических адресов в `AboutActivity`. Кнопка «Проверить доступность» параллельно вызывает анонимный `GET /ping` на настроенных [[Backend/Beacon|Beacon]], [[Backend/Identity|Identity]], [[Backend/Users|Users]], [[Backend/Files|Files]], [[Backend/Messages|Messages]], [[Backend/Updates|Updates]], [[Backend/Onliner|Onliner]], [[Backend/FastAuth|FastAuth]] и [[Backend/Calls|Calls]]; каждая строка показывает доступность и время запроса. |
| `secretChatsEnabled` | `testing_secret_chats_enabled` | `encryptedChatButton` в шапке `ChatsFragment` (иконка `ic_hood`, открывает `CreateEncryptedChatActivity`). По умолчанию кнопка `View.GONE`; видимость переоценивается в `onViewCreated` и `onResume`, чтобы переключение в TestingSettings подхватывалось при возврате. |

Оба флага по умолчанию `false` — обычная сборка не показывает ни блок ID, ни кнопку скрытых чатов.

## Раздел «Персонализация» — локальные параметры

Выбранное изображение фона синхронизируется через [[Backend/Users]]: при login и Splash `GrpcManager.getUserSettings()` загружается параллельно с профилем и обновляет кэш `GlobalParam`. `ChatActivity` повторяет запрос при открытии и возврате: это покрывает быстрый offline-start, в котором Splash сразу открывает сохранённый список чатов. `chatBackgroundFileId` — глобальный фон, `chatBackgroundOverrides` — map `chatId → fileId`; UUID-ключи нормализуются, а `ChatActivity` выбирает override, иначе глобальное значение. Устаревшая асинхронная загрузка глобального изображения не может перерисовать уже полученный override. Старые локальные выбранные изображения намеренно заменяются серверным ответом. Blur, затемнение и скругление пузырей остаются локальными.

`PersonalizationSettingsActivity` устанавливает глобальный фон через `SetGlobalChatBackground`. В `UserProfileActivity` и `GroupInfoActivity` доступен selector фона конкретного чата: «Использовать глобальный фон» удаляет override через `SetChatBackground(chatId, "")`; прочие пункты используют каталог `GetPersonalization`.

`PersonalizationSettingsActivity` хранит локальные параметры в `GlobalParam` (`barkfluff_prefs`). Блок «Папки» содержит три `MaterialSwitch`:

| Свойство `GlobalParam` | Ключ prefs | Что включает |
|---|---|---|
| `compactFolders` | `folders_compact` | Компактные папки: в сегменте `ChatsFragment.foldersRecyclerView` скрывает текст имени папки, оставляя только иконку + бейдж непрочитанных. |
| `folderTabsNoOutline` | `folders_no_outline` | Убирает `stroke` у неактивных вкладок папок через `bg_folder_tab_no_outline`; выбранная вкладка сохраняет `bg_folder_tab_selected` с подсветкой. |
| `excludeFolderChatsFromAll` | `folders_exclude_from_all` | Чаты, входящие хотя бы в одну пользовательскую папку, не показываются во вкладке «Все чаты» и не учитываются в её бейдже. |
| `mainTabCallsVisible` | `main_tab_calls_visible` | Показывает/скрывает вкладку «Звонки»; по умолчанию `false`, поэтому вкладка звонков скрыта. «Чаты» и «Профиль» обязательны, в настройках показаны серыми и не отключаются. |
| `relativeOnlineTime` | `relative_online_time` | Форматирует last online через `OnlineTimeFormatter`: `был(а) 15 минут назад` или `был(а) в 6:15`. Применяется в `ChatActivity`, `UserProfileActivity`, `GroupInfoActivity`. |
| `chatStickerSizeDp` | `chat_sticker_size_dp` | Размер стикеров в чате, диапазон 96..240dp, по умолчанию 160dp. `PersonalizationSettingsActivity` показывает preview, `ChatActivity` передаёт значение в `MessageAdapter`. |

Фон чатов в блоке «Фон чатов» остаётся серверной персонализацией, но список в настройках теперь свернут до 3×3 ячеек (включая «Без фона») и раскрывается кнопкой, если элементов больше. Папочные флаги применяются в `ChatsFragment.renderFolderTabs()` / `applyFolderFilter()` / `computeAllChatsUnread()`. Вкладки главного экрана перечитываются в `MainActivity.onResume()`.

## Настройки → Аккаунт — поле «О себе»

`AccountSettingsActivity` в карточке полей профиля содержит `itemBio` под `itemUsername` (разделитель `MaterialDivider`). Текущее значение читается из `globalParam.description` и отображается в `textBio` (placeholder «Не указано» при пустой строке). По клику — `showEditDialog("О себе", …, allowEmpty = true)` → `grpcManager.changeBio(newValue)` (`GrpcManager.kt:1674`). При успехе значение сохраняется в `globalParam.description` (тот же бэкенд-поле, что наполняется из `getCurrentUserData().bio` в `SplashActivity`/`LoginActivity`/`RegisterActivity`).

## App Widget «Закреплённые чаты»

Нативный App Widget для рабочего стола Android: до 3 строк с аватаром, именем чата, текстом последнего сообщения и бейджем непрочитанных. Тап по строке открывает `ChatActivity` соответствующего чата, тап по заголовку — `MainActivity`. Кнопка refresh в углу триггерит немедленное обновление.

Пользователь может создавать **несколько** виджетов одновременно (каждый со своим именем и своей подборкой 1-3 чатов). Управление — через пункт **«Виджеты»** в `ProfileFragment` (`itemWidgets`, иконка `ic_widgets`, между блоками Персонализация и Папки чатов) → `WidgetsSettingsActivity`. Создание нового виджета — стандартный Android-флоу: long-press на рабочем столе → BarkFluff → «Закреплённые чаты», автоматически открывается `WidgetConfigureActivity`.

### Компоненты `com/barkfluff/client/widget/`

- `WidgetConfig.kt` — data class `{ name: String, chatIds: List<String> }`, константа `MAX_CHATS = 3`.
- `WidgetRepository.kt` — singleton поверх `SharedPreferences("barkfluff_widgets")`. Ключ `widget_<appWidgetId>` → JSON. Методы: `getConfig`, `saveConfig`, `deleteConfig`, `listAllConfigs`, `findAppWidgetIdsForChat(chatId)`, `placedAppWidgetIds(context)`.
- `WidgetRenderer.kt` — строит `RemoteViews` по конфигу + снимку `List<ChatData>`. Аватары грузит через `AvatarLoader.getImageLoader(context).execute()` → `Bitmap` → круглая маска через `BitmapShader` → `setImageViewBitmap`. При отсутствии fileId или ошибке — placeholder-Bitmap с цветным кругом и инициалами (палитра из `AvatarLoader.PLACEHOLDER_COLORS`). **Важно**: include одного и того же layout трижды внутри RemoteViews не работает (RemoteViews адресует view по id и находит только первое вхождение), поэтому строки собираются через `views.removeAllViews(R.id.widgetRowsContainer)` + `views.addView(..., rowViews)` с отдельным `RemoteViews(R.layout.widget_chat_row)` на каждую строку.
- `WidgetUpdater.kt` — единая точка обновления:
  - `refreshWidget(context, appWidgetId)` — synchronous suspend под mutex, ограничен бюджетом `REFRESH_BUDGET_MS = 8_000` (`withTimeout` **внутри** `withLock`, чтобы ожидание мьютекса не съедало бюджет).
  - `refreshAllWidgets(context)` — для всех размещённых.
  - `scheduleRefreshForChat(context, chatId)` — дебаунсит 500мс через `ConcurrentHashMap<Int, Job>`, ранний return если ни один виджет не содержит `chatId`. Используется из realtime-стримов.
  - In-memory кеш `getChats()` на 10 секунд (`CACHE_TTL_MS`) — чтобы шторм real-time событий не дёргал gRPC по разу на каждый виджет.
  - Если `messagesClient == null` (виджет работает в фоне без активного приложения) — переинициализирует через `grpcManager.createMessagesClient(globalParam.socketMessages, …)`.
  - Если `accessToken` пуст — виджет рендерится в режиме «Войдите в приложение».
- `PinnedChatsWidgetProvider.kt` — `AppWidgetProvider`. `onUpdate` → под одним `goAsync()` последовательно обходит все id, общий бюджет `ON_UPDATE_BUDGET_MS = 9_000` (виджеты обновляются под мьютексом, поэтому бюджета одного виджета на цикл не хватает). `onDeleted` → `WidgetRepository.deleteConfig` для каждого id. `onReceive` ловит кастомный `ACTION_REFRESH = "com.barkfluff.client.widget.ACTION_REFRESH"` (от кнопки refresh в виджете), тоже через `goAsync()`.
  - Двойного `goAsync()` не возникает: `super.onReceive` при `ACTION_APPWIDGET_UPDATE` вызывает `onUpdate` (один вызов), а кастомный `ACTION_REFRESH` `AppWidgetProvider` игнорирует.

### Бюджет времени и деградация рендера

Окно `goAsync()` для foreground-broadcast — ~10 с, а таймауты OkHttp в `AvatarLoader.getImageLoader` втрое больше (30 с на connect/read/write). Без ограничения сверху обновление виджета в это окно не укладывалось, а `onUpdate` вдобавок вообще не удерживал broadcast — процесс мог быть убит до отрисовки, и виджет молча оставался старым.

- Аватары грузятся **параллельно** (`coroutineScope` + `async`/`awaitAll` до сборки строк), а не цепочкой по одному.
- Каждая загрузка под `withTimeout(AVATAR_TIMEOUT_MS = 3_000)`, охватывающим и резолв ссылки через gRPC (`getFileDownloadUrl`), и сам `imageLoader.execute`. При таймауте — тот же placeholder с инициалами, что и при ошибке, поэтому `render` всегда возвращает готовые `RemoteViews`.
- `TimeoutCancellationException` логируется как `Log.w` (не ошибка), обычная `CancellationException` пробрасывается — иначе глоталась бы штатная отмена дебаунса из `scheduleRefreshForChat`.
- При исчерпании бюджета `refreshWidget` `updateAppWidget` не вызывается, и виджет остаётся с прежним содержимым — это лучше пустого.
- `WidgetRefreshWorker.kt` — `CoroutineWorker`. Periodic WorkManager job `widget-refresh` раз в 30 минут с `NetworkType.CONNECTED`. Регистрируется в `BarkFluffApplication.onCreate()` через `enqueueUniquePeriodicWork(... KEEP ...)`. Fallback на случай killed-app (когда `RealtimeService` не работает).
- `WidgetConfigureActivity.kt` — Activity с `ACTION_APPWIDGET_CONFIGURE` в манifest'е. Поля: `nameInput` (max 48 символов, дефолт «Закреплённые чаты»), `pickChatsButton` (переиспользует `FolderChatPickerActivity` с `EXTRA_INITIAL_SELECTED`, обрезает результат до 3 + Toast при переборе), `selectedChatsRecyclerView` (показывает выбранные + крестик «удалить»). По умолчанию `setResult(RESULT_CANCELED)` — Android удаляет widget если пользователь не дошёл до «Сохранить». При сохранении: `WidgetRepository.saveConfig` → `WidgetUpdater.refreshWidget` → `setResult(RESULT_OK, intentWithAppWidgetId)` + `finish()`. В edit-mode (флаг `EXTRA_EDIT_MODE=true` от `WidgetsSettingsActivity`) `setResult` пропускается.

### `WidgetsSettingsActivity.kt`

Экран «Виджеты» в настройках. `RecyclerView` со списком конфигов из `WidgetRepository.listAllConfigs()`. Тап по строке открывает `WidgetConfigureActivity` с `EXTRA_APPWIDGET_ID` + `EXTRA_EDIT_MODE=true`. На `onResume` чистит «висячие» конфиги — id, которых нет в `placedAppWidgetIds()` (на случай если `onDeleted` не успел сработать). Под списком — карточка-подсказка как добавить виджет с рабочего стола.

### Интеграция в `RealtimeService`

`WidgetUpdater.scheduleRefreshForChat(context, event.chatId)` вызывается из 4 collector'ов: `collectNewMessages`, `collectMessagesRead` (только когда читал сам пользователь), `collectMessagesEdited`, `collectMessagesDeleted`. Дебаунс 500мс внутри `WidgetUpdater` гасит штормы; если виджетов с этим chatId нет — мгновенный return без gRPC.

### Resources

- Layouts: `widget_pinned_chats.xml` (root), `widget_chat_row.xml` (одна строка). `activity_widget_configure.xml`, `activity_widgets_settings.xml`, `item_widget_config.xml`, `item_widget_selected_chat.xml`.
- Drawables: `widget_background.xml` (rounded rect r=24dp), `widget_unread_badge.xml` (rounded rect r=12dp), `widget_row_ripple.xml`, `ic_widgets.xml`, `ic_refresh.xml`.
- Widget metadata: `res/xml/pinned_chats_widget_info.xml` — `minWidth=250dp`, `minHeight=180dp`, `configure=...WidgetConfigureActivity`, `updatePeriodMillis="0"` (обновляемся через WorkManager + RealtimeService, без системного таймера).
- Цвета: `widget_background_color`, `widget_text_primary`, `widget_text_secondary`, `widget_accent_color`, `widget_accent_text` — все через Material You system tokens (`@android:color/system_neutral1_*`, `system_accent1_*`), вариант для dark в `values-night/colors.xml`. `?attr/colorPrimary` в RemoteViews не резолвится корректно — поэтому в widget layouts всегда `@color/widget_*`.
- Manifest: `<receiver .widget.PinnedChatsWidgetProvider>` с двумя action — `APPWIDGET_UPDATE` и `com.barkfluff.client.widget.ACTION_REFRESH`. `<activity .widget.WidgetConfigureActivity>` exported=true с `APPWIDGET_CONFIGURE`. `<activity .WidgetsSettingsActivity>` exported=false.
- Зависимость: `androidx.work:work-runtime-ktx:2.9.1` (WorkManager) добавлена в `app/build.gradle.kts`.

### Передача chatId в ChatActivity

`WidgetRenderer.openChatPendingIntent` использует `Intent(context, ChatActivity::class.java).putExtra("chat_id", chatId).putExtra("chat_title", title)` с `FLAG_ACTIVITY_NEW_TASK or FLAG_ACTIVITY_CLEAR_TOP`. requestCode = `appWidgetId * 10 + rowIndex + 1` — уникальный для каждой PendingIntent, иначе несколько строк / виджетов схлопывались бы в один Intent. Расширения ChatActivity: `EXTRA_CHAT_ID = "chat_id"` и `EXTRA_CHAT_TITLE = "chat_title"`.

## gRPC Коды ошибок (из x-error-code trailer)

| Исключение | ErrorCode |
|-----------|-----------|
| OtpCodeNeedException | `C1576884-12D8-4722-A7EE-9F9789AD1265` |
| NotValidOtpCodeException | `803B632C-4457-4B05-9435-9C3DD0F41E00` |
| InvalidLoginOrPasswordException | `21BFB9B5-C377-45D1-9B15-6B7F3432B397` |

## Системное «Поделиться» (Share Intent)

Клиент зарегистрирован как получатель `ACTION_SEND` / `ACTION_SEND_MULTIPLE`. В системном Share Sheet пользователь может выбрать Barkfluff и переслать в любой чат текст/ссылку, одно или несколько изображений/видео/аудио/файлов.

- **Activity**: `share/ShareReceiverActivity.kt` — экспортированная (`android:exported=true`) с `launchMode=singleTask`, `excludeFromRecents=true`, `taskAffinity=""`. Intent-filters в `AndroidManifest.xml` покрывают `text/*`, `image/*`, `video/*`, `audio/*`, `application/*`, `*/*`.
- **Авторизация**: при отсутствии `refreshToken` / `socketUsers` / `socketMessages` — Toast `share_not_authorized` и `finish()`. Pending-share-payload **не** сохраняется (по решению UX).
- **Парсинг**: `parseSend` / `parseSendMultiple` достают `EXTRA_STREAM` (один Uri или ArrayList) и `EXTRA_TEXT` / `EXTRA_SUBJECT`; для каждого Uri резолвится MIME через `contentResolver.getType`, делается `takePersistableUriPermission` под try/catch. Результат — `SharePayload` (`Text` / `SingleFile` / `MultipleFiles`).
- **UI выбора чата**: `activity_share_receiver.xml` — `CoordinatorLayout` (`fitsSystemWindows=true`, фон `colorSurfaceContainerLowest`) + `AppBarLayout` с `liftOnScroll` + `MaterialToolbar` (иконка `ic_close`, заголовок «Куда отправить?», подзаголовок «Через Barkfluff») + `RecyclerView` с переиспользованным `ChatAdapter`. Чаты грузятся через `grpcManager.getChats()` после `ensureTokenValid` + `initAllClients`. Display-title для ЛС резолвится через `getUserData` (как в `ChatsFragment.resolveDisplayItem`). Window insets применяются вручную через `ViewCompat.setOnApplyWindowInsetsListener`: top → padding AppBar, bottom → padding RecyclerView (под gesture-nav). Пустое состояние — круглая M3-карта с `ic_chat_bubble` (как в `fragment_chats.xml`). Индикатор — `CircularProgressIndicator`.
- **Подтверждение**: клик по чату открывает `share/ShareConfirmBottomSheet.kt` (`BottomSheetDialogFragment`) — M3-bottom-sheet с `BottomSheetDragHandleView`, заголовком `headlineSmall`, label-подзаголовком, превью контента, `TextInputLayout` OutlinedBox для подписи и pill-кнопкой `MaterialButton` (56dp, corner 28dp) с иконкой `ic_send`. Bottom-padding динамически учитывает IME и navigation-bar инсеты (`max(ime, nav)`) — кнопка поднимается над клавиатурой.
  - `SharePayload.Text` → текст в EditText (редактируется), без превью изображения, отправляется как `SendJob(text=..., attachments=[])`.
  - `SharePayload.SingleFile`: image → Coil `imageView.load(uri)` в `ShapeableImageView` (corner Large); video → `contentResolver.loadThumbnail(uri, Size(512,512))` (API 29+); прочее → filled `MaterialCardView` (`colorSurfaceContainerHigh`, corner 16dp) с filled-tonal плашкой иконки (`colorPrimaryContainer`, corner 14dp) + имя/размер (`OpenableColumns.DISPLAY_NAME` / `SIZE`).
  - `SharePayload.MultipleFiles` → горизонтальный `RecyclerView` миниатюр (`item_share_preview_thumb.xml`, 96×96dp, corner Medium).
- **Постановка в очередь**: MIME → `AttachmentSpec`: `image/*` → `RawImage(uri)`, `video/*` → `Video(EditedVideoSpec(uri))`, остальное → `Document(uri)`. Дальше — обычный `MediaSendService.enqueue(ctx, SendJob(...))`, и SendJob проходит существующий конвейер (compress/upload/sendMessage) без изменений.
- **После отправки**: Toast `share_sent_toast`, `dismissAllowingStateLoss()`, `activity.finish()` — задача live в foreground-сервисе и без открытого UI.
- **Payload через Activity**: `SharePayload` содержит `Uri`-списки и не парселится — bottom-sheet читает его через `(activity as ShareReceiverActivity).payload`.

Связанные файлы:
- `share/SharePayload.kt`
- `share/ShareReceiverActivity.kt`
- `share/ShareConfirmBottomSheet.kt`
- `res/layout/activity_share_receiver.xml`
- `res/layout/sheet_share_confirm.xml`
- `res/layout/item_share_preview_thumb.xml`
- `res/values/strings.xml` — ключи `share_*`

## Локализация

Per-app locales через `AppCompatDelegate.setApplicationLocales` (без `attachBaseContext`/`BaseActivity`).

- Поддержанные языки: **ru**, **en**, **de**, **es**, **zh-CN** + «Системный» (сбрасывает override).
- Ресурсы: `res/values/strings.xml` (ru, default) + `values-en/`, `values-de/`, `values-es/`, `values-zh-rCN/`.
- Юридические документы локализованы **не через strings.xml**, а отдельными markdown-файлами в `assets/legal/` — см. раздел про модалку согласия выше.
- `res/xml/locales_config.xml` перечисляет все 5 локалей; `AndroidManifest.xml` ссылается через `android:localeConfig="@xml/locales_config"`.
- `utils/LocaleManager.kt` — `apply(language)` маппит `GlobalParam.LANGUAGE_*` константы в `LocaleListCompat`; `"system"` → `getEmptyLocaleList()`.
- Хранение: `GlobalParam.appLanguage` (обычный `SharedPreferences`, ключ `app_language`, сохраняется при `clearUserData()` — это настройка устройства, не аккаунта).
- Применяется при старте: `BarkFluffApplication.onCreate()` вызывает `LocaleManager.apply(GlobalParam(this).appLanguage)`.
- UI: `LanguageSettingsActivity` (`activity_language_settings.xml`) — карточка с `RadioGroup` из 6 пунктов. Каждая строка содержит **флаг-эмодзи + нативное имя языка** (🌐 Системный, 🇷🇺 Русский, 🇬🇧 English, 🇩🇪 Deutsch, 🇪🇸 Español, 🇨🇳 中文). Открывается из `ProfileFragment` → пункт «Язык».
- При смене языка AppCompat сам пересоздаёт Activity-стек через `recreate()`.

### Правила локализации и доступности UI

- Пользовательский текст нельзя оставлять литералом в Kotlin или XML. Для экранов, Toast/Snackbar, диалогов, валидации, уведомлений, notification channels и accessibility labels используются ресурсы `strings.xml`.
- Любой translatable-ключ синхронно добавляется во все пять наборов: `values` (ru), `values-en`, `values-de`, `values-es`, `values-zh-rCN`. Форматные placeholders и `plurals` должны сохранять смысл и placeholder-ы; для русского не заменять `plurals` одной строкой.
- Серверные ошибки, имена пользователей/чатов, содержимое сообщений, URL, ID и имена файлов не переводятся. Статическая UI-обёртка ошибки локализуется отдельно, а серверный текст передаётся как placeholder.
- Проверка ресурсов, placeholders, `plurals`, hardcoded XML-атрибутов и Kotlin UI-контекстов запускается из `Android`:

  `python tools/check_android_ui.py`

- Для TalkBack интерактивные действия получают смысловые `cd_*`-ресурсы во всех локалях. Динамические состояния (play/pause, mute/unmute, камера, микрофон, screen share, pin/unpin, select/deselect) обновляют label после изменения состояния.
- Декоративные изображения, фоновые элементы, иконки рядом с уже озвучиваемым текстом и аватары рядом с именем используют `android:contentDescription="@null"` и при необходимости `importantForAccessibility="no"`. Описание не добавляется автоматически каждому `ImageView`.
- Самостоятельные изображения и custom views озвучивают содержательное состояние один раз; `CallTileView` сообщает имя участника и существенные состояния, а дочерние декоративные части исключены из фокуса. Интерактивные области — не менее 48dp.
- Notification channel IDs не меняются. После смены языка каналы регистрируются повторно с локализованными названием и описанием, чтобы Android обновил отображаемые подписи.

## Топология сборки и модуль `:core`

Единый Gradle-рут — `Android/`:

```
Android/
  settings.gradle.kts        # include(:core, :app-v1)
  build.gradle.kts           # плагины apply false
  gradle/libs.versions.toml  # единый каталог версий
  core/                      # общий не-UI слой + proto
  Barkfluff.Client.Android/app → :app-v1  (Views/XML)
```

- **Тулчейн:** AGP 8.9.1, Gradle 9.2.1, Kotlin 2.2.20, Java 17.
- **Сборка только с JDK 17+:** `JAVA_HOME` = JBR Android Studio (JDK 21). Из `Android/`:
  `./gradlew :core:assembleDebug :app-v1:assembleDebug`

### Состав `:core`

`com.android.library`, namespace `com.barkfluff.client.core`, minSdk 31. Пакеты сохранили имена `com.barkfluff.client.*` (V1-код не правит импорты). Содержит: `grpc/` (GrpcManager, AuthInterceptor, DeviceInfoInterceptor, RealtimeService), `data/` (GlobalParam, ClientColors, ServerDataElement, OpenChatManager), `repository/` (Chat/Private/Secret), `crypto/` (BarkFluffSignalStore, PrekeyManager, PrivateChatCrypto), чистые `utils/` (FileCache, ImageCompressor, FileUrlCache, ImageCache, NetworkUtils, AudioPlayerHelper, FileSaveUtils, AppVersionUtil), `proto/` (protobuf-плагин, режим lite). `api(libsignal-android)`, `consumer-rules.pro` с keep-правилами.

**Развязка границы:** `RealtimeService` не зависит от UI/Notification/Widget — введён интерфейс `RealtimeSideEffects` (onChatChanged / dismissChatNotifications / showMessageNotification). Реализация `RealtimeSideEffectsImpl` живёт в app-слое (пакет `notifications/`, грузит уведомления через NotificationHelper + AvatarLoader/Coil).

**Осталось в app (НЕ в core):** `EncryptedInviteHandler`, `E2EBootstrap`, `StickerCache`, View-coupled utils (AvatarLoader, LocaleManager, SpringPress, ImageLoadHelper, FirebaseTokenHelper, LogoutHelper, UpdateChecker) — зависят от Activity/Application.

## Файловая структура

- `gradle/libs.versions.toml` — все версии зависимостей
- `core/src/main/proto/` — 13 proto файлов
- `app/src/main/java/com/barkfluff/client/` — все исходники
- Полная карта проекта — в Obsidian: [[Android-ProjectMap]] + [[Android-FileIndex]] (отдельного `PROJECT_MAP.md` в репозитории нет)

## Per-chat mute (отключение уведомлений чата)

- `ChatActivity` — пункт меню «три точки» (`btnMore` → `showChatMenu`) переключает mute через `GrpcManager.setChatMuted(chatId, muted, until?)`. Состояние читается из `GetChatInfo.muted`.
- `GrpcManager`: `setChatMuted()`, `getMutedChats()` (Set<chatId>).
- `ChatRepository.ChatInfo.muted` — маппится из proto `GetChatInfoResponse.muted`.
- `GlobalParam.mutedChatIds` (StringSet) + `setChatMutedLocal()` — локальный кэш; `BarkFluffFirebaseMessagingService.onMessageReceived` пропускает уведомление, если `chatId` в кэше (guard от гонок кэша токенов; сервер и так подавляет push).
- Строки: `chat_menu_mute/unmute`, `chat_muted/unmuted`, `chat_mute_error` (все 5 локалей). Серверная часть — [[Backend/Users]] → Per-chat mute.

## Offline-first кеш чатов (V1)

- \`:app-v1\` хранит список чатов, папки, отображаемые данные личных чатов и всю просмотренную историю в зашифрованной Room/SQLCipher БД. Ключ создаётся случайно и хранится в \`EncryptedSharedPreferences\`; scope включает Beacon-сервер и ID пользователя.
- \`ChatsFragment\` сначала читает локальный снимок. При его отсутствии показывает 7 skeleton-строк; затем обновляет до трёх серверных страниц и папки. «Обновление…», offline-подсказка и «Соединение…» сменяют имя в одной строке шапки с короткой fade/slide-анимацией; повтор синхронизации остаётся кнопкой рядом. «Соединение…» показывается только при переподключении основного realtime-стрима новых сообщений, а не при первичном подключении или ошибке вспомогательного стрима.
- \`ChatActivity\` немедленно показывает последние 30 кешированных сообщений, а затем обновляет серверную страницу только для открытого чата. Страницы пагинации и события realtime (new/read/edit/delete) сохраняются обратно в кеш.
- `ChatDraftRepository` хранит в той же зашифрованной БД scoped-журнал обычных чатов: текст, `replyToMessageId`, server revision, локальное поколение и sync-state. Изменение фиксируется локально сразу, upsert отправляется через 2 секунды бездействия и при уходе с `ChatActivity`; недоставленные upsert/delete повторяются при старте/возврате приложения и восстановлении сети. Tombstone удаляет только известную revision, поэтому поздний ответ или другой клиент не стирает новую правку.
- При открытии обычного чата несинхронизированный локальный черновик имеет приоритет, иначе запрашивается `GetChatDraft`. Reply восстанавливается из кеша или загружается по ID; у удалённого сообщения остаётся текст без reply. V1 намеренно не сохраняет файлы, upload-очередь, attachment-диалог, edit-режим, private- и secret-чаты. После успешной отправки удаляется только generation отправленного текста/reply.
- Настройки хранилища показывают серверные категории и две локальные величины: Coil/bitmap изображения и encrypted Room-кеш чатов с количеством чатов/сообщений. «Очистить кеш» удаляет оба отображаемых источника, включая БД и её ключ. \`LogoutHelper\` также очищает кеш, поэтому данные другого аккаунта не отображаются.
## Логирование и приватность (V1)

- В release-сборке `Log.v/d/i/w/println` полностью вырезаются R8 через `-assumenosideeffects` в `Barkfluff.Client.Android/app/proguard-rules.pro`. Вместе с вызовом устраняется и конкатенация аргументов — строковые константы не попадают в dex (проверяется поиском по `classes*.dex`).
- `Log.e` намеренно оставлен для диагностики прод-крашей. Поэтому **в аргументах `Log.e` не должно быть PII**: текста сообщений, `content://` URI, presigned-URL, FCM-токенов, поисковых запросов. Логировать вместо этого ID (`fileId`, `chatId`, `messageId`), длины (`textLength`) и флаги наличия (`hasUrl`).
- Правило объявлено в proguard-rules.pro у `:app-v1`. `-assumenosideeffects` действует на весь merged-DEX приложения, поэтому код `:core` при сборке V1 покрывается автоматически.
- Никогда не логировать proto-сообщения целиком (`$response`, `$user`): `toString()` у protobuf-lite печатает все поля, включая username, bio и URL.

## Сборка

```bash
cd Android
./gradlew :app-v1:assembleDebug
```

### Тестовая сборка на macOS

На macOS клиент V1 собирается из корневого Android Gradle-проекта. Использовать JBR из Android Studio и локальный Android SDK:

```bash
cd /Users/liis/Projects/BarkFluff/Android
JAVA_HOME="/Applications/Android Studio.app/Contents/jbr/Contents/Home" \
ANDROID_HOME="/Users/liis/Library/Android/sdk" \
sh ./gradlew :app-v1:assembleDebug
```
