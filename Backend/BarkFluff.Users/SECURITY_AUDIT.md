# Аудит Безопасности: BarkFluff.Users

**Дата аудита:** 4 марта 2026 г.  
**Аудитор:** Security Assessment Team  
**Статус:** 🔴 Критические уязвимости обнаружены

---

## Резюме

Сервис BarkFluff.Users содержит **24 уязвимости**, включая **10 критических**, **5 высоких**, **9 средних**. Сервис требует немедленного исправления перед развертыванием в продакшен.

---

## Критические уязвимости (Critical)

### 1. IDOR уязвимость в GetUserContacts
| Параметр | Значение |
|----------|----------|
| **Файл** | `Features/GetUserContacts/GetUserContactsCommandHandler.cs` |
| **Метод** | `Handle(GetUserContactsCommand request, ...)` |
| **Уровень** | 🔴 Критический |
| **CWE** | CWE-639: Authorization Bypass Through User-Controlled Key |

**Описание проблемы:**
```csharp
public async Task<GetUserContactsResponse> Handle(GetUserContactsCommand request, CancellationToken cancellationToken)
{
    var user = await _usersStorage.GetById(request.UserId);  // Нет проверки авторизации
    // ...
    return new GetUserContactsResponse()
    {
        Contact = new UserContact() { Email = user.Contact.Email }  // Возвращает email
    };
}
```

**Как эксплуатировать:**
1. Злоумышленник может перебирать userId и получать email всех пользователей системы
2. Массовый сбор персональных данных

**Рекомендации по исправлению:**
```csharp
public async Task<GetUserContactsResponse> Handle(GetUserContactsCommand request, CancellationToken cancellationToken)
{
    // Проверка: пользователь может получить только свои контакты
    if (request.UserId != _userContext.UserId)
    {
        _logger.LogWarning("Попытка доступа к контактам пользователя {RequestedId} от {CurrentId}", 
            request.UserId, _userContext.UserId);
        throw new UnauthorizedAccessException();
    }
    
    var user = await _usersStorage.GetById(request.UserId);
    // ...
}
```

---

### 2. IDOR уязвимость в GetUserDevices
| Параметр | Значение |
|----------|----------|
| **Файл** | `Features/Devices/GetUserDevices/GetUserDevicesQueryHandler.cs` |
| **Метод** | `Handle(GetUserDevicesQuery request, ...)` |
| **Уровень** | 🔴 Критический |
| **CWE** | CWE-639: Authorization Bypass |

**Описание проблемы:**
```csharp
public async Task<GetUserDevicesResponse> Handle(GetUserDevicesQuery request, CancellationToken cancellationToken)
{
    var devices = await devicesStorage.GetDevicesByUserId(request.UserId);  // Нет проверки
    // ...
}
```

**Как эксплуатировать:**
1. Перебор userId для получения информации об устройствах других пользователей
2. Сбор информации для targeted атак

**Рекомендации по исправлению:**
```csharp
if (request.UserId != _userContext.UserId)
{
    throw new UnauthorizedAccessException();
}
```

---

### 3. IDOR уязвимость в DeleteUserDevice
| Параметр | Значение |
|----------|----------|
| **Файл** | `Features/Devices/DeleteUserDevice/DeleteUserDeviceCommandHandler.cs` |
| **Метод** | `Handle(DeleteUserDeviceCommand request, ...)` |
| **Уровень** | 🔴 Критический |
| **CWE** | CWE-639: Authorization Bypass |

**Описание проблемы:**
```csharp
public async Task<DeleteUserDeviceResponse> Handle(DeleteUserDeviceCommand request, CancellationToken cancellationToken)
{
    await devicesStorage.DeleteDevice(request.DeviceId, request.UserId);  // request.UserId из запроса!
}
```

**Как эксплуатировать:**
1. Злоумышленник может удалять устройства других пользователей
2. DoS атака на пользователей

**Рекомендации по исправлению:**
```csharp
// Использовать userContext.UserId вместо request.UserId
await devicesStorage.DeleteDevice(request.DeviceId, _userContext.UserId);
```

---

### 4. IDOR уязвимость в операциях с баджами
| Параметр | Значение |
|----------|----------|
| **Файл** | `Features/Badges/AssignUserBadge/AssignUserBadgeCommandHandler.cs` |
| **Метод** | `Handle(AssignUserBadgeCommand request, ...)` |
| **Уровень** | 🔴 Критический |
| **CWE** | CWE-639: Authorization Bypass |

**Описание проблемы:**
```csharp
public async Task<AssignUserBadgeResponse> Handle(AssignUserBadgeCommand request, CancellationToken cancellationToken)
{
    var userBadge = await _usersStorage.AssignBadgeToUserAsync(request.UserId, request.BadgeId, priority);
    // Нет проверки: имеет ли право текущий пользователь назначать бадж этому userId
}
```

**Как эксплуатировать:**
1. Назначение/удаление баджей любому пользователю
2. Компрометация системы репутации

**Рекомендации по исправлению:**
- Эти методы должны быть доступны только через Service token с административными правами
- Добавить проверку `[Authorize(Policy = nameof(TokenType.Service))]`

---

### 5. Отсутствие валидации username при регистрации
| Параметр | Значение |
|----------|----------|
| **Файл** | `Features/AddDraftUser/AddDraftUserCommandHandler.cs` |
| **Метод** | `Handle(AddDraftUserCommand request, ...)` |
| **Уровень** | 🔴 Критический |
| **CWE** | CWE-20: Improper Input Validation |

**Описание проблемы:**
- Нет валидации формата username (допустимые символы, длина, запрещённые паттерны)
- Нет проверки на XSS-паттерны

**Как эксплуатировать:**
```
username: "<script>alert(1)</script>"
username: "../../../etc/passwd"
username: "admin" (зарезервированное имя)
```

**Рекомендации по исправлению:**
```csharp
private void ValidateUsername(string username)
{
    if (string.IsNullOrWhiteSpace(username) || username.Length < 3 || username.Length > 32)
        throw new InvalidUsernameException("Длина от 3 до 32 символов");
    
    if (!Regex.IsMatch(username, @"^[a-zA-Z0-9_-]+$"))
        throw new InvalidUsernameException("Только латиница, цифры, _ и -");
    
    var reserved = new[] { "admin", "moderator", "support", "system" };
    if (reserved.Contains(username.ToLower()))
        throw new InvalidUsernameException("Зарезервированное имя");
}
```

---

### 6. XSS уязвимость через Bio
| Параметр | Значение |
|----------|----------|
| **Файл** | `Features/ChangeBio/ChangeBioCommandHandler.cs` |
| **Метод** | `Handle(ChangeBioCommand request, ...)` |
| **Уровень** | 🔴 Критический |
| **CWE** | CWE-79: Improper Neutralization of Input During Web Page Generation |

**Описание проблемы:**
```csharp
if (request.Bio != null && request.Bio.Length > 200)
{
    throw new BioTooLongException();
}
await _usersStorage.ChangeBio(_userContext.UserId, request.Bio);  // Сохраняется как есть
```

**Как эксплуатировать:**
```
<script>alert(document.cookie)</script>
<img src=x onerror="alert(1)">
<a href="javascript:alert(1)">click</a>
```

**Рекомендации по исправлению:**
- Санитизировать HTML при сохранении или отображении
- Использовать библиотеку типа HtmlSanitizer
- Либо разрешить только plain text

---

### 7. XSS уязвимость через FirstName/LastName
| Параметр | Значение |
|----------|----------|
| **Файл** | `Features/ChangeName/ChangeNameCommandHandler.cs` |
| **Метод** | `Handle(ChangeNameCommand request, ...)` |
| **Уровень** | 🔴 Критический |
| **CWE** | CWE-79: XSS |

**Описание проблемы:**
- Имя и фамилия сохраняются без валидации

**Рекомендации по исправлению:**
```csharp
private void ValidateName(string name)
{
    if (string.IsNullOrWhiteSpace(name) || name.Length > 50)
        throw new InvalidNameException();
    
    // Разрешены только буквы, пробел, дефис
    if (!Regex.IsMatch(name, @"^[a-zA-Zа-яА-ЯёЁ\s-]+$"))
        throw new InvalidNameException("Недопустимые символы");
}
```

---

### 8. SSRF уязвимость в SetProfilePictureServer
| Параметр | Значение |
|----------|----------|
| **Файл** | `Features/SetProfilePictureServer/SetProfilePictureServerCommandHandler.cs` |
| **Метод** | `Handle(SetProfilePictureServerCommand request, ...)` |
| **Уровень** | 🔴 Критический |
| **CWE** | CWE-918: Server-Side Request Forgery (SSRF) |

**Описание проблемы:**
```csharp
public async Task<SetProfilePictureServerResponse> Handle(SetProfilePictureServerCommand request, CancellationToken cancellationToken)
{
    await _usersStorage.UpdateProfilePicture(request.UserId, request.ProfilePictureUrl, ...);
    // request.ProfilePictureUrl не валидируется!
}
```

**Как эксплуатировать:**
```
http://localhost:8080/admin
http://169.254.169.254/latest/meta-data/ (AWS metadata)
http://internal-service:5000/secret
```

**Рекомендации по исправлению:**
```csharp
private bool IsValidPublicUrl(string url)
{
    if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        return false;
    
    if (uri.Scheme != "https")
        return false;
    
    // Проверка на private IP диапазоны
    var ip = Dns.GetHostAddresses(uri.Host);
    foreach (var address in ip)
    {
        if (IPAddress.IsLoopback(address) || 
            address.IsIPv6LinkLocal || 
            address.GetAddressBytes()[0] == 10 || 
            address.GetAddressBytes()[0] == 172 && address.GetAddressBytes()[1] >= 16 && address.GetAddressBytes()[1] <= 31)
        {
            return false;
        }
    }
    
    return true;
}
```

---

### 9. Отсутствие rate limiting
| Параметр | Значение |
|----------|----------|
| **Файл** | `Host/UsersApiService.cs` |
| **Метод** | Все gRPC методы |
| **Уровень** | 🔴 Критический |
| **CWE** | CWE-770: Allocation of Resources Without Limits or Throttling |

**Как эксплуатировать:**
1. Brute-force email/username через `CheckExistEmail` / `CheckExistUsername`
2. DoS через перебор userId
3. Enumeration всех пользователей через `SearchUsers`

**Рекомендации по исправлению:**
- CheckExist*: 10 запросов/минуту
- SearchUsers: 30 запросов/минуту
- ExportData: 1 запрос/час

---

### 10. Утечка информации через ExportData
| Параметр | Значение |
|----------|----------|
| **Файл** | `Features/ExportData/ExportDataCommandHandler.cs` |
| **Метод** | `Handle(ExportDataCommand request, ...)` |
| **Уровень** | 🔴 Критический |
| **CWE** | CWE-200: Information Exposure |

**Описание проблемы:**
```csharp
public async Task<ExportDataResponse> Handle(ExportDataCommand request, CancellationToken cancellationToken)
{
    var user = await _usersStorage.GetById(request.UserId);  // Нет проверки авторизации!
    // Экспортирует все данные пользователя
}
```

**Как эксплуатировать:**
1. Экспорт данных любого пользователя (GDPR violation)
2. Массовый сбор персональных данных

**Рекомендации по исправлению:**
```csharp
if (request.UserId != _userContext.UserId)
{
    // Проверка на service token для административного доступа
    if (!_userContext.HasPolicy(nameof(TokenType.Service)))
    {
        throw new UnauthorizedAccessException();
    }
}
```

---

## Высокие уязвимости (High)

### 11. Манипуляция storage limit
| Параметр | Значение |
|----------|----------|
| **Файл** | `Features/UpdateStorageLimit/UpdateStorageLimitCommandHandler.cs` |
| **Метод** | `Handle(UpdateStorageLimitCommand request, ...)` |
| **Уровень** | 🟠 Высокий |
| **CWE** | CWE-639: Authorization Bypass |

**Описание проблемы:**
```csharp
public async Task<UpdateStorageLimitResponse> Handle(UpdateStorageLimitCommand request, CancellationToken cancellationToken)
{
    await _usersStorage.UpdateStorageLimitGb(request.UserId, request.StorageLimitGb);
    // Любой может изменить свой или чужой лимит
}
```

**Рекомендации по исправлению:**
- Доступно только через Service token с административными правами

---

### 12. Отсутствие валидации email формата
| Параметр | Значение |
|----------|----------|
| **Файл** | `Features/AddDraftUser/AddDraftUserCommandHandler.cs` |
| **Метод** | `Handle(AddDraftUserCommand request, ...)` |
| **Уровень** | 🟠 Высокий |
| **CWE** | CWE-20: Improper Input Validation |

**Рекомендации по исправлению:**
```csharp
if (!new EmailAddressAttribute().IsValid(email))
{
    throw new InvalidEmailException();
}
```

---

### 13. Weak hashing алгоритм для паролей
| Параметр | Значение |
|----------|----------|
| **Файл** | `Helpers/PasswordHasher.cs` |
| **Метод** | `HashPassword(string password)` |
| **Уровень** | 🟠 Высокий |
| **CWE** | CWE-328: Reversible One-Way Hash |

**Описание проблемы:**
```csharp
public static string HashPassword(string password)
{
    using var sha256 = SHA256.Create();
    var hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
    return Convert.ToBase64String(hashedBytes);  // Нет соли!
}
```

**Рекомендации по исправлению:**
- Использовать PBKDF2, bcrypt или Argon2 с уникальной солью

---

### 14. Potential SQL Injection в SearchUsersByTrigram
| Параметр | Значение |
|----------|----------|
| **Файл** | `Persistence/Services/UsersStorage.cs` |
| **Метод** | `SearchUsersByTrigram(string searchTerm, ...)` |
| **Уровень** | 🟠 Высокий |
| **CWE** | CWE-89: SQL Injection |

**Описание проблемы:**
- Используется `FromSqlRaw` с параметрами, но требуется мониторинг

**Статус:** Код использует параметризованные запросы, что правильно.

---

### 15. Информация о draft пользователях утекает через SearchUsersServer
| Параметр | Значение |
|----------|----------|
| **Файл** | `Features/SearchUsersServer/SearchUsersServerQueryHandler.cs` |
| **Метод** | `Handle(SearchUsersServerQuery request, ...)` |
| **Уровень** | 🟠 Высокий |
| **CWE** | CWE-200: Information Exposure |

**Рекомендации по исправлению:**
- Добавить проверку `if (user.IsDraft) throw new UserNotFoundException()`

---

## Средние уязвимости (Medium)

### 16. Отсутствие лимита на Size в SearchUsers
| Параметр | Значение |
|----------|----------|
| **Файл** | `Features/SearchUsers/SearchUsersQueryHandler.cs` |
| **Уровень** | 🟡 Средний |

**Рекомендации:**
- Добавить общий лимит на количество запросов поиска в минуту

---

### 17. ProfilePicture не валидируется на тип файла
| Параметр | Значение |
|----------|----------|
| **Файл** | `Features/SetProfilePicture/SetProfilePictureCommandHandler.cs` |
| **Уровень** | 🟡 Средний |

**Рекомендации:**
- Добавить дополнительную валидацию MIME-типа и расширения файла

---

### 18. Нет валидации на отрицательные значения StorageLimitGb
| Параметр | Значение |
|----------|----------|
| **Файл** | `Features/UpdateStorageLimit/UpdateStorageLimitCommandHandler.cs` |
| **Уровень** | 🟡 Средний |

**Рекомендации по исправлению:**
```csharp
if (request.StorageLimitGb < 0 || request.StorageLimitGb > 250)
{
    throw new InvalidStorageLimitException();
}
```

---

### 19. Location в UserDevice не валидируется
| Параметр | Значение |
|----------|----------|
| **Файл** | `Features/Devices/RegisterDevice/RegisterDeviceCommandHandler.cs` |
| **Уровень** | 🟡 Средний |

**Рекомендации:**
- Ограничить длину (макс. 255 символов), санитизировать

---

### 20. Нет проверки на IsDraft в GetUser
| Параметр | Значение |
|----------|----------|
| **Файл** | `Features/GetUser/GetUserQueryHandler.cs` |
| **Уровень** | 🟡 Средний |

**Рекомендации:**
- Добавить проверку `if (user.IsDraft) throw new UserNotFoundException()`

---

### 21. Логирование чувствительных данных
| Параметр | Значение |
|----------|----------|
| **Файл** | Множественные файлы |
| **Уровень** | 🟡 Средний |

**Рекомендации:**
- Маскировать чувствительные данные в логах

---

### 22. Отсутствие пагинации в GetAllBadges
| Параметр | Значение |
|----------|----------|
| **Файл** | `Features/Badges/Queries/GetAllBadgesQueryHandler.cs` |
| **Уровень** | 🟡 Средний |

**Рекомендации:**
- Добавить пагинацию для защиты от DoS

---

### 23. Нет ограничения на количество баджей у пользователя
| Параметр | Значение |
|----------|----------|
| **Файл** | `Persistence/Services/UsersStorage.cs` |
| **Уровень** | 🟡 Средний |

**Рекомендации:**
- Добавить лимит (например, макс. 100 баджей)

---

### 24. BioTooLongException проверяет только максимальную длину
| Параметр | Значение |
|----------|----------|
| **Файл** | `Features/ChangeBio/ChangeBioCommandHandler.cs` |
| **Уровень** | 🟡 Средний |

**Рекомендации:**
- Добавить проверку на минимальную длину или пустое значение

---

## Сводная таблица уязвимостей

| # | Уязвимость | Уровень | Файл |
|---|------------|---------|------|
| 1 | IDOR в GetUserContacts | 🔴 Critical | Features/GetUserContacts/GetUserContactsCommandHandler.cs |
| 2 | IDOR в GetUserDevices | 🔴 Critical | Features/Devices/GetUserDevices/GetUserDevicesQueryHandler.cs |
| 3 | IDOR в DeleteUserDevice | 🔴 Critical | Features/Devices/DeleteUserDevice/DeleteUserDeviceCommandHandler.cs |
| 4 | IDOR в операциях с баджами | 🔴 Critical | Features/Badges/* |
| 5 | Отсутствие валидации username | 🔴 Critical | Features/AddDraftUser/AddDraftUserCommandHandler.cs |
| 6 | XSS через Bio | 🔴 Critical | Features/ChangeBio/ChangeBioCommandHandler.cs |
| 7 | XSS через FirstName/LastName | 🔴 Critical | Features/ChangeName/ChangeNameCommandHandler.cs |
| 8 | SSRF через profile_picture_url | 🔴 Critical | Features/SetProfilePictureServer/SetProfilePictureServerCommandHandler.cs |
| 9 | Отсутствие rate limiting | 🔴 Critical | Host/UsersApiService.cs |
| 10 | Утечка данных через ExportData | 🔴 Critical | Features/ExportData/ExportDataCommandHandler.cs |
| 11 | Манипуляция storage limit | 🟠 High | Features/UpdateStorageLimit/UpdateStorageLimitCommandHandler.cs |
| 12 | Отсутствие валидации email | 🟠 High | Features/AddDraftUser/AddDraftUserCommandHandler.cs |
| 13 | Weak password hashing | 🟠 High | Helpers/PasswordHasher.cs |
| 14 | Potential SQL Injection | 🟠 High | Persistence/Services/UsersStorage.cs |
| 15 | Утечка draft пользователей | 🟠 High | Features/SearchUsersServer/SearchUsersServerQueryHandler.cs |
| 16-24 | Средние уязвимости | 🟡 Medium | См. выше |

---

## Приоритетные рекомендации по исправлению

### Немедленно (Critical):
1. ✅ Исправить все IDOR уязвимости (пункты 1-4, 10)
2. ✅ Добавить валидацию username и email (пункты 5, 12)
3. ✅ Санитизировать Bio и имя (пункты 6, 7)
4. ✅ Валидировать URL в SetProfilePictureServer (пункт 8)
5. ✅ Добавить rate limiting (пункт 9)

### В течение 1 недели (High):
6. Ограничить доступ к UpdateStorageLimit (пункт 11)
7. Заменить SHA256 на bcrypt/PBKDF2 (пункт 13)
8. Добавить проверку IsDraft в GetUser (пункт 20)

### В течение 1 месяца (Medium/Low):
9. Добавить валидацию всех входных данных
10. Настроить логирование без чувствительных данных
11. Добавить лимиты на количество записей

---

## Статус Исправления

| Уязвимость | Статус | Дата Исправления | Примечания |
|------------|--------|------------------|------------|
| 1. IDOR GetUserContacts | ⏳ Ожидает | - | - |
| 2. IDOR GetUserDevices | ⏳ Ожидает | - | - |
| 3. IDOR DeleteUserDevice | ⏳ Ожидает | - | - |
| 4. IDOR Badges | ⏳ Ожидает | - | - |
| 5. Валидация username | ⏳ Ожидает | - | - |
| 6. XSS Bio | ⏳ Ожидает | - | - |
| 7. XSS Name | ⏳ Ожидает | - | - |
| 8. SSRF | ⏳ Ожидает | - | - |
| 9. Rate limiting | ⏳ Ожидает | - | Требуется Redis |
| 10. ExportData IDOR | ⏳ Ожидает | - | - |

---

## Контакты

По вопросам безопасности обращайтесь: security@barkfluff.com
