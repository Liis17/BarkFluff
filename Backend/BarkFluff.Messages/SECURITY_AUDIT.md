# Аудит Безопасности: BarkFluff.Messages

**Дата аудита:** 4 марта 2026 г.  
**Аудитор:** Security Assessment Team  
**Статус:** 🔴 Критические уязвимости обнаружены

---

## Резюме

Сервис BarkFluff.Messages содержит **14 уязвимостей**, включая **3 критические**, **7 высоких**, **4 средних**. Сервис требует немедленного исправления перед развертыванием в продакшен.

---

## Критические уязвимости (Critical)

### 1. IDOR - Экспорт данных пользователей
| Параметр | Значение |
|----------|----------|
| **Файл** | `Features/ExportData/GetUserAllMessagesQueryHandler.cs` |
| **Метод** | `Handle(GetUserAllMessagesQuery request, ...)` |
| **Уровень** | 🔴 Критический |
| **CWE** | CWE-639: Authorization Bypass Through User-Controlled Key |

**Описание проблемы:**
```csharp
public async Task<GetUserAllMessagesResponse> Handle(GetUserAllMessagesQuery request, CancellationToken cancellationToken)
{
    _logger.LogInformation("Экспорт всех сообщений для пользователя {UserId}", request.UserId);

    // Получаем все чаты, где пользователь является участником
    var userChatIds = await _context.ChatMembers
        .Where(cm => cm.UserId == request.UserId)
        .Select(cm => cm.ChatId)
        .Distinct()
        .ToListAsync(cancellationToken);

    // Получаем все сообщения из этих чатов
    var messages = await _context.Messages
        .Where(m => userChatIds.Contains(m.ChatId))
        .OrderBy(m => m.SentAt)
        .ToListAsync(cancellationToken);
```

**Как эксплуатировать:**
1. Злоумышленник может отправить запрос с `user_id` любого пользователя
2. Получить все личные сообщения жертвы
3. Получить все групповые чаты с участниками
4. Получить все вложения (file_id, preview_url)
5. Метаданные о прочтении сообщений

**Рекомендации по исправлению:**
```csharp
public async Task<GetUserAllMessagesResponse> Handle(GetUserAllMessagesQuery request, CancellationToken cancellationToken)
{
    // Проверка: пользователь может экспортировать только свои данные
    if (request.UserId != _userContext.UserId)
    {
        // Проверка на service token для административного доступа
        if (!_userContext.HasPolicy(nameof(TokenType.Service)))
        {
            _logger.LogWarning("Попытка экспорта данных пользователя {RequestedId} от {CurrentId}", 
                request.UserId, _userContext.UserId);
            throw new UnauthorizedAccessException();
        }
    }
    
    // ... остальной код
}
```

---

### 2. Отсутствие валидации текста сообщения — XSS/Injection риски
| Параметр | Значение |
|----------|----------|
| **Файл** | `Features/SendMessage/SendMessageCommandHandler.cs` |
| **Метод** | `Handle(SendMessageCommand request, ...)` |
| **Уровень** | 🔴 Критический |
| **CWE** | CWE-79: XSS / CWE-20: Improper Input Validation |

**Описание проблемы:**
```csharp
var message = new Message
{
    ChatId = chatId.Value, 
    Content = new MessageContent()
    {
        Attachments = attachments, 
        Text = request.Message.Text  // Нет валидации!
    },
    // ...
};
```

**Как эксплуатировать:**
```
# Отправка XSS payload
sendMessage(text="<script>alert(document.cookie)</script>")

# Отправка очень длинного сообщения (DoS)
sendMessage(text="A" * 10000000)

# Injection через специальные символы
sendMessage(text="\u0000\u0001\u0002...")
```

**Рекомендации по исправлению:**
```csharp
private void ValidateMessageText(string text)
{
    if (string.IsNullOrEmpty(text))
        return; // Пустые сообщения могут быть разрешены
    
    if (text.Length > 4000)
        throw new MessageTooLongException("Максимум 4000 символов");
    
    // Проверка на control characters
    if (text.Any(c => char.IsControl(c) && c != '\n' && c != '\r' && c != '\t'))
        throw new InvalidMessageException("Недопустимые символы");
}
```

---

### 3. SSRF через file_ids в сообщениях
| Параметр | Значение |
|----------|----------|
| **Файл** | `Features/SendMessage/SendMessageCommandHandler.cs` |
| **Метод** | `Handle(SendMessageCommand request, ...)` |
| **Уровень** | 🔴 Критический |
| **CWE** | CWE-918: Server-Side Request Forgery (SSRF) |

**Описание проблемы:**
```csharp
var filesInfo = await _filesServerApiClient.GetFilesDataAsync(
    new GetFilesDataRequest { FileIds = { request.Message.FileIds.Select(x => x.ToString())}});

if (filesInfo.FilesInfos.Any(x => !_attachmentMap.ContainsKey(x.Type)))
{
    throw new FileNotSupportedException();
}
```

**Как эксплуатировать:**
1. Если Files сервис имеет уязвимости, возможна загрузка вредоносного контента
2. SSRF через URL в превью файлов
3. Отображение вредоносного контента другим пользователям

**Рекомендации по исправлению:**
- Строгая валидация типов файлов на стороне Files сервиса
- Проверка MIME-типов содержимого
- Ограничение размеров файлов

---

## Высокие уязвимости (High)

### 4. Отсутствие Rate Limiting на отправку сообщений
| Параметр | Значение |
|----------|----------|
| **Файл** | `Features/SendMessage/SendMessageCommandHandler.cs` |
| **Метод** | `Handle(SendMessageCommand request, ...)` |
| **Уровень** | 🟠 Высокий |
| **CWE** | CWE-770: Allocation of Resources Without Limits |

**Как эксплуатировать:**
1. Спам в чатах
2. DoS атака на сервис и базу данных
3. Флуд сообщений через MassTransit очередь

**Рекомендации по исправлению:**
```csharp
// Добавить rate limiting middleware
app.UseRateLimiter(new RateLimiterOptions
{
    FixedWindow = new FixedWindowRateLimiterOptions
    {
        PermitLimit = 10, // 10 сообщений
        Window = TimeSpan.FromMinutes(1) // в минуту
    }
});
```

---

### 5. Утечка информации через ListChats
| Параметр | Значение |
|----------|----------|
| **Файл** | `Features/ListChats/ListChatsCommandHandler.cs` |
| **Метод** | `Handle(ListChatsCommand request, ...)` |
| **Уровень** | 🟠 Высокий |
| **CWE** | CWE-200: Information Exposure |

**Описание проблемы:**
```csharp
private async Task LoadNameAndImageChat(Chat chat)
{
    var memberId = chat.Members[0].UserId == _userContext.UserId 
        ? chat.Members[1].UserId 
        : chat.Members[0].UserId;

    var userInfo = await _usersServerApiClient.GetByIdAsync(
        new GetByIdRequest() { UserId = memberId });

    chat.Title = $"{userInfo.User.FirstName} {userInfo.User.LastName}";
    chat.Picture = userInfo.User.ProfilePicture;
}
```

**Рекомендации:**
- Убедиться, что Users API также проверяет права доступа
- Кэшировать данные пользователей для предотвращения избыточных запросов

---

### 6. Потенциальная уязвимость в GetPersonChatId
| Параметр | Значение |
|----------|----------|
| **Файл** | `Features/GetPersonChatId/GetPersonChatIdCommandHandler.cs` |
| **Метод** | `Handle(GetPersonChatIdCommand request, ...)` |
| **Уровень** | 🟠 Высокий |
| **CWE** | CWE-770: Allocation of Resources Without Limits |

**Описание проблемы:**
- Создание личных чатов с любым пользователем без ограничений
- Нет проверки на заблокированных пользователей

**Рекомендации по исправлению:**
```csharp
// Проверять, не заблокирован ли целевой пользователь
var isBlocked = await _usersStorage.IsBlocked(request.UserId, _userContext.UserId);
if (isBlocked)
{
    throw new UserBlockedException();
}
```

---

### 7. Недостаточная проверка прав при KickUser
| Параметр | Значение |
|----------|----------|
| **Файл** | `Features/KickUser/KickUserCommandHandler.cs` |
| **Метод** | `Handle(KickUserCommand request, ...)` |
| **Уровень** | 🟠 Высокий |
| **CWE** | CWE-639: Authorization Bypass |

**Описание проблемы:**
```csharp
if (!groupChatInfo.UsersCanKick.Contains(_userContext.UserId))
{
    throw new NoPermissionException();
}

// Нет проверки что creator не исключается!
await _chatsStorage.RemoveChatMember(request.ChatId, chatMember.UserId);
```

**Как эксплуатировать:**
1. Исключение создателя чата если его ID добавлен в UsersCanKick
2. Исключение самого себя (может вызвать ошибки в логике чата)

**Рекомендации по исправлению:**
```csharp
// Запретить исключение создателя чата
if (chatMember.UserId == groupChatInfo.CreatorId)
{
    throw new CannotKickCreatorException();
}

// Запретить исключение самого себя
if (chatMember.UserId == _userContext.UserId)
{
    throw new CannotKickSelfException();
}
```

---

### 8. Отсутствие проверки chat_id на принадлежность пользователю
| Параметр | Значение |
|----------|----------|
| **Файл** | `Features/GetChatInfo/GetChatInfoCommandHandler.cs` |
| **Метод** | `Handle(GetChatInfoCommand request, ...)` |
| **Уровень** | 🟠 Высокий |
| **CWE** | CWE-639: Authorization Bypass |

**Описание проблемы:**
```csharp
public async Task<bool> CheckAccessToChat(Guid chatId, long userId)
{
    var chat = await _context.Chats.Include(x=> x.Members)
        .FirstOrDefaultAsync(x => x.Id == chatId);

    if (chat is null)
    {
        return false;  // Не различает "нет доступа" и "не существует"
    }

    return chat.Members.Any(x => x.UserId == userId);
}
```

**Рекомендации:**
- Возвращать разные ошибки для "чат не существует" и "нет доступа"
- Не раскрывать информацию о существовании приватных чатов

---

### 9. SQL Injection риск через ExecuteSqlRawAsync
| Параметр | Значение |
|----------|----------|
| **Файл** | `Persistence/Services/MessagesStorage.cs` |
| **Метод** | `GetMessagesByChatId(...)` |
| **Уровень** | 🟠 Высокий |
| **CWE** | CWE-89: SQL Injection |

**Описание проблемы:**
```csharp
string attachmentTypeFilter;
if (attachmentType.HasValue && attachmentType.Value != Domain.MessageAttachmentType.Unknown)
{
    var typeValue = (int)attachmentType.Value;
    attachmentTypeFilter = "AND a.\"Type\" = " + typeValue;  // Потенциальный риск!
}
```

**Рекомендации по исправлению:**
- Использовать параметризованные запросы везде
- Избегать конкатенации SQL даже с "безопасными" значениями

---

### 10. Массовое раскрытие участников чата через ListChatMembers
| Параметр | Значение |
|----------|----------|
| **Файл** | `Features/ListChatMembers/ListChatMembersCommandHandler.cs` |
| **Метод** | `Handle(ListChatMembersCommand request, ...)` |
| **Уровень** | 🟠 Высокий |
| **CWE** | CWE-200: Information Exposure |

**Описание проблемы:**
```csharp
var usersResponse = await _usersServerApiClient.ListByIdsAsync(
    new ListByIdsRequest { Ids = { members.Select(x => x.UserId).ToList() } });
```

**Рекомендации:**
- Ограничить количество возвращаемых участников за запрос
- Проверять настройки приватности пользователей

---

## Средние уязвимости (Medium)

### 11. Отсутствие проверки размера сообщения в ListMessages
| Параметр | Значение |
|----------|----------|
| **Файл** | `Features/ListMessages/ListMessagesCommandHandler.cs` |
| **Уровень** | 🟡 Средний |

**Рекомендации:**
- Ограничить общее количество возвращаемых сообщений (offsetBefore + offsetAfter + 1)

---

### 12. Нет валидации Count в ListChatMembers
| Параметр | Значение |
|----------|----------|
| **Файл** | `Features/ListChatMembers/ListChatMembersCommandHandler.cs` |
| **Уровень** | 🟡 Средний |

**Рекомендации:**
- Добавить максимальное ограничение (например, 100 участников за запрос)

---

### 13. Игнорирование ошибок кэша без логирования
| Параметр | Значение |
|----------|----------|
| **Файл** | `Persistence/Services/ChatCache.cs` |
| **Уровень** | 🟡 Средний |

**Описание проблемы:**
```csharp
catch (Exception e)
{
    // Пустой catch - ошибка теряется!
}
```

**Рекомендации:**
- Логировать ошибки кэша
- Рассмотреть возможность использования fallback механизма

---

### 14. Отсутствие проверки на дублирование в ReadBy
| Параметр | Значение |
|----------|----------|
| **Файл** | `Persistence/Services/MessagesStorage.cs` |
| **Уровень** | 🟡 Средний |

**Рекомендации:**
- Использовать HashSet вместо List для ReadBy

---

## Сводная таблица уязвимостей

| # | Уязвимость | Уровень | Файл |
|---|------------|---------|------|
| 1 | IDOR - Экспорт данных | 🔴 Critical | Features/ExportData/GetUserAllMessagesQueryHandler.cs |
| 2 | Отсутствие валидации текста | 🔴 Critical | Features/SendMessage/SendMessageCommandHandler.cs |
| 3 | SSRF через file_ids | 🔴 Critical | Features/SendMessage/SendMessageCommandHandler.cs |
| 4 | Отсутствие Rate Limiting | 🟠 High | Features/SendMessage/SendMessageCommandHandler.cs |
| 5 | Утечка информации через ListChats | 🟠 High | Features/ListChats/ListChatsCommandHandler.cs |
| 6 | Потенциальная уязвимость в GetPersonChatId | 🟠 High | Features/GetPersonChatId/GetPersonChatIdCommandHandler.cs |
| 7 | Недостаточная проверка прав при KickUser | 🟠 High | Features/KickUser/KickUserCommandHandler.cs |
| 8 | Отсутствие проверки chat_id | 🟠 High | Features/GetChatInfo/GetChatInfoCommandHandler.cs |
| 9 | SQL Injection риск | 🟠 High | Persistence/Services/MessagesStorage.cs |
| 10 | Массовое раскрытие участников | 🟠 High | Features/ListChatMembers/ListChatMembersCommandHandler.cs |
| 11 | Отсутствие проверки размера | 🟡 Medium | Features/ListMessages/ListMessagesCommandHandler.cs |
| 12 | Нет валидации Count | 🟡 Medium | Features/ListChatMembers/ListChatMembersCommandHandler.cs |
| 13 | Игнорирование ошибок кэша | 🟡 Medium | Persistence/Services/ChatCache.cs |
| 14 | Отсутствие проверки на дублирование | 🟡 Medium | Persistence/Services/MessagesStorage.cs |

---

## Приоритетные рекомендации по исправлению

### Немедленно (Critical):
1. ✅ Исправить IDOR в ExportData
2. ✅ Добавить валидацию текста сообщения
3. ✅ Проверить валидацию файлов в Files сервисе

### Высокий приоритет:
4. ✅ Добавить rate limiting на отправку сообщений
5. ✅ Исправить проверку прав в KickUser
6. ✅ Добавить проверку членства в чате
7. ✅ Исправить SQL injection риск

### Средний приоритет:
8. Добавить лимиты на количество записей
9. Улучшить обработку ошибок кэша
10. Использовать HashSet для ReadBy

---

## Статус Исправления

| Уязвимость | Статус | Дата Исправления | Примечания |
|------------|--------|------------------|------------|
| 1. IDOR ExportData | ⏳ Ожидает | - | - |
| 2. Валидация текста | ⏳ Ожидает | - | - |
| 3. SSRF | ⏳ Ожидает | - | Зависит от Files |
| 4. Rate Limiting | ⏳ Ожидает | - | Требуется middleware |
| 5. Утечка ListChats | ⏳ Ожидает | - | - |
| 6. GetPersonChatId | ⏳ Ожидает | - | - |
| 7. KickUser | ⏳ Ожидает | - | - |
| 8. Проверка chat_id | ⏳ Ожидает | - | - |
| 9. SQL Injection | ⏳ Ожидает | - | - |
| 10. Участники чата | ⏳ Ожидает | - | - |

---

## Контакты

По вопросам безопасности обращайтесь: security@barkfluff.com
