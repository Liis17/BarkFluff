# План реализации: Система баджей для профилей пользователей

## Краткое описание задачи

Реализовать систему баджей для профилей пользователей на бэкенде с поддержкой gRPC API. Система должна поддерживать различные типы баджей (достижения, статусы ботов/разработчиков/тестовых аккаунтов), управление приоритетами отображения и лимиты показа баджей в разных контекстах.

## Текущая архитектура

- **Технологический стек**: .NET 9.0, Entity Framework Core, gRPC, PostgreSQL, Docker
- **Основные компоненты**:
  - Микросервис `BarkFluff.Users` - управление профилями пользователей
  - Существующая структура БД с таблицами `Users` и `UserContacts`
  - gRPC API через сервисы `UsersApi` и `UsersServerApi`
- **Паттерны и подходы**:
  - CQRS с MediatR для обработки команд и запросов
  - Clean Architecture с разделением на Domain, Features, Infrastructure, Persistence
  - Entity Framework Code First с миграциями
- **Внешние зависимости**:
  - PostgreSQL для хранения данных
  - gRPC Proto для межсервисного взаимодействия
  - MediatR для обработки запросов

## Предлагаемое решение

### Архитектурные изменения
- [ ] Добавление новых Domain моделей `Badge` и `UserBadge`
- [ ] Расширение существующего микросервиса `BarkFluff.Users`
- [ ] Создание новых gRPC методов для управления баджами
- [ ] Добавление миграций БД для создания таблиц баджей

### Интеграции и зависимости
- **База данных**: Новые таблицы `badges` и `user_badges` в PostgreSQL
- **gRPC контракты**: Расширение `users_api.proto` новыми сервисами и сообщениями
- **Авторизация**: Использование существующей системы JWT токенов для ограничения доступа

## Детальный план реализации

### Этап 1: Подготовка доменных моделей и БД
- [ ] Создать доменную модель `Badge` в `Backend/BarkFluff.Users/Domain/Badge.cs`
- [ ] Создать доменную модель `UserBadge` в `Backend/BarkFluff.Users/Domain/UserBadge.cs`
- [ ] Обновить `UsersContext.cs` для включения новых DbSet
- [ ] Создать миграцию БД для таблиц баджей
- [ ] Применить миграцию и проверить структуру БД

### Этап 2: Расширение gRPC контрактов
- [ ] Обновить `Shared/BarkFluff.Proto/users_api.proto` добавив новые message типы:
  - `Badge` message с полями id, name, image_url, description
  - `UserBadge` message с полями badge, priority, assigned_date
  - Request/Response типы для всех операций с баджами
- [ ] Добавить новые gRPC методы в service UsersApi:
  - `GetUserBadges(GetUserBadgesRequest) returns(GetUserBadgesResponse)`
- [ ] Добавить новые gRPC методы в service UsersServerApi:
  - `AssignUserBadge(AssignUserBadgeRequest) returns(AssignUserBadgeResponse)`
  - `RemoveUserBadge(RemoveUserBadgeRequest) returns(RemoveUserBadgeResponse)`
  - `UpdateUserBadgePriority(UpdateUserBadgePriorityRequest) returns(UpdateUserBadgePriorityResponse)`
  - `CreateBadge(CreateBadgeRequest) returns(CreateBadgeResponse)`
  - `GetAllBadges(GetAllBadgesRequest) returns(GetAllBadgesResponse)`

### Этап 3: Реализация Features (CQRS Commands/Queries)
- [ ] Создать `Backend/BarkFluff.Users/Features/Badges/GetUserBadges/GetUserBadgesQuery.cs`
- [ ] Создать `Backend/BarkFluff.Users/Features/Badges/GetUserBadges/GetUserBadgesQueryHandler.cs`
- [ ] Создать `Backend/BarkFluff.Users/Features/Badges/AssignUserBadge/AssignUserBadgeCommand.cs`
- [ ] Создать `Backend/BarkFluff.Users/Features/Badges/AssignUserBadge/AssignUserBadgeCommandHandler.cs`
- [ ] Создать `Backend/BarkFluff.Users/Features/Badges/RemoveUserBadge/RemoveUserBadgeCommand.cs`
- [ ] Создать `Backend/BarkFluff.Users/Features/Badges/RemoveUserBadge/RemoveUserBadgeCommandHandler.cs`
- [ ] Создать `Backend/BarkFluff.Users/Features/Badges/UpdateUserBadgePriority/UpdateUserBadgePriorityCommand.cs`
- [ ] Создать `Backend/BarkFluff.Users/Features/Badges/UpdateUserBadgePriority/UpdateUserBadgePriorityCommandHandler.cs`
- [ ] Создать `Backend/BarkFluff.Users/Features/Badges/CreateBadge/CreateBadgeCommand.cs`
- [ ] Создать `Backend/BarkFluff.Users/Features/Badges/CreateBadge/CreateBadgeCommandHandler.cs`
- [ ] Создать `Backend/BarkFluff.Users/Features/Badges/GetAllBadges/GetAllBadgesQuery.cs`
- [ ] Создать `Backend/BarkFluff.Users/Features/Badges/GetAllBadges/GetAllBadgesQueryHandler.cs`

### Этап 4: Обновление API сервисов
- [ ] Обновить `Backend/BarkFluff.Users/Host/UsersApiService.cs` добавив новый метод `GetUserBadges`
- [ ] Обновить `Backend/BarkFluff.Users/Host/UsersServerApiService.cs` добавив методы:
  - `AssignUserBadge`
  - `RemoveUserBadge`
  - `UpdateUserBadgePriority`
  - `CreateBadge`
  - `GetAllBadges`
- [ ] Добавить авторизационные политики для административных операций

### Этап 5: Обновление маппинга и расширений
- [ ] Обновить `Backend/BarkFluff.Users/Mapping/UserMapping.cs` для включения баджей в User entity
- [ ] Создать `Backend/BarkFluff.Users/Mapping/BadgeMapping.cs` для маппинга Badge entities в Proto messages
- [ ] Обновить метод `GetUser` для возврата пользователя с баджами согласно лимитам отображения

### Этап 6: Обновление хранилища данных
- [ ] Обновить `Backend/BarkFluff.Users/Persistence/Services/UsersStorage.cs` добавив методы для работы с баджами:
  - `GetUserBadgesAsync(long userId, int? limit = null)`
  - `AssignBadgeToUserAsync(long userId, int badgeId, int priority)`
  - `RemoveBadgeFromUserAsync(long userId, int badgeId)`
  - `UpdateUserBadgePriorityAsync(long userId, int badgeId, int newPriority)`
  - `CreateBadgeAsync(Badge badge)`
  - `GetAllBadgesAsync()`

### Этап 7: Тестирование и финализация
- [ ] Написать unit-тесты для всех новых команд и запросов
- [ ] Создать интеграционные тесты для gRPC методов
- [ ] Протестировать сценарии:
  - Назначение и снятие баджей
  - Обновление приоритетов баджей
  - Получение баджей с лимитами (1 для списков, 3 для профиля, все для детального просмотра)
  - Создание новых баджей администратором
- [ ] Обновить документацию gRPC API

## Технические уточнения

- **Изображения баджей**: Будут храниться через сервис Files как новый тип файлов
- **Приоритеты**: Базовое значение 1000, меньшие числа = выше приоритет
- **Кэширование**: Логика кэширования будет реализована на стороне клиента
- **Автоматическое назначение**: На данном этапе не планируется
- **Управление баджами**: Пока без специальных ролей администраторов

## Риски и ограничения

- **Технические риски**:
  - Необходимость координации с другими микросервисами при отображении баджей в сообщениях
  - Производительность запросов при получении пользователей с баджами в больших списках
  - Размер gRPC сообщений при передаче пользователей с множеством баджей

- **Ресурсные ограничения**:
  - Увеличение размера базы данных при активном использовании баджей
  - Дополнительная нагрузка на сеть при передаче данных о баджах

## Критерии готовности

- [ ] Все gRPC методы корректно обрабатывают запросы и возвращают баджи с правильной сортировкой по приоритету
- [ ] Система поддерживает лимиты отображения (1 бадж в списках, 3 в профиле, все при детальном просмотре)
- [ ] Авторизация корректно ограничивает доступ к административным функциям
- [ ] Все unit и интеграционные тесты проходят успешно
- [ ] Документация gRPC API обновлена с примерами использования
- [ ] Миграции БД применяются без ошибок
- [ ] Производительность системы не деградирует при работе с баджами

## Технические детали реализации

### Структура таблицы badges
```sql
CREATE TABLE badges (
    id SERIAL PRIMARY KEY,
    name VARCHAR(100) NOT NULL,
    description TEXT,
    image_url VARCHAR(255) NOT NULL,
    created_date TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
    is_active BOOLEAN DEFAULT TRUE
);
```

### Структура таблицы user_badges
```sql
CREATE TABLE user_badges (
    id SERIAL PRIMARY KEY,
    user_id BIGINT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    badge_id INTEGER NOT NULL REFERENCES badges(id) ON DELETE CASCADE,
    priority INTEGER NOT NULL DEFAULT 1000,
    assigned_date TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
    UNIQUE(user_id, badge_id)
);
```

### Приоритеты баджей
- Базовое значение: 1000
- Меньшие числа = выше приоритет (например, 100 выше чем 1000)
- Большие числа = ниже приоритет (например, 2000 ниже чем 1000)
- Сортировка: ORDER BY priority ASC, assigned_date ASC