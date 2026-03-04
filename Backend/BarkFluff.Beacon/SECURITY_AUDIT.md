# Аудит Безопасности: BarkFluff.Beacon / Onliner / Updates / Navigator

**Дата аудита:** 4 марта 2026 г.  
**Аудитор:** Security Assessment Team

---

## 1. BarkFluff.Beacon

**Статус:** 🔴 Требует исправлений

### Уязвимости

| # | Уязвимость | Уровень | Файл |
|---|------------|---------|------|
| 1 | Отсутствие авторизации | 🔴 High | `Host/BeaconApiService.cs` |
| 2 | Утечка конфигураций | 🔴 High | `Features/GetServerInfo/GetServerInfoCommandHandler.cs` |
| 3 | IDOR | 🟠 High | `Features/GetServerInfo/...` |
| 4 | Отсутствие аудита | 🟡 Medium | Все файлы |

### Описание проблем

**Отсутствие авторизации:**
```csharp
// Program.cs - нет UseXAuth()
public class BeaconApiService : BeaconApi.BeaconApiBase
{
    // Нет [Authorize] атрибута
    public override Task<GetServerInfoResponse> GetServerInfo(...)
    {
        // Любой может получить информацию о сервере
    }
}
```

**Утечка конфигураций:**
- Возвращает конфигурации всех сервисов (Identity, Users, Files, Messages, Updates, Onliner)
- Раскрывает хосты и порты внутренних сервисов

### Рекомендации

1. Добавить `[Authorize(Policy = nameof(TokenType.Service))]`
2. Ограничить возврат конфигураций только необходимыми данными
3. Добавить service token для регистрации в Navigator
4. Ввести rate limiting

---

## 2. BarkFluff.Onliner

**Статус:** 🟠 Требует улучшений

### Уязвимости

| # | Уязвимость | Уровень | Файл |
|---|------------|---------|------|
| 1 | IDOR (GetOnlineStatus) | 🟠 High | `Features/GetOnlineStatus/GetOnlineStatusQueryHandler.cs` |
| 2 | IDOR (SubscribeToOnlineStatus) | 🟠 High | `Features/SubscribeToOnlineStatus/...` |
| 3 | Отсутствие приватности | 🟠 High | Все файлы |
| 4 | Rate limiting | 🟡 Medium | `Host/OnlinerApiService.cs` |

### Описание проблем

**IDOR GetOnlineStatus:**
```csharp
// Можно запросить статус любого пользователя по ID
foreach (var userId in request.UserIds)
{
    var status = await GetUserStatusAsync(userId, cancellationToken);
    // Нет проверки прав!
}
```

**IDOR SubscribeToOnlineStatus:**
```csharp
// Можно подписаться на статус любого пользователя
var connectionId = _subscriptionsManager.RegisterSubscription(
    userId,
    request.UserIds,  // Нет проверки прав на подписку!
    request.ResponseStream);
```

### Рекомендации

1. Добавить проверку "друзей" или контактов перед возвратом статуса
2. Реализовать настройки приватности (кто может видеть мой статус)
3. Добавить лимит на количество отслеживаемых пользователей
4. Rate limiting для SetOnlineStatus

---

## 3. BarkFluff.Updates

**Статус:** 🔴 Критические уязвимости

### Уязвимости

| # | Уязвимость | Уровень | Файл |
|---|------------|---------|------|
| 1 | IDOR (подписка на чаты) | 🔴 Critical | `Features/SubscribeNewMessages/StreamSubscriptionsManager.cs` |
| 2 | Утечка сообщений | 🔴 Critical | `Features/SubscribeNewMessages/Handlers/NewMessageNotificationHandler.cs` |
| 3 | Отсутствие шифрования | 🟠 High | Все файлы |
| 4 | Нет аудита доступа | 🟡 Medium | Все файлы |

### Описание проблем

**IDOR подписка на чаты:**
```csharp
// Нет проверки прав на подписку!
public Guid RegisterSubscription(long userId, IServerStreamWriter<NewMessageEvent> responseStream)
{
    // Просто регистрирует подписку без проверки членства в чате
}
```

**Утечка сообщений:**
```csharp
// Отправляет сообщения всем из notification.Members без дополнительной проверки
foreach (var memberId in notification.Members)
{
    var streams = _subscriptionsManager.GetUserStreams(memberId);
    foreach (var stream in streams)
    {
        await stream.WriteAsync(newMessageEvent, cancellationToken);
    }
}
```

### Рекомендации

**КРИТИЧНО:**
1. Добавить проверку членства в чате перед подпиской
2. Валидировать ChatId и membership при каждой операции
3. Добавить end-to-end шифрование для чувствительных сообщений
4. Аудит всех операций чтения сообщений

---

## 4. BarkFluff.Navigator

**Статус:** 🟠 Требует улучшений

### Уязвимости

| # | Уязвимость | Уровень | Файл |
|---|------------|---------|------|
| 1 | Отсутствие авторизации | 🟠 High | `Host/NavigatorApiService.cs` |
| 2 | Server Spoofing | 🟠 High | `Features/RegisterServer/RegisterServerCommandHandler.cs` |
| 3 | Information Disclosure | 🟡 Medium | `Features/ListServers/ListServersQueryHandler.cs` |
| 4 | Нет верификации Beacon | 🟡 Medium | `Persistence/ServersStorage.cs` |

### Описание проблем

**Отсутствие авторизации:**
```csharp
// Нет [Authorize] атрибута
public class NavigatorApiService : NavigatorApi.NavigatorApiBase
{
    public override async Task<RegisterServerResponse> RegisterServer(...)
    {
        // Любой может зарегистрировать сервер
        var domainServer = new ServerInfo
        {
            AddedBy = _userContext.IsAuthenticated ? _userContext.UserId.ToString() : "Anonymous"
        };
    }
}
```

**Server Spoofing:**
- Можно зарегистрировать чужой Beacon
- Нет проверки что Beacon действительно принадлежит requester

### Рекомендации

1. Добавить `[Authorize(Policy = nameof(TokenType.Service))]` для RegisterServer
2. Реализовать challenge-response верификацию Beacon
3. Добавить проверку уникальности имени сервера
4. Ввести service token для регистрации
5. Аудит всех регистраций серверов

---

## Сводная таблица

| Сервис | Критические | Высокие | Средние | Оценка |
|--------|-------------|---------|---------|--------|
| Beacon | 0 | 2 | 2 | 🟠 |
| Onliner | 0 | 2 | 2 | 🟠 |
| Updates | 2 | 1 | 1 | 🔴 |
| Navigator | 0 | 2 | 2 | 🟠 |

---

## Приоритетные рекомендации

### Немедленного исправления:
1. **Updates:** Добавить проверку членства в чате
2. **Beacon:** Добавить авторизацию
3. **Onliner:** Добавить проверку приватности статусов
4. **Navigator:** Добавить авторизацию для регистрации серверов

---

## Контакты

security@barkfluff.com
