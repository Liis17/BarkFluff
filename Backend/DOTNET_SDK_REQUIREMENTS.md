# BarkFluff Backend — .NET SDK для сборки на Linux

## Требуемые версии SDK

Для сборки **всего** бекенда на Linux необходимо установить:

| SDK | Статус |
|-----|--------|
| **.NET 9.0 SDK** | Обязателен — большинство сервисов |
| **.NET 10.0 SDK** | Обязателен — 3 сервиса + 1 Shared-библиотека |

---

## Сервисы по версиям

### .NET 9.0 (`net9.0`)

| Проект | Путь |
|--------|------|
| BarkFluff.Beacon | `Backend/BarkFluff.Beacon/` |
| BarkFluff.ClientStorage | `Backend/BarkFluff.ClientStorage/` |
| BarkFluff.Configuration | `Backend/BarkFluff.Configuration/` |
| BarkFluff.FastAuth | `Backend/BarkFluff.FastAuth/` |
| BarkFluff.Files | `Backend/BarkFluff.Files/` |
| BarkFluff.GrpcServer | `Backend/BarkFluff.GrpcServer/` |
| BarkFluff.Identity | `Backend/BarkFluff.Identity/` |
| BarkFluff.Messages | `Backend/BarkFluff.Messages/` |
| BarkFluff.Navigator | `Backend/BarkFluff.Navigator/` |
| BarkFluff.Notification | `Backend/BarkFluff.Notification/` |
| BarkFluff.Updates | `Backend/BarkFluff.Updates/` |
| BarkFluff.Users | `Backend/BarkFluff.Users/` |
| BarkFluff.Web | `Backend/BarkFluff.Web/` |
| Barkfluff.CloudMessaging | `Backend/Barkfluff.CloudMessaging/` |
| Barkfluff.Developers | `Backend/Barkfluff.Developers/` |

### .NET 10.0 (`net10.0`)

| Проект | Путь |
|--------|------|
| BarkFluff.Onliner | `Backend/BarkFluff.Onliner/` |
| Barkfluff.AdminPanel | `Backend/Barkfluff.AdminPanel/` |
| Barkfluff.WebServer | `Backend/Barkfluff.WebServer/` |

---

## Shared-библиотеки по версиям

### .NET 9.0

| Библиотека | Путь |
|------------|------|
| BarkFluff.Proto | `Shared/BarkFluff.Proto/` |
| BarkFluff.Shared.Auth | `Shared/BarkFluff.Shared.Auth/` |
| BarkFluff.Shared.Exceptions | `Shared/BarkFluff.Shared.Exceptions/` |
| BarkFluff.Shared.Identity | `Shared/BarkFluff.Shared.Identity/` |
| BarkFluff.Shared.Queue | `Shared/BarkFluff.Shared.Queue/` |

### .NET 10.0

| Библиотека | Путь |
|------------|------|
| BarkFluff.Shared.SecurityUtilities | `Shared/BarkFluff.Shared.SecurityUtilities/` |

---

## Установка на Linux (Ubuntu/Debian)

### Вариант 1 — через Microsoft package feed

```bash
# Добавить Microsoft feed
wget https://packages.microsoft.com/config/ubuntu/$(lsb_release -rs)/packages-microsoft-prod.deb -O packages-microsoft-prod.deb
sudo dpkg -i packages-microsoft-prod.deb
rm packages-microsoft-prod.deb

sudo apt-get update

# .NET 9 SDK
sudo apt-get install -y dotnet-sdk-9.0

# .NET 10 SDK
sudo apt-get install -y dotnet-sdk-10.0
```

### Вариант 2 — через snap

```bash
sudo snap install dotnet-sdk --classic --channel=9.0
sudo snap install dotnet-sdk --classic --channel=10.0
```

### Проверка установки

```bash
dotnet --list-sdks
# Ожидаемый вывод:
# 9.0.xxx [/usr/lib/dotnet/sdk]
# 10.0.xxx [/usr/lib/dotnet/sdk]
```

---

## Примечание

В CI/CD (`.github/workflows/build-backend-*.yml`) первая задача каждого workflow
(`check-dotnet`) автоматически проверяет наличие нужного SDK на self-hosted раннере
и завершает сборку с уведомлением в Telegram, если SDK не установлен.

Сервисы, требующие **.NET 10.0** (Onliner, AdminPanel, WebServer), проверяют наличие
`10.x`, все остальные — наличие `9.x`.
