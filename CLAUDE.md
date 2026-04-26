# BarkFluff

Распределённая платформа обмена сообщениями в реальном времени (.NET 9 ( местами уже 10), gRPC, RabbitMQ).

## База знаний — Obsidian

**Всегда** обращайся к хранилищу Obsidian за контекстом по проекту, описанием сервисов, паттернами и функциями. Читай только нужный файл, не весь Index.

| Файл                                          | Содержимое                                            |
| --------------------------------------------- | ----------------------------------------------------- |
| `Obsidian/ClaudeVault/Index.md`               | Оглавление, ссылки на все разделы                     |
| `Obsidian/ClaudeVault/Архитектура.md`         | Tech stack, порты, XAuth, CQRS, gRPC-клиент, RabbitMQ |
| `Obsidian/ClaudeVault/Backend/{Сервис}.md`    | Документация по каждому микросервису                  |
| `Obsidian/ClaudeVault/Shared/{Библиотека}.md` | Shared-библиотеки (Proto, Auth, Exceptions, ...)      |
| `Obsidian/ClaudeVault/Клиенты/{Платформа}.md` | Android, Windows, macOS, iOS, Linux                   |

## Правила обновления Obsidian

При изменении архитектуры, добавлении функций или сервисов — обновляй соответствующий файл в хранилище.

- Backend-сервис → `Backend/{ServiceName}.md`
- Shared-библиотека → `Shared/{Name}.md`
- Клиент → `Клиенты/{Platform}.md`
- Новый паттерн → `Архитектура.md`
- Новый сервис → создать файл + добавить ссылку в `Index.md`

Используй `[[WikiLinks]]` для перекрёстных ссылок между файлами Obsidian.

## Правила чтения файлов проекта

- **Исследование кодовой базы** (поиск файлов, понимание структуры, анализ зависимостей) — используй суб-агент типа `Explore` с параметром `model: "haiku"`. Это экономит контекст основной сессии.
- **Редактирование файлов** — читай сам напрямую через `Read`, суб-агент не нужен.
- **Файлы Obsidian** (`Obsidian/ClaudeVault/**/*.md`) — читай сам напрямую через `Read`, без суб-агента.

## Быстрые команды

### Backend (Docker)

```bash
cd Backend
docker-compose -f docker-compose-dev.yml up -d
docker-compose -f docker-compose-dev.yml ps
docker-compose -f docker-compose-dev.yml down
```

### Сборка сервиса

```bash
dotnet build Backend/BarkFluff.{Service}/BarkFluff.{Service}.csproj
```

### Android

```bash
cd Android/Barkfluff.Client.Android
./gradlew assembleDebug
```

### Windows (WPF)

```bash
dotnet build Windows/BarkFluff.Client.WPF/BarkFluff.Client.WPF.csproj
```
