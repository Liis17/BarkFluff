# Аудит: BarkFluff.Navigator

> Дата: 2026-06-12. Область: код сервиса, Dockerfile, nginx, docker-compose.

## Сводка

Navigator — публичный реестр серверов BarkFluff (`navigator.barkfluff.com`): `RegisterServer` принимает саморегистрацию серверов, `ListServers` отдаёт каталог клиентам. Оба RPC доступны анонимно: для `ListServers` это by design, но анонимный `RegisterServer` позволяет любому зарегистрировать вредоносный «сервер» с произвольным BeaconHost — каталог, которому доверяют клиенты, спуфится без каких-либо препятствий. Вторая серьёзная проблема — JWT-секрет XAuth захардкожен в `appsettings.json` и закоммичен в репозиторий, при этом именно он используется в проде (Navigator не вызывает `LoadConfiguration`). Троттлинг регистраций обходится сменой имени, а внутреннее хранилище `_servers` никогда не очищается — анонимный клиент может неограниченно раздувать память процесса. nginx-конфига для navigator в `Backend/nginx/` нет — TLS-терминация живёт вне репозитория, а собственный compose публикует голый h2c-порт.

| Критичность | Количество |
| ----------- | ---------- |
| Critical    | 0          |
| High        | 3          |
| Medium      | 2          |
| Low         | 4          |

## Безопасность

### S1. Анонимная регистрация серверов — спуфинг каталога (фишинг клиентов) — High

**Файл:** `Backend/BarkFluff.Navigator/Host/NavigatorApiService.cs:31` (метод без `[Authorize]`), `Backend/BarkFluff.Navigator/Host/NavigatorApiService.cs:46` (`AddedBy = "Anonymous"`)
**Проблема:** `RegisterServer` не требует аутентификации — `AddedBy` явно допускает анонима. XAuth подключён (`Program.cs:33,41`), но ни на одном метод