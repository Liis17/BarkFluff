using Barkfluff.Developers.Domain;

namespace Barkfluff.Developers.Infrastructure;

public static class SeedData
{
    public static List<DocumentationSection> GetSeedSections()
    {
        return
        [
            new()
            {
                Id = Guid.NewGuid(), Key = "overview", Title = "Обзор", Type = "overview", Order = 1,
                Content = """
                {
                  "hero": {
                    "eyebrow": "Developer Portal",
                    "title": "Создай свой",
                    "titleAccent": "BarkFluff клиент",
                    "subtitle": "Открытые gRPC-контракты, описание протоколов и примеры интеграции — всё необходимое для разработки нативного клиента на любом языке программирования.",
                    "pills": ["gRPC / proto3", "TLS / XAuth", "Real-time streaming", "10 микросервисов"]
                  },
                  "cards": [
                    {"title":"gRPC + TLS","description":"Все сервисы работают по gRPC (HTTP/2). Шифрование на уровне соединения, поддержка streaming.","icon":"shield"},
                    {"title":"XAuth","description":"JWT-токены в заголовке x-auth-token. Обязательные metadata-заголовки для идентификации клиента.","icon":"auth"},
                    {"title":"Real-time","description":"Серверный стриминг новых сообщений, read-receipts и статусов онлайн через UpdatesApi и OnlinerApi.","icon":"bolt"},
                    {"title":"Вложения","description":"Изображения, видео, аудио, документы, голосовые и стикеры. Дедупликация по SHA-256 хешу.","icon":"file"},
                    {"title":"Мультиплатформа","description":"Один backend — любой клиент: WPF, Android (Kotlin), iOS (Swift), веб, Linux (C++).","icon":"devices"},
                    {"title":"Navigator","description":"Публичный реестр серверов. Клиент находит сервер через Navigator → Beacon, без хардкода адресов.","icon":"globe"}
                  ]
                }
                """
            },
            new()
            {
                Id = Guid.NewGuid(), Key = "quickstart", Title = "Быстрый старт", Type = "quickstart", Order = 2,
                Content = """
                {
                  "eyebrow": "Руководство",
                  "lead": "Минимальный путь от нуля до отправки первого сообщения. Всего 5 шагов — Navigator, Beacon, регистрация, авторизация, запросы.",
                  "code": {
                    "language": "Псевдокод · Полный поток подключения",
                    "lines": [
                      {"text":"// 1. Получить список серверов (публичный endpoint без авторизации)","type":"comment"},
                      {"text":"metadata = { \"x-device-id\": uuid, \"x-device-name\": \"MyDevice\",","type":"code"},
                      {"text":"             \"x-ip\": \"1.2.3.4\", \"x-os\": \"Android 14\",","type":"code"},
                      {"text":"             \"x-app-name\": \"MyClient\", \"x-app-version\": \"1.0.0\" }","type":"code"},
                      {"text":"servers = NavigatorApi.ListServers({}, metadata)","type":"fn"},
                      {"text":"server  = servers[0]  // выбрать нужный","type":"code"},
                      {"text":"","type":"empty"},
                      {"text":"// 2. Получить адреса микросервисов (Beacon)","type":"comment"},
                      {"text":"info = BeaconApi.GetServerInfo({}, metadata)","type":"fn"},
                      {"text":"//   info.identity.endpoint → host:port для IdentityApi","type":"comment"},
                      {"text":"//   info.users / files / messages / updates / onliner","type":"comment"},
                      {"text":"","type":"empty"},
                      {"text":"// 3. Создать аккаунт (если нет)","type":"comment"},
                      {"text":"resp = IdentityApi.CreateAccount({ first_name, last_name, username, email }, metadata)","type":"fn"},
                      {"text":"IdentityApi.ConfirmAccount({ code_id: resp.code_id, code_value: emailCode }, metadata)","type":"fn"},
                      {"text":"","type":"empty"},
                      {"text":"// 4. Авторизоваться","type":"comment"},
                      {"text":"auth = IdentityApi.Auth({ username: \"user\", password: \"pass\" }, metadata)","type":"fn"},
                      {"text":"//   Если 2FA включена → ошибка x-error-code C1576884... → повторить с otp_code","type":"comment"},
                      {"text":"accessToken  = auth.access_token.value","type":"fn"},
                      {"text":"refreshToken = auth.refresh_token.value","type":"fn"},
                      {"text":"","type":"empty"},
                      {"text":"// 5. Использовать API (добавить x-auth-token)","type":"comment"},
                      {"text":"metadata[\"x-auth-token\"] = accessToken","type":"fn"},
                      {"text":"chats = MessagesApi.ListChats({ pagination: { offset: 0, size: 50 } }, metadata)","type":"fn"},
                      {"text":"","type":"empty"},
                      {"text":"// 6. Обновить токен когда истечёт (access_token.expiration_date)","type":"comment"},
                      {"text":"newToken = IdentityApi.CreateToken({ refresh_token: refreshToken }, metadata)","type":"fn"}
                    ]
                  },
                  "infoBox": "Все metadata-заголовки (x-device-id, x-app-name и др.) обязательны для каждого gRPC-запроса, включая публичные (Navigator, Beacon). x-device-id должен сохраняться между сессиями — он идентифицирует устройство в системе сессий."
                }
                """
            },
            new()
            {
                Id = Guid.NewGuid(), Key = "implementation", Title = "Реализация клиента", Type = "implementation", Order = 3,
                Content = """
                {
                  "eyebrow": "Руководство",
                  "lead": "Ключевые паттерны из официальных клиентов — WPF (C#) и Android (Kotlin). Адаптируй под свой язык.",
                  "cards": [
                    {
                      "title": "Инициализация каналов",
                      "subtitle": "Navigator → Beacon → создать gRPC каналы",
                      "icon": "channels",
                      "code": [
                        {"text":"// Шаг 1: Navigator (plaintext, порт 443, TLS)","type":"comment"},
                        {"text":"navigatorChannel = grpc.dial(\"navigator.barkfluff.com:443\", tls=true)","type":"fn"},
                        {"text":"servers = NavigatorApi.ListServers({})","type":"fn"},
                        {"text":"","type":"empty"},
                        {"text":"// Шаг 2: Beacon нужного сервера","type":"comment"},
                        {"text":"beaconChannel = grpc.dial(server.beacon_uri, tls=server.tls_enabled)","type":"fn"},
                        {"text":"info = BeaconApi.GetServerInfo({})","type":"fn"},
                        {"text":"","type":"empty"},
                        {"text":"// Шаг 3: Все каналы","type":"comment"},
                        {"text":"identityChannel = grpc.dial(info.identity.endpoint, ...)","type":"fn"},
                        {"text":"usersChannel    = grpc.dial(info.users.endpoint, ...)","type":"fn"},
                        {"text":"messagesChannel = grpc.dial(info.messages.endpoint, ...)","type":"fn"}
                      ]
                    },
                    {
                      "title": "Управление токенами",
                      "subtitle": "SafeCall с авторетраем при 401",
                      "icon": "tokens",
                      "code": [
                        {"text":"// Перед каждым запросом — проверить срок токена","type":"comment"},
                        {"text":"func safeCall(fn):","type":"kw"},
                        {"text":"  try:","type":"kw"},
                        {"text":"    if isExpired(accessToken):","type":"kw"},
                        {"text":"      accessToken = refreshAccessToken()","type":"fn"},
                        {"text":"    return fn()","type":"fn"},
                        {"text":"  catch Unauthenticated:","type":"kw"},
                        {"text":"    accessToken = refreshAccessToken()","type":"fn"},
                        {"text":"    return fn()  // одна повторная попытка","type":"fn"},
                        {"text":"","type":"empty"},
                        {"text":"func refreshAccessToken():","type":"kw"},
                        {"text":"  resp = IdentityApi.CreateToken({ refresh_token })","type":"fn"},
                        {"text":"  return resp.access_token.value","type":"fn"}
                      ]
                    },
                    {
                      "title": "Real-time стриминг",
                      "subtitle": "Новые сообщения + read-receipts + онлайн",
                      "icon": "streaming",
                      "code": [
                        {"text":"// Подписка на новые сообщения","type":"comment"},
                        {"text":"stream = UpdatesApi.SubscribeNewMessages({})","type":"fn"},
                        {"text":"for event in stream:","type":"kw"},
                        {"text":"  showNotification(event)","type":"fn"},
                        {"text":"","type":"empty"},
                        {"text":"// Read-receipts (new_read_by = ПОЛНЫЙ список)","type":"comment"},
                        {"text":"stream = UpdatesApi.SubscribeMessagesRead({})","type":"fn"},
                        {"text":"","type":"empty"},
                        {"text":"// Онлайн-статусы","type":"comment"},
                        {"text":"OnlinerApi.ChangeUsersInSubscription({ user_ids })","type":"fn"},
                        {"text":"stream = OnlinerApi.SubscribeToOnlineStatus({ user_ids })","type":"fn"}
                      ]
                    },
                    {
                      "title": "Загрузка файлов",
                      "subtitle": "Дедупликация → получить URL → POST",
                      "icon": "upload",
                      "code": [
                        {"text":"// 1. Дедупликация по SHA-256","type":"comment"},
                        {"text":"hash = sha256(fileBytes)","type":"fn"},
                        {"text":"check = FilesApi.CheckFileHash({ file_hash: hash })","type":"fn"},
                        {"text":"if check.file_id != \"\":","type":"kw"},
                        {"text":"  return check.file_id","type":"fn"},
                        {"text":"","type":"empty"},
                        {"text":"// 2. Получить URL для загрузки","type":"comment"},
                        {"text":"upload = FilesApi.GetUploadUrl({ file_type: IMAGE })","type":"fn"},
                        {"text":"","type":"empty"},
                        {"text":"// 3. HTTP POST на upload.url","type":"comment"},
                        {"text":"http.post(upload.url, multipart({ file: bytes }))","type":"fn"}
                      ]
                    }
                  ]
                }
                """
            },
            new()
            {
                Id = Guid.NewGuid(), Key = "auth-headers", Title = "XAuth заголовки", Type = "auth-headers", Order = 4,
                Content = """
                {
                  "eyebrow": "Аутентификация",
                  "lead": "Каждый gRPC-запрос должен содержать обязательные metadata-заголовки. Защищённые методы дополнительно требуют x-auth-token.",
                  "encodingNote": "Все заголовки устройства передаются как Base64(UTF-8, NO_WRAP). Токен авторизации x-auth-token — единственный plain-string заголовок.",
                  "deviceHeaders": [
                    {"name":"x-device-id","format":"Base64","description":"UUID v4 устройства. Генерируется один раз при установке, сохраняется между сессиями. Идентифицирует устройство в системе сессий.","example":"550e8400-e29b-41d4-a716-446655440000"},
                    {"name":"x-device-name","format":"Base64","description":"Читаемое имя устройства (модель, hostname).","example":"Samsung Galaxy S24"},
                    {"name":"x-ip-address","format":"Base64","description":"Внешний IP-адрес клиента. Используется для геолокации сессий.","example":"192.168.1.10"},
                    {"name":"x-os-name","format":"Base64","description":"Операционная система клиента (название + версия).","example":"Android 14"},
                    {"name":"x-app-name","format":"Base64","description":"Название вашего приложения.","example":"MyBarkFluffClient"},
                    {"name":"x-app-version","format":"Base64","description":"Версия приложения (semver).","example":"1.0.0"}
                  ],
                  "authHeader": {
                    "name": "x-auth-token",
                    "format": "plain string",
                    "description": "Access-токен пользователя. Передаётся без Base64 и без префикса Bearer. Получается через Auth или CreateToken. Истекает — смотри access_token.expiration_date."
                  },
                  "kotlinExample": [
                    {"text":"fun toBase64(value: String): String =","type":"kw"},
                    {"text":"    Base64.encodeToString(value.toByteArray(Charsets.UTF_8), Base64.NO_WRAP)","type":"fn"},
                    {"text":"","type":"empty"},
                    {"text":"// DeviceInfoInterceptor.start()","type":"comment"},
                    {"text":"headers.put(key(\"x-device-id\"),    toBase64(deviceId))","type":"fn"},
                    {"text":"headers.put(key(\"x-device-name\"),  toBase64(getDeviceName()))","type":"fn"},
                    {"text":"headers.put(key(\"x-auth-token\"), accessToken)","type":"fn"}
                  ],
                  "serverApiNote": "Сервисы *ServerApi предназначены для межсервисного взаимодействия внутри инфраструктуры. Они недоступны с пользовательским токеном — только с сервисным JWT."
                }
                """
            },
            new()
            {
                Id = Guid.NewGuid(), Key = "connection-flow", Title = "Поток подключения", Type = "connection-flow", Order = 5,
                Content = """
                {
                  "eyebrow": "Аутентификация",
                  "lead": "Стандартный сценарий первого запуска клиента от обнаружения сервера до полноценной работы.",
                  "steps": [
                    {"title":"Найти сервер — NavigatorApi","description":"Вызови NavigatorApi.ListServers({}) по публичному адресу navigator.barkfluff.com:443 (gRPC + TLS). Получи список серверов с beacon_uri каждого. Позволь пользователю выбрать сервер или используй первый из списка."},
                    {"title":"Получить адреса микросервисов — BeaconApi","description":"Подключись к beacon_uri выбранного сервера и вызови BeaconApi.GetServerInfo({}). Ответ содержит адреса (host:port) и флаги tls_enabled для каждого из 6 микросервисов: identity, users, files, messages, updates, onliner."},
                    {"title":"Зарегистрировать аккаунт — IdentityApi (опционально)","description":"Если у пользователя нет аккаунта: CreateAccount(first_name, last_name, username, email) → получить code_id → ConfirmAccount(code_id, emailCode) → получить refresh_token. Немедленно перейди к шагу 5 для получения access-токена."},
                    {"title":"Авторизоваться — IdentityApi","description":"Вызови Auth({ username или email, password }). При включённой 2FA запрос завершится с кодом ошибки C1576884-12D8-4722-A7EE-9F9789AD1265 — повтори с otp_code. Сохрани access_token.value и refresh_token.value."},
                    {"title":"Обновлять access-токен — IdentityApi","description":"Когда истечёт access_token.expiration_date — вызови CreateToken({ refresh_token }) для получения нового access-токена. Обновляй заголовок x-auth-token во всех последующих запросах."}
                  ]
                }
                """
            },
            new()
            {
                Id = Guid.NewGuid(), Key = "error-codes", Title = "Коды ошибок", Type = "error-codes", Order = 6,
                Content = """
                {
                  "eyebrow": "Аутентификация",
                  "lead": "Бизнес-ошибки возвращаются через gRPC trailer-заголовок x-error-code. Стандартные gRPC status codes используются для технических ошибок.",
                  "trailerNote": "Код читается из trailer-метаданных ответа. В grpc-okhttp (Android): перехватывай StatusRuntimeException и читай trailers.get(Metadata.Key.of(\"x-error-code\", ASCII_STRING_MARSHALLER))."
                }
                """
            }
        ];
    }

    public static List<ProtoMetadata> GetSeedProtoMetadata()
    {
        return
        [
            new() { Id = Guid.NewGuid(), FileName = "shared.proto", DisplayName = "Общие типы", Slug = "proto-shared", Order = 1, RpcDescriptions = """
                {"description":"Общие типы данных, используемые во всех API сервисах","subsections":[
                  {"title":"Message — структура сообщения","type":"fields","items":[
                    {"name":"id","type":"int64","description":"Уникальный идентификатор сообщения"},
                    {"name":"sender_id","type":"int64","description":"ID пользователя-отправителя"},
                    {"name":"read_by","type":"int64[]","description":"Список ID пользователей, прочитавших сообщение"},
                    {"name":"sent_at","type":"Timestamp","description":"Дата и время отправки (UTC)"},
                    {"name":"content","type":"MessageContent","description":"Содержимое: текст + вложения"},
                    {"name":"type","type":"MessageContentType","description":"GENERIC — обычное, SYSTEM — системное"}
                  ]},
                  {"title":"MessageAttachment — вложение","type":"fields","items":[
                    {"name":"id","type":"int64","description":"ID вложения"},
                    {"name":"type","type":"MessageAttachmentType","description":"Тип файла (IMAGE, VIDEO, DOCUMENT, AUDIO и др.)"},
                    {"name":"file_id","type":"string","description":"ID файла — передай в FilesApi.GetTempDownloadUrl"},
                    {"name":"preview_url","type":"string","description":"Прямой URL превью (может быть пустым)"},
                    {"name":"attachment_size","type":"int64","description":"Размер файла в байтах"},
                    {"name":"preview_file_id","type":"string","description":"ID файла превью"},
                    {"name":"file_name","type":"string","description":"Оригинальное имя файла"}
                  ]},
                  {"title":"MessageAttachmentType","type":"enum","items":[
                    {"name":"MESSAGE_ATTACHMENT_TYPE_UNKNOWN","num":"0","description":"Неизвестный тип"},
                    {"name":"IMAGE","num":"1","description":"Изображение (JPEG, PNG, WebP и др.)"},
                    {"name":"VIDEO","num":"2","description":"Видеофайл"},
                    {"name":"GIF","num":"3","description":"Анимированная гифка"},
                    {"name":"DOCUMENT","num":"4","description":"Документ (PDF, ZIP, DOCX и др.)"},
                    {"name":"AUDIO","num":"5","description":"Аудиофайл"},
                    {"name":"VOICE","num":"6","description":"Голосовое сообщение"},
                    {"name":"STICKER","num":"7","description":"Стикер в формате WebP"}
                  ]},
                  {"title":"PageRequest — пагинация","type":"fields","items":[
                    {"name":"offset","type":"int32","description":"Количество элементов для пропуска (с начала)"},
                    {"name":"size","type":"int32","description":"Размер страницы. Максимум зависит от метода — обычно 50"}
                  ]}
                ]}
                """ },
            new() { Id = Guid.NewGuid(), FileName = "beacon_api.proto", DisplayName = "Beacon — обнаружение сервисов", Slug = "proto-beacon", Order = 2, RpcDescriptions = """
                {"description":"Получить адреса всех микросервисов выбранного сервера","info":"Beacon — первый вызов после выбора сервера. Адрес Beacon берётся из поля beacon_uri ответа NavigatorApi. Вызывать без авторизации.",
                "rpcs":[{"name":"GetServerInfo","req":"GetServerInfoRequest","resp":"GetServerInfoResponse","stream":false,"description":"Получить имя, описание, цвета сервера и адреса всех 6 микросервисов"}],
                "subsections":[
                  {"title":"GetServerInfoResponse","type":"fields","items":[
                    {"name":"name","type":"string","description":"Отображаемое имя сервера"},
                    {"name":"description","type":"string","description":"Описание сервера"},
                    {"name":"color","type":"ServerColor","description":"Цветовая схема сервера (light/main/dark hex)"},
                    {"name":"identity","type":"Service","description":"Адрес IdentityApi"},
                    {"name":"users","type":"Service","description":"Адрес UsersApi"},
                    {"name":"files","type":"Service","description":"Адрес FilesApi"},
                    {"name":"messages","type":"Service","description":"Адрес MessagesApi"},
                    {"name":"updates","type":"Service","description":"Адрес UpdatesApi (streaming)"},
                    {"name":"onliner","type":"Service","description":"Адрес OnlinerApi"}
                  ]},
                  {"title":"Service","type":"fields","items":[
                    {"name":"name","type":"string","description":"Название микросервиса"},
                    {"name":"endpoint","type":"ServiceEndpoint","description":"Хост и порт: { host, port }"},
                    {"name":"tls_enabled","type":"bool","description":"Использовать TLS при подключении"},
                    {"name":"status","type":"ServiceStatus","description":"Текущее состояние сервиса"}
                  ]},
                  {"title":"ServiceStatus","type":"enum","items":[
                    {"name":"Unknown","num":"0","description":"Статус неизвестен"},
                    {"name":"Healthy","num":"1","description":"Сервис работает нормально"},
                    {"name":"Degraded","num":"2","description":"Сервис работает с отклонениями"},
                    {"name":"Unhealthy","num":"3","description":"Сервис нездоров — возможны ошибки"},
                    {"name":"Offline","num":"4","description":"Сервис недоступен"}
                  ]}
                ]}
                """ },
            new() { Id = Guid.NewGuid(), FileName = "identity_api.proto", DisplayName = "Identity — авторизация", Slug = "proto-identity", Order = 3, RpcDescriptions = """
                {"description":"Регистрация, аутентификация, 2FA, управление сессиями и паролем",
                "rpcs":[
                  {"name":"Auth","req":"AuthRequest","resp":"AuthResponse","stream":false,"description":"Авторизация по логину/email + пароль. При 2FA вернёт ошибку OtpCodeNeeded — повтори с otp_code"},
                  {"name":"FastAuth","req":"FastAuthRequest","resp":"AuthResponse","stream":false,"description":"Быстрый вход по fast_auth_id (QR-авторизация)"},
                  {"name":"CreateToken","req":"CreateTokenRequest","resp":"CreateTokenResponse","stream":false,"description":"Обменять refresh_token на новый access_token"},
                  {"name":"CreateAccount","req":"CreateAccountRequest","resp":"CreateAccountResponse","stream":false,"description":"Создать черновик аккаунта. Вернёт code_id для подтверждения email"},
                  {"name":"ConfirmAccount","req":"ConfirmAccountRequest","resp":"ConfirmAccountResponse","stream":false,"description":"Подтвердить email кодом. Возвращает refresh_token"},
                  {"name":"GetActiveSessions","req":"GetActiveSessionsRequest","resp":"GetActiveSessionsResponse","stream":false,"description":"Список активных сессий (устройств) текущего пользователя"},
                  {"name":"RemoveActiveSession","req":"RemoveActiveSessionRequest","resp":"RemoveActiveSessionResponse","stream":false,"description":"Завершить сессию по device_id"},
                  {"name":"EnableOtpVerification","req":"EnableOtpVerificationRequest","resp":"EnableOtpVerificationResponse","stream":false,"description":"Включить 2FA. Возвращает QR-код и код для ручного ввода"},
                  {"name":"ConfirmOtpVerification","req":"ConfirmOtpVerificationRequest","resp":"ConfirmOtpVerificationResponse","stream":false,"description":"Подтвердить включение 2FA кодом из приложения"},
                  {"name":"DisableOtpVerification","req":"DisableOtpVerificationRequest","resp":"DisableOtpVerificationResponse","stream":false,"description":"Отключить метод 2FA"},
                  {"name":"ListOtpVerification","req":"ListOtpVerificationRequest","resp":"ListOtpVerificationResponse","stream":false,"description":"Статус 2FA: authenticator_enabled, email_enabled"},
                  {"name":"ResetPassword","req":"ResetPasswordRequest","resp":"ResetPasswordResponse","stream":false,"description":"Запросить сброс пароля. Вернёт reset_id"},
                  {"name":"ConfirmResetPassword","req":"ConfirmResetPasswordRequest","resp":"ConfirmResetPasswordResponse","stream":false,"description":"Подтвердить сброс кодом. Вернёт токены"},
                  {"name":"SetPassword","req":"SetPasswordRequest","resp":"SetPasswordResponse","stream":false,"description":"Установить/сменить пароль"}
                ],
                "subsections":[
                  {"title":"AuthRequest","type":"fields","items":[
                    {"name":"login (oneof)","type":"","description":"Передать одно из двух: username или email — не оба одновременно"},
                    {"name":"  username","type":"string","description":"Имя пользователя"},
                    {"name":"  email","type":"string","description":"Электронная почта"},
                    {"name":"password","type":"string","description":"Пароль"},
                    {"name":"otp_code","type":"string","description":"Код 2FA (передавать только при получении OtpCodeNeedException)"}
                  ]},
                  {"title":"Token","type":"fields","items":[
                    {"name":"value","type":"string","description":"Строковое значение JWT токена"},
                    {"name":"expiration_date","type":"Timestamp","description":"Дата и время истечения. Проверяй перед каждым запросом"}
                  ]},
                  {"title":"OtpTypeId","type":"enum","items":[
                    {"name":"Unknown","num":"0","description":"Неизвестный тип"},
                    {"name":"Authenticator","num":"1","description":"Google Authenticator и совместимые (TOTP)"},
                    {"name":"Email","num":"2","description":"Код на email пользователя"}
                  ]}
                ]}
                """ },
            new() { Id = Guid.NewGuid(), FileName = "users_api.proto", DisplayName = "Users — профили пользователей", Slug = "proto-users", Order = 4, RpcDescriptions = """
                {"description":"Профили, поиск, устройства, бейджи и настройки приватности",
                "rpcs":[
                  {"name":"GetUser","req":"GetUserRequest","resp":"GetUserResponse","stream":false,"description":"Получить профиль. user_id=0 — текущий пользователь"},
                  {"name":"SetProfilePicture","req":"SetProfilePictureRequest","resp":"SetProfilePictureResponse","stream":false,"description":"Установить аватар. file_id — ID файла через FilesApi (тип USER_AVATAR)"},
                  {"name":"CheckExistUsername","req":"CheckExistUsernameRequest","resp":"CheckExistResponse","stream":false,"description":"Проверить занятость username"},
                  {"name":"CheckExistEmail","req":"CheckExistEmailRequest","resp":"CheckExistResponse","stream":false,"description":"Проверить существование email"},
                  {"name":"ChangeName","req":"ChangeNameRequest","resp":"ChangeNameResponse","stream":false,"description":"Изменить имя и/или фамилию"},
                  {"name":"ChangeUsername","req":"ChangeUsernameRequest","resp":"ChangeUsernameResponse","stream":false,"description":"Изменить юзернейм"},
                  {"name":"ChangeBio","req":"ChangeBioRequest","resp":"ChangeBioResponse","stream":false,"description":"Изменить описание профиля"},
                  {"name":"SearchUsers","req":"SearchUsersRequest","resp":"SearchUsersResponse","stream":false,"description":"Поиск по username/имени/фамилии. Максимум 50 результатов"},
                  {"name":"GetUserBadges","req":"GetUserBadgesRequest","resp":"GetUserBadgesResponse","stream":false,"description":"Бейджи пользователя"},
                  {"name":"GetDevices","req":"GetDevicesRequest","resp":"GetDevicesResponse","stream":false,"description":"Список авторизованных устройств"},
                  {"name":"GetCurrentDevice","req":"GetCurrentDeviceRequest","resp":"GetCurrentDeviceResponse","stream":false,"description":"Информация о текущем устройстве"},
                  {"name":"RenameDevice","req":"RenameDeviceRequest","resp":"RenameDeviceResponse","stream":false,"description":"Задать пользовательское имя устройству"},
                  {"name":"SetFirebaseToken","req":"SetFirebaseTokenRequest","resp":"SetFirebaseTokenResponse","stream":false,"description":"Зарегистрировать FCM-токен для push-уведомлений"},
                  {"name":"SetNotificationsEnabled","req":"SetNotificationsEnabledRequest","resp":"SetNotificationsEnabledResponse","stream":false,"description":"Включить/выключить push-уведомления"},
                  {"name":"GetPrivacySettings","req":"GetPrivacySettingsRequest","resp":"GetPrivacySettingsResponse","stream":false,"description":"Получить настройки приватности профиля"},
                  {"name":"UpdatePrivacySettings","req":"UpdatePrivacySettingsRequest","resp":"UpdatePrivacySettingsResponse","stream":false,"description":"Обновить настройки приватности"}
                ],
                "subsections":[
                  {"title":"User","type":"fields","items":[
                    {"name":"id","type":"int64","description":"Уникальный ID пользователя"},
                    {"name":"first_name","type":"string","description":"Имя"},
                    {"name":"last_name","type":"string","description":"Фамилия"},
                    {"name":"username","type":"string","description":"Юзернейм (уникальный)"},
                    {"name":"registration_date","type":"Timestamp","description":"Дата регистрации"},
                    {"name":"profile_picture","type":"string","description":"URL аватара (полный размер)"},
                    {"name":"bio","type":"string","description":"Описание профиля"},
                    {"name":"profile_picture_preview","type":"string","description":"URL превью аватара"},
                    {"name":"badges","type":"UserBadge[]","description":"Бейджи пользователя"},
                    {"name":"storage_limit_gb","type":"int32","description":"Лимит хранилища в ГБ"}
                  ]},
                  {"title":"ProfileFieldVisibility","type":"enum","items":[
                    {"name":"ALL","num":"0","description":"Видят все"},
                    {"name":"FRIENDS","num":"1","description":"Временно трактуется как NONE"},
                    {"name":"NONE","num":"2","description":"Скрыто ото всех"}
                  ]}
                ]}
                """ },
            new() { Id = Guid.NewGuid(), FileName = "messages_api.proto", DisplayName = "Messages — чаты и сообщения", Slug = "proto-messages", Order = 5, RpcDescriptions = """
                {"description":"Список чатов, отправка/получение сообщений, read receipts, участники групп",
                "rpcs":[
                  {"name":"ListChats","req":"ListChatsRequest","resp":"ListChatsResponse","stream":false,"description":"Список чатов с пагинацией. Максимум 50 за раз"},
                  {"name":"ListMessages","req":"ListMessagesRequest","resp":"ListMessagesResponse","stream":false,"description":"Сообщения чата. Пагинация вокруг опорного сообщения"},
                  {"name":"ListChatMembers","req":"ListChatMembersRequest","resp":"ListChatMembersResponse","stream":false,"description":"Участники чата с именами"},
                  {"name":"SendMessage","req":"SendMessageRequest","resp":"SendMessageResponse","stream":false,"description":"Отправить сообщение"},
                  {"name":"CreateGroupChat","req":"CreateGroupChatRequest","resp":"CreateGroupChatResponse","stream":false,"description":"Создать групповой чат"},
                  {"name":"KickUser","req":"KickUserRequest","resp":"KickUserResponse","stream":false,"description":"Удалить пользователя из группового чата"},
                  {"name":"MarkAsRead","req":"MarkAsReadRequest","resp":"MarkAsReadResponse","stream":false,"description":"Отметить сообщения прочитанными"},
                  {"name":"ListChatAttachments","req":"ListChatAttachmentsRequest","resp":"ListChatAttachmentsResponse","stream":false,"description":"Вложения чата с фильтрацией по типу"},
                  {"name":"GetPersonChatId","req":"GetPersonChatIdRequest","resp":"GetPersonChatIdResponse","stream":false,"description":"Получить ID личного чата с пользователем"},
                  {"name":"GetChatInfo","req":"GetChatInfoRequest","resp":"GetChatInfoResponse","stream":false,"description":"Метаданные чата"}
                ],
                "subsections":[
                  {"title":"Chat","type":"fields","items":[
                    {"name":"id","type":"string","description":"Идентификатор чата"},
                    {"name":"title","type":"string","description":"Название чата"},
                    {"name":"picture","type":"string","description":"URL картинки чата"},
                    {"name":"is_group_chat","type":"bool","description":"true — групповой чат, false — личная переписка"},
                    {"name":"last_message","type":"Message","description":"Последнее сообщение"},
                    {"name":"members","type":"ChatMember[]","description":"Участники (только для ЛС)"},
                    {"name":"count_unread","type":"int64","description":"Количество непрочитанных сообщений"},
                    {"name":"first_unread_message_id","type":"int64","description":"ID первого непрочитанного"}
                  ]},
                  {"title":"OutgoingMessage — отправляемое сообщение","type":"fields","items":[
                    {"name":"text","type":"string","description":"Текст сообщения"},
                    {"name":"files_ids","type":"string[]","description":"ID файлов загруженных через FilesApi"}
                  ]}
                ]}
                """ },
            new() { Id = Guid.NewGuid(), FileName = "files_api.proto", DisplayName = "Files — файлы и хранилище", Slug = "proto-files", Order = 6, RpcDescriptions = """
                {"description":"Загрузка/скачивание файлов, дедупликация, стикерпаки, информация о хранилище",
                "rpcs":[
                  {"name":"GetUploadUrl","req":"GetUploadUrlRequest","resp":"GetUploadUrlResponse","stream":false,"description":"Получить одноразовый URL для HTTP multipart загрузки файла"},
                  {"name":"GetTempDownloadUrl","req":"GetTempDownloadUrlRequest","resp":"GetTempDownloadUrlResponse","stream":false,"description":"Получить временные ссылки на скачивание по списку file_ids"},
                  {"name":"CheckFileHash","req":"CheckFileHashRequest","resp":"CheckFileHashResponse","stream":false,"description":"Проверить существует ли файл с таким SHA-256 хешем"},
                  {"name":"GetUserStorageInfo","req":"GetUserStorageInfoRequest","resp":"GetUserStorageInfoResponse","stream":false,"description":"Статистика хранилища"},
                  {"name":"ListStickerPacks","req":"ListStickerPacksRequest","resp":"ListStickerPacksResponse","stream":false,"description":"Список доступных стикерпаков"},
                  {"name":"GetStickerPack","req":"GetStickerPackRequest","resp":"GetStickerPackResponse","stream":false,"description":"Стикерпак со всеми стикерами"}
                ],
                "subsections":[
                  {"title":"UploadFileType","type":"enum","items":[
                    {"name":"UPLOAD_FILE_TYPE_UNKNOWN","num":"0","description":"Неизвестный тип"},
                    {"name":"USER_AVATAR","num":"1","description":"Аватар пользователя"},
                    {"name":"MESSAGE_ATTACHMENT_IMAGE","num":"2","description":"Изображение во вложении"},
                    {"name":"MESSAGE_ATTACHMENT_VIDEO","num":"3","description":"Видео во вложении"},
                    {"name":"MESSAGE_ATTACHMENT_GIF","num":"4","description":"GIF-анимация"},
                    {"name":"MESSAGE_ATTACHMENT_DOCUMENT","num":"5","description":"Документ"},
                    {"name":"CHAT_PICTURE","num":"6","description":"Обложка группового чата"},
                    {"name":"MESSAGE_ATTACHMENT_AUDIO","num":"7","description":"Аудиофайл"},
                    {"name":"MESSAGE_ATTACHMENT_VOICE","num":"8","description":"Голосовое сообщение"},
                    {"name":"MESSAGE_ATTACHMENT_STICKER","num":"9","description":"Стикер (WebP)"}
                  ]},
                  {"title":"GetUserStorageInfoResponse","type":"fields","items":[
                    {"name":"total_used_storage","type":"int64","description":"Общий использованный объём в байтах"},
                    {"name":"storage_limit","type":"int64","description":"Лимит хранилища в байтах"},
                    {"name":"storage_by_types","type":"StorageByType[]","description":"Разбивка по типам файлов"}
                  ]}
                ]}
                """ },
            new() { Id = Guid.NewGuid(), FileName = "updates_api.proto", DisplayName = "Updates — реальное время", Slug = "proto-updates", Order = 7, RpcDescriptions = """
                {"description":"Серверный стриминг новых сообщений и статусов прочтения",
                "warning":"Поле new_read_by в MessageReadEvent содержит ПОЛНЫЙ список всех прочитавших сообщение, а не только новых. При обновлении UI заменяй список целиком.",
                "rpcs":[
                  {"name":"SubscribeNewMessages","req":"SubscribeNewMessagesRequest","resp":"stream NewMessageEvent","stream":true,"description":"Серверный стрим новых сообщений во всех чатах"},
                  {"name":"SubscribeMessagesRead","req":"SubscribeMessagesReadRequest","resp":"stream MessageReadEvent","stream":true,"description":"Серверный стрим событий прочтения сообщений"}
                ],
                "subsections":[
                  {"title":"NewMessageEvent","type":"fields","items":[
                    {"name":"message","type":"shared.Message","description":"Полная структура нового сообщения"},
                    {"name":"chat_id","type":"string","description":"ID чата, в который пришло сообщение"}
                  ]},
                  {"title":"MessageReadEvent","type":"fields","items":[
                    {"name":"chat_id","type":"string","description":"ID чата"},
                    {"name":"message_id","type":"int64","description":"ID прочитанного сообщения"},
                    {"name":"new_read_by","type":"int64[]","description":"ПОЛНЫЙ список ID прочитавших (не дельта!)"}
                  ]}
                ],
                "info":"Реализуй reconnect с exponential backoff: при обрыве стрима ожидай 2с → 4с → 8с → ... до 30с."}
                """ },
            new() { Id = Guid.NewGuid(), FileName = "onliner_api.proto", DisplayName = "Onliner — онлайн-статусы", Slug = "proto-onliner", Order = 8, RpcDescriptions = """
                {"description":"Трекинг онлайн-статусов пользователей в реальном времени",
                "rpcs":[
                  {"name":"SubscribeToOnlineStatus","req":"SubscribeToOnlineStatusRequest","resp":"stream UserOnlineStatus","stream":true,"description":"Стрим изменений статуса для заданного списка пользователей"},
                  {"name":"SetOnlineStatus","req":"SetOnlineStatusRequest","resp":"SetOnlineStatusResponse","stream":false,"description":"Обновить статус «В сети». Вызывать каждые 3 секунды"},
                  {"name":"GetOnlineStatus","req":"GetOnlineStatusRequest","resp":"GetOnlineStatusResponse","stream":false,"description":"Получить текущий статус списка пользователей"},
                  {"name":"ChangeUsersInSubscription","req":"ChangeUsersInSubscriptionRequest","resp":"ChangeUsersInSubscriptionResponse","stream":false,"description":"Обновить список пользователей для подписки"}
                ],
                "subsections":[
                  {"title":"UserOnlineStatus","type":"fields","items":[
                    {"name":"user_id","type":"int64","description":"ID пользователя"},
                    {"name":"status","type":"StatusTypeId","description":"Текущий статус: ONLINE или OFFLINE"},
                    {"name":"last_seen","type":"Timestamp","description":"Время последнего изменения статуса"}
                  ]},
                  {"title":"StatusTypeId","type":"enum","items":[
                    {"name":"STATUS_TYPE_ID_UNKNOWN","num":"0","description":"Неизвестен"},
                    {"name":"STATUS_ONLINE","num":"1","description":"Пользователь онлайн"},
                    {"name":"STATUS_OFFLINE","num":"2","description":"Пользователь офлайн"}
                  ]}
                ],
                "info":"Паттерн keepalive: вызывай SetOnlineStatus({}) каждые 3 секунды пока приложение активно."}
                """ },
            new() { Id = Guid.NewGuid(), FileName = "fast_auth_api.proto", DisplayName = "FastAuth — быстрая авторизация", Slug = "proto-fastauth", Order = 9, RpcDescriptions = """
                {"description":"QR-авторизация и подключение доверенных устройств",
                "rpcs":[
                  {"name":"GenerateConnectDeviceToken","req":"GenerateConnectDeviceTokenRequest","resp":"GenerateConnectDeviceTokenResponse","stream":false,"description":"Создать токен для подключения нового доверенного устройства"},
                  {"name":"ConnectDevice","req":"ConnectDeviceRequest","resp":"ConnectDeviceResponse","stream":false,"description":"Подключить устройство используя токен"},
                  {"name":"AcceptConnectDevice","req":"AcceptConnectDeviceRequest","resp":"AcceptConnectDeviceResponse","stream":false,"description":"Принять подключение устройства"},
                  {"name":"SubscribeConnectDeviceStatus","req":"SubscribeConnectDeviceStatusRequest","resp":"stream ConnectDeviceStatusEvent","stream":true,"description":"Стрим статуса подключения устройства"},
                  {"name":"GenerateFastAuthToken","req":"GenerateFastAuthTokenRequest","resp":"GenerateFastAuthTokenResponse","stream":false,"description":"Создать токен быстрой авторизации (QR или текст)"},
                  {"name":"CreateFastAuth","req":"CreateFastAuthRequest","resp":"CreateFastAuthResponse","stream":false,"description":"Инициировать быстрый вход используя токен"},
                  {"name":"CheckFastAuth","req":"CheckFastAuthRequest","resp":"CheckFastAuthResponse","stream":false,"description":"Проверить статус быстрой авторизации"},
                  {"name":"AcceptFastAuth","req":"AcceptFastAuthRequest","resp":"AcceptFastAuthResponse","stream":false,"description":"Принять запрос быстрой авторизации"},
                  {"name":"SubscribeFastAuthRequests","req":"SubscribeFastAuthRequestsRequest","resp":"stream FastAuthRequest","stream":true,"description":"Стрим входящих запросов быстрой авторизации"},
                  {"name":"SubscribeFastAuthResult","req":"SubscribeFastAuthResultRequest","resp":"stream FastAuthResult","stream":true,"description":"Стрим результата быстрой авторизации"},
                  {"name":"ListConnectedDevices","req":"ListConnectedDevicesRequest","resp":"ListConnectedDevicesResponse","stream":false,"description":"Список подключённых устройств"},
                  {"name":"RemoveConnectedDevice","req":"RemoveConnectedDeviceRequest","resp":"RemoveConnectedDeviceResponse","stream":false,"description":"Удалить доверенное устройство"}
                ],
                "info":"FastAuth позволяет авторизоваться на новом устройстве отсканировав QR-код с уже авторизованного."}
                """ },
            new() { Id = Guid.NewGuid(), FileName = "navigator_api.proto", DisplayName = "Navigator — реестр серверов", Slug = "proto-navigator", Order = 10, RpcDescriptions = """
                {"description":"Публичный реестр серверов BarkFluff. Точка входа для клиента",
                "rpcs":[
                  {"name":"ListServers","req":"ListServersRequest","resp":"ListServersResponse","stream":false,"description":"Получить список всех зарегистрированных серверов"},
                  {"name":"RegisterServer","req":"RegisterServerRequest","resp":"RegisterServerResponse","stream":false,"description":"Зарегистрировать новый сервер в публичном реестре"}
                ],
                "subsections":[
                  {"title":"ServerInfo","type":"fields","items":[
                    {"name":"name","type":"string","description":"Отображаемое имя сервера"},
                    {"name":"description","type":"string","description":"Описание сервера"},
                    {"name":"accounts_count","type":"int64","description":"Количество зарегистрированных пользователей"},
                    {"name":"beacon_uri","type":"ServiceEndpoint","description":"Адрес Beacon-сервиса { host, port }"},
                    {"name":"server_public_name","type":"string","description":"Публичное имя (поддомен, slug и т.п.)"},
                    {"name":"location","type":"string","description":"Географическое расположение сервера"},
                    {"name":"color","type":"ServerColor","description":"Цветовая схема: lite_hex / main_hex / hard_hex"}
                  ]}
                ],
                "info":"Публичный адрес: navigator.barkfluff.com:443 — gRPC с TLS. Не требует авторизации. Это единственный захардкоженный адрес в клиенте."}
                """ }
        ];
    }
}
