# Settings migration

В backend завершён переход с устаревшего Configuration на Settings.

## Что изменилось

- Settings работает на gRPC-порту `7003` и хранит параметры в базе `settings`.
- Новый Setup UI работает на HTTP-порту `7032` и запускается отдельным bootstrap-compose.
- Все новые compose-файлы используют `SETTINGS_SERVICE_URL` и имя сервиса `settings`.
- Первичная настройка выполняется последовательно по группам и после завершения
  блокируется отпечатком каталога полей.
- Старый проект Configuration, его тесты, образ, workflow и deployment-файлы удалены.
- `CONFIGURATION_SERVICE_URL` оставлен только в runtime как временный compatibility
  alias, чтобы старые образы могли обновляться поэтапно.

## Проверка

```bash
dotnet test Tests/BarkFluff.Settings.Tests/BarkFluff.Settings.Tests.csproj --no-restore
dotnet test Tests/BarkFluff.Setup.Tests/BarkFluff.Setup.Tests.csproj --no-restore
```

Инструкция для операторов: [`Docs/settings-setup.md`](../Docs/settings-setup.md).
