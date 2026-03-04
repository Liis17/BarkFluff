# Аудит Безопасности: BarkFluff.Configuration

**Дата аудита:** 4 марта 2026 г.  
**Аудитор:** Security Assessment Team  
**Статус:** 🔴 КРИТИЧЕСКОЕ СОСТОЯНИЕ

---

## Резюме

Сервис BarkFluff.Configuration находится в **критическом состоянии**. Все конфигурации всех сервисов доступны **без какой-либо авторизации**. Требуется **немедленное отключение** от продакшена до исправления.

---

## Критические уязвимости

### 1. Отсутствие авторизации
| Файл | `Host/ConfigurationApiService.cs` |
|------|-----------------------------------|
| **Метод** | `GetConfiguration(GetConfigurationRequest request, ...)` |
| **Уровень** | 🔴 Критический |
| **CWE** | CWE-306: Missing Authentication for Critical Function |

**Описание:**
```csharp
public class ConfigurationApiService : ConfigurationApi.ConfigurationApiBase
{
    // Нет [Authorize] атрибута!
    public override Task<GetConfigurationResponse> GetConfiguration(...)
    {
        // Любой может получить конфигурацию любого сервиса
    }
}
```

**Эксплуатация:**
- Получение connection strings к БД
- Получение паролей и токенов
- Получение внутренней структуры сервисов

**Исправление:**
```csharp
[Authorize(Policy = nameof(TokenType.Service))]
public override Task<GetConfigurationResponse> GetConfiguration(...)
```

---

### 2. IDOR - Доступ к конфигурации любого сервиса
| Файл | `Infrastructure/ConfigurationStorage.cs` |
|------|------------------------------------------|
| **Метод** | `GetConfiguration(ServiceId serviceId)` |
| **Уровень** | 🔴 Критический |
| **CWE** | CWE-639: Authorization Bypass |

**Описание:**
```csharp
public async Task<List<ConfigurationItem>> GetConfiguration(ServiceId serviceId)
{
    // Нет проверки прав доступа!
    return await _context.Configurations
        .Where(x => x.ServiceId == serviceId)
        .ToListAsync();
}
```

**Исправление:**
- Проверять что requester имеет право на доступ к этой конфигурации
- Реализовать RBAC

---

### 3. Утечка секретов
| Файл | `Domain/ConfigurationItem.cs` |
|------|-------------------------------|
| **Уровень** | 🔴 Критический |
| **CWE** | CWE-311: Missing Encryption of Sensitive Data |

**Описание:**
- Пароли БД хранятся в открытом виде
- Токены сервисов хранятся в открытом виде
- JWT секреты хранятся в открытом виде

**Исправление:**
- Шифровать чувствительные значения в БД
- Использовать HashiCorp Vault или аналог

---

### 4. Отсутствие аудита изменений
| Файл | Все файлы |
|------|----------|
| **Уровень** | 🟠 Высокий |
| **CWE** | CWE-778: Insufficient Logging |

**Исправление:**
- Логировать все изменения конфигураций
- Сохранять кто и когда изменил

---

## Сводная таблица

| # | Уязвимость | Уровень | Статус |
|---|------------|---------|--------|
| 1 | Отсутствие авторизации | 🔴 Critical | ⏳ Ожидает |
| 2 | IDOR | 🔴 Critical | ⏳ Ожидает |
| 3 | Утечка секретов | 🔴 Critical | ⏳ Ожидает |
| 4 | Отсутствие аудита | 🟠 High | ⏳ Ожидает |

---

## Рекомендации

**НЕМЕДЛЕННО:**
1. Отключить сервис от продакшена
2. Добавить авторизацию для всех методов
3. Реализовать RBAC
4. Зашифровать чувствительные данные

---

## Контакты

security@barkfluff.com
