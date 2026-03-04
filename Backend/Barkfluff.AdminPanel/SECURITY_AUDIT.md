# Аудит Безопасности: BarkFluff.AdminPanel

**Дата аудита:** 4 марта 2026 г.  
**Аудитор:** Security Assessment Team  
**Статус:** 🔴 КРИТИЧЕСКОЕ СОСТОЯНИЕ

---

## Резюме

Сервис BarkFluff.AdminPanel находится в **критическом состоянии**. Прямой доступ к Docker socket, полное управление пользователями без аудита, слабая аутентификация.

---

## Критические уязвимости

### 1. Docker socket доступ — полный контроль над хостом
| Параметр | Значение |
|----------|----------|
| **Файл** | `Services/DockerService.cs` |
| **Метод** | `RunDockerCommandAsync(...)` |
| **Уровень** | 🔴 Критический |
| **CWE** | CWE-78: Improper Neutralization of Special Elements used in an OS Command |

**Описание проблемы:**
```csharp
// Строки 273-290: Перезапуск через helper-контейнер
await RunDockerCommandAsync(
    "run", "-d", "--rm",
    "--name", "admin-panel-restarter",
    "--user", "root",
    "-v", $"{dockerSock}:/var/run/docker.sock",
    // ...
    "-c", "sleep 2 && docker restart admin-panel"
);
```

**Как эксплуатировать:**
1. Через Docker socket можно запустить контейнер с любыми правами
2. Получить доступ к файловой системе хоста
3. Выполнить произвольные команды на хосте
4. Украсть credentials других сервисов

**Рекомендации по исправлению:**
```csharp
// Ограничить Docker операции только restart
// Запретить volume mounts
// Запретить --user root
// Использовать отдельный non-root пользователь
```

---

### 2. Управление пользователями без аудита
| Параметр | Значение |
|----------|----------|
| **Файл** | `Endpoints/UsersEndpoints.cs` |
| **Уровень** | 🔴 Критический |
| **CWE** | CWE-778: Insufficient Logging |

**Описание проблемы:**
```csharp
// Строки 233-245: Отключение 2FA любому пользователю
await identityClient.DisableOtpVerificationServerAsync(new DisableOtpVerificationServerRequest
{
    UserId = id,
    OtpType = (OtpTypeId)body.OtpType
});

// Строки 206-216: Изменение лимита хранилища
var response = await usersClient.UpdateStorageLimitAsync(new UpdateStorageLimitRequest
{
    UserId = id,
    StorageLimitGb = body.StorageLimitGb
});
```

**Как эксплуатировать:**
1. Отключить 2FA любому пользователю (включая других админов)
2. Изменить storage limit без ограничений
3. Назначить/удалить баджи
4. Загрузить аватарку от имени пользователя

**Рекомендации по исправлению:**
- Детальный аудит всех действий администраторов
- Требовать подтверждение для критических операций
- Разделение ролей (super-admin, operator, viewer)

---

### 3. Слабая аутентификация
| Параметр | Значение |
|----------|----------|
| **Файл** | `Services/AuthService.cs` |
| **Уровень** | 🔴 Критический |
| **CWE** | CWE-306: Missing Authentication for Critical Function |

**Описание проблемы:**
- Аутентификация по Telegram username (может быть изменен)
- Нет проверки соответствия IP при создании и использовании токена
- Race condition между созданием запроса и подтверждением

**Рекомендации по исправлению:**
- Использовать Telegram user ID вместо username
- Привязка токена к IP
- Добавить 2FA для админ-панели

---

## Высокие уязвимости

### 4. Токены в cookies без HttpOnly
| Параметр | Значение |
|----------|----------|
| **Файл** | `Middleware/TokenAuthMiddleware.cs` |
| **Уровень** | 🟠 Высокий |
| **CWE** | CWE-614: Sensitive Cookie in HTTPS Session Without 'Secure' Attribute |

**Описание проблемы:**
```csharp
// Строки 52-58: Валидация токена из cookies
if (!context.Request.Cookies.TryGetValue("auth_token", out var tokenValue) ||
    !Guid.TryParse(tokenValue, out var tokenId))
{
    return null;
}
```

**Рекомендации по исправлению:**
```csharp
// Установить HttpOnly и Secure флаги
var cookieOptions = new CookieOptions
{
    HttpOnly = true,
    Secure = true,
    SameSite = SameSiteMode.Strict
};
```

---

### 5. Self-update уязвимость
| Параметр | Значение |
|----------|----------|
| **Файл** | `Services/DockerService.cs` |
| **Метод** | `RestartAdminPanelAsync()`, `UpdateAdminPanelAsync()` |
| **Уровень** | 🟠 Высокий |
| **CWE** | CWE-78: OS Command Injection |

**Описание проблемы:**
- Обновление самого AdminPanel через Docker
- Может быть использовано для компрометации

**Рекомендации:**
- Отключить self-update
- Требовать подтверждение для обновления

---

### 6. Доступ к логам всех сервисов
| Параметр | Значение |
|----------|----------|
| **Файл** | `Endpoints/SeqEndpoints.cs` |
| **Уровень** | 🟠 Высокий |
| **CWE** | CWE-200: Information Exposure |

**Описание:**
- Все логи всех сервисов доступны
- Логи могут содержать чувствительные данные (пароли, токены)

**Рекомендации:**
- Ограничить доступ к логам
- Маскировать чувствительные данные в логах

---

### 7. Только проверка AuthToken для Docker операций
| Параметр | Значение |
|----------|----------|
| **Файл** | `Endpoints/DockerEndpoints.cs` |
| **Уровень** | 🟠 Высокий |
| **CWE** | CWE-284: Improper Access Control |

**Описание проблемы:**
```csharp
// Строки 24-29: Только проверка токена
if (context.Items["AuthToken"] is not AuthToken)
    return Results.Unauthorized();

var containers = await dockerService.GetContainersAsync();
```

**Рекомендации:**
- Проверять что пользователь - админ
- Разделение ролей для Docker операций

---

## Средние уязвимости

### 8. Отсутствие 2FA для админов
| Параметр | Значение |
|----------|----------|
| **Файл** | `Endpoints/AuthEndpoints.cs` |
| **Уровень** | 🟡 Средний |
| **CWE** | CWE-306: Missing Authentication |

**Рекомендации:**
- Реализовать 2FA для админ-панели

---

### 9. Telegram Bot токен в конфигурации
| Параметр | Значение |
|----------|----------|
| **Файл** | `Services/TelegramBotService.cs` |
| **Уровень** | 🟡 Средний |
| **CWE** | CWE-311: Missing Encryption |

**Рекомендации:**
- Шифрование токена бота
- Использовать environment variables

---

### 10. Команды в боте без дополнительной аутентификации
| Параметр | Значение |
|----------|----------|
| **Файл** | `Services/TelegramBotService.cs` |
| **Метод** | Обработка `/kill`, `/rename` |
| **Уровень** | 🟡 Средний |

**Рекомендации:**
- Дополнительная аутентификация для критических команд

---

## Сводная таблица

| # | Уязвимость | Уровень | Статус |
|---|------------|---------|--------|
| 1 | Docker socket доступ | 🔴 Critical | ⏳ Ожидает |
| 2 | Управление пользователями без аудита | 🔴 Critical | ⏳ Ожидает |
| 3 | Слабая аутентификация | 🔴 Critical | ⏳ Ожидает |
| 4 | Токены в cookies без HttpOnly | 🟠 High | ⏳ Ожидает |
| 5 | Self-update уязвимость | 🟠 High | ⏳ Ожидает |
| 6 | Доступ к логам | 🟠 High | ⏳ Ожидает |
| 7 | Только проверка AuthToken | 🟠 High | ⏳ Ожидает |
| 8 | Отсутствие 2FA для админов | 🟡 Medium | ⏳ Ожидает |
| 9 | Telegram Bot токен | 🟡 Medium | ⏳ Ожидает |
| 10 | Команды в боте | 🟡 Medium | ⏳ Ожидает |

---

## Приоритетные рекомендации

### Немедленно (Critical):
1. ✅ **Ограничить Docker операции** (только restart, нет exec, нет volume mounts)
2. ✅ **Добавить детальный аудит** всех действий администраторов
3. ✅ **Реализовать 2FA** для админ-панели
4. ✅ **Исправить аутентификацию** (Telegram user ID вместо username)

### Высокий приоритет:
5. HttpOnly + Secure флаги для cookies
6. Привязка токенов к IP/устройству
7. Разделение ролей администраторов (super-admin, operator, viewer)
8. Rate limiting для всех endpoints
9. Шифрование токенов в хранилище

### Средний приоритет:
10. Сессии с ограниченным временем жизни
11. Whitelist операций для каждого админа
12. Маскирование чувствительных данных в логах

---

## Статус Исправления

| Уязвимость | Статус | Дата Исправления | Примечания |
|------------|--------|------------------|------------|
| 1. Docker socket | ⏳ Ожидает | - | Критично! |
| 2. Аудит | ⏳ Ожидает | - | - |
| 3. Аутентификация | ⏳ Ожидает | - | - |
| 4. Cookies | ⏳ Ожидает | - | - |
| 5. Self-update | ⏳ Ожидает | - | - |
| 6. Логи | ⏳ Ожидает | - | - |
| 7. AuthToken | ⏳ Ожидает | - | - |
| 8. 2FA | ⏳ Ожидает | - | - |

---

## Контакты

security@barkfluff.com
