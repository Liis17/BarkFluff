# BarkFluff — Аутентификация и заголовки (XAuth)

## Обзор

Все gRPC-запросы к сервисам BarkFluff используют систему **XAuth**. Каждый запрос должен содержать обязательные metadata-заголовки с описанием клиента, а защищённые методы — дополнительно токен доступа.

---

## Обязательные metadata-заголовки

Эти заголовки необходимо передавать в metadata **каждого gRPC-запроса**:

| Заголовок       | Тип    | Описание                                                    | Пример                     |
|-----------------|--------|-------------------------------------------------------------|----------------------------|
| `x-device-id`   | string | Уникальный идентификатор устройства (UUID v4, генерируется один раз при установке) | `550e8400-e29b-41d4-a716-446655440000` |
| `x-device-name` | string | Читаемое имя устройства                                     | `Samsung Galaxy S24`        |
| `x-ip`          | string | IP-адрес клиента                                            | `192.168.1.10`              |
| `x-os`          | string | Операционная система                                        | `Android 14`                |
| `x-app-name`    | string | Название вашего клиента                                     | `MyBarkFluffClient`         |
| `x-app-version` | string | Версия приложения                                           | `1.0.0`                     |

> `x-device-id` должен сохраняться между сессиями — он идентифицирует устройство в системе управления сессиями.

---

## Заголовок авторизации

Для защищённых методов дополнительно передаётся:

| Заголовок      | Описание                      |
|----------------|-------------------------------|
| `x-auth-token` | Access-токен пользователя     |

Токен передаётся как строка (без префикса `Bearer`).

---

---

## Поток подключения

### 1. Найти сервер (Navigator)

Используйте публичный Navigator для получения списка серверов:

```
navigator.barkfluff.com:443  (gRPC с TLS)
```

```protobuf
// navigator_api.proto
rpc ListServers(ListServersRequest) returns (ListServersResponse);
```

Ответ содержит список серверов с адресом `beacon_uri` для каждого.

### 2. Получить адреса микросервисов (Beacon)

Подключитесь к Beacon выбранного сервера:

```protobuf
// beacon_api.proto
rpc GetServerInfo(GetServerInfoRequest) returns (GetServerInfoResponse);
```

Ответ содержит адреса (`host:port`) и флаг `tls_enabled` для каждого микросервиса: `identity`, `users`, `files`, `messages`, `updates`, `onliner`.

### 3. Зарегистрировать аккаунт (Identity)

```protobuf
// Шаг 1 — создать черновик аккаунта
rpc CreateAccount(CreateAccountRequest) returns (CreateAccountResponse);
// Возвращает code_id

// Шаг 2 — подтвердить email-кодом (отправляется на почту)
rpc ConfirmAccount(ConfirmAccountRequest) returns (ConfirmAccountResponse);
// Возвращает refresh_token
```

После подтверждения немедленно получите access-токен (шаг 5).

### 4. Авторизоваться (Identity)

```protobuf
rpc Auth(AuthRequest) returns (AuthResponse);
// Возвращает access_token + refresh_token
```

`AuthRequest.login` — это `oneof`: передаётся либо `username`, либо `email`.

### 5. Обновить access-токен

Access-токен имеет ограниченный срок действия. Когда он истечёт — обновите его через:

```protobuf
rpc CreateToken(CreateTokenRequest) returns (CreateTokenResponse);
// refresh_token → access_token
```

---

## Двухфакторная аутентификация (2FA)

### При авторизации

Если у пользователя включена 2FA, gRPC-вызов `Auth` завершится с ошибкой `UNAUTHENTICATED` и trailer-заголовком:

```
x-error-code: C1576884-12D8-4722-A7EE-9F9789AD1265
```

Это означает: требуется OTP-код. Повторите вызов `Auth`, передав `otp_code`.

### Типы 2FA

| `OtpTypeId`     | Описание                              |
|-----------------|---------------------------------------|
| `Authenticator` | Google Authenticator и совместимые    |
| `Email`         | Код на email                          |

---

## Коды ошибок (x-error-code)

Сервер возвращает бизнес-ошибки через trailer-заголовок `x-error-code`:

| Код (GUID)                               | Исключение                          | Описание                            |
|------------------------------------------|-------------------------------------|-------------------------------------|
| `C1576884-12D8-4722-A7EE-9F9789AD1265`  | `OtpCodeNeedException`              | Требуется OTP-код 2FA               |
| `803B632C-4457-4B05-9435-9C3DD0F41E00`  | `NotValidOtpCodeException`          | Неверный OTP-код                    |
| `21BFB9B5-C377-45D1-9B15-6B7F3432B397`  | `InvalidLoginOrPasswordException`   | Неверный логин или пароль           |

---

## Пример подключения (псевдокод)

```
// 1. Metadata для каждого запроса
metadata = {
    "x-device-id":   "550e8400-e29b-41d4-a716-446655440000",
    "x-device-name": "My Device",
    "x-ip":          "1.2.3.4",
    "x-os":          "Android 14",
    "x-app-name":    "MyClient",
    "x-app-version": "1.0.0"
}

// 2. Получить список серверов
servers = NavigatorApi.ListServers({})

// 3. Получить адреса сервисов
info = BeaconApi.GetServerInfo({})  // @ info.identity.endpoint

// 4. Авторизоваться
auth = IdentityApi.Auth({ username: "user", password: "pass" }, metadata)
access_token  = auth.access_token.value
refresh_token = auth.refresh_token.value

// 5. Использовать защищённые методы
metadata["x-auth-token"] = access_token
user = UsersApi.GetUser({ user_id: 123 }, metadata)

// 6. Обновить токен когда истечёт
new_access = IdentityApi.CreateToken({ refresh_token: refresh_token }, metadata)
```

---

## Примечания

- Все временны́е поля используют тип `google.protobuf.Timestamp`.
- `option csharp_namespace` в `.proto` файлах — это C#-специфичная опция. В других языках она игнорируется.
- Сервисы `*ServerApi` (напр. `UsersServerApi`, `FilesServerApi`) предназначены для внутреннего взаимодействия между сервисами и не доступны с пользовательским токеном.
