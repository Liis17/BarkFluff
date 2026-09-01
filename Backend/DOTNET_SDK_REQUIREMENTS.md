# BarkFluff Backend — .NET SDK для сборки на Linux

## Требуемые версии SDK

Для сборки **всего** бекенда на Linux необходимо установить:

| SDK | Статус |
|-----|--------|
| **.NET 10.0 SDK** | Обязателен — все сервисы и Shared-библиотеки |

---

## Сервисы

Все backend-проекты собираются на `net10.0`:

| Проект | Путь |
|--------|------|
| BarkFluff.Beacon | `Backend/BarkFluff.Beacon/` |
| BarkFluff.ClientStorage | `Backend/BarkFluff.ClientStorage/` |
| BarkFluff.Settings | `Backend/BarkFluff.Settings/` |
| BarkFluff.Setup | `Backend/BarkFluff.Setup/` |
| BarkFluff.FastAuth | `Backend/BarkFluff.FastAuth/` |
| BarkFluff.Files | `Backend/BarkFluff.Files/` |
| BarkFluff.GrpcServer | `Backend/BarkFluff.GrpcServer/` |
| BarkFluff.Identity | `Backend/BarkFluff.Identity/` |
| BarkFluff.Messages | `Backend/BarkFluff.Messages/` |
| BarkFluff.Navigator | `Backend/BarkFluff.Navigator/` |
| BarkFluff.Notification | `Backend/BarkFluff.Notification/` |
| BarkFluff.Onliner | `Backend/BarkFluff.Onliner/` |
| BarkFluff.Updates | `Backend/BarkFluff.Updates/` |
| BarkFluff.Users | `Backend/BarkFluff.Users/` |
| BarkFluff.Web | `Backend/BarkFluff.Web/` |
| Barkfluff.AdminPanel | `Backend/Barkfluff.AdminPanel/` |
| Barkfluff.CloudMessaging | `Backend/Barkfluff.CloudMessaging/` |
| Barkfluff.Developers | `Backend/Barkfluff.Developers/` |
| Barkfluff.WebServer | `Backend/Barkfluff.WebServer/` |

---

## Shared-библиотеки

Все Shared-библиотеки также собираются на `net10.0`:

| Библиотека | Путь |
|------------|------|
| BarkFluff.Proto | `Shared/BarkFluff.Proto/` |
| BarkFluff.Shared.Auth | `Shared/BarkFluff.Shared.Auth/` |
| BarkFluff.Shared.Exceptions | `Shared/BarkFluff.Shared.Exceptions/` |
| BarkFluff.Shared.Identity | `Shared/BarkFluff.Shared.Identity/` |
| BarkFluff.Shared.Queue | `Shared/BarkFluff.Shared.Queue/` |
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

# .NET 10 SDK
sudo apt-get install -y dotnet-sdk-10.0
```

### Вариант 2 — через snap

```bash
sudo snap install dotnet-sdk --classic --channel=10.0
```

### Проверка установки

```bash
dotnet --list-sdks
# Ожидаемый вывод:
# 10.0.110 [/var/snap/dotnet/common/dotnet/sdk]
```

---

## Примечание

Корневой `global.json` закрепляет SDK `10.0.110` без автоматического скачивания и
без перехода на другую patch-версию. В CI/CD (`.github/workflows/build-backend-*.yml`)
задача `check-dotnet` проверяет наличие именно этой версии на self-hosted раннере и
завершает сборку с уведомлением в Telegram, если SDK не установлен.
