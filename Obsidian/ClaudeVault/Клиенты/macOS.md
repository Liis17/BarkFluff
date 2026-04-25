# BarkFluff macOS

SwiftUI приложение для macOS. gRPC через grpc-swift.

> UI/UX спецификация всех экранов и сценариев: [[Клиенты/DesignDocument]] (источник: `dd.md`)

Расположение: `Mac/Barkfluff/`
Swift 6.2, platforms: macOS 26, iOS 26.

## Зависимости

- `grpc-swift 2.0.0+`
- `swift-protobuf 1.28.0+`
- `KeychainAccess 4.2.2+` — токены
- `Nuke` — загрузка изображений

## Архитектура (MVVM + Coordinator)

### Локальные пакеты (`Packages/`)

```
BFProto/       # Proto definitions и gRPC код
BFNetworking/  # gRPC клиент, соединения, репозитории
BFCore/        # Бизнес-логика: сервисы, модели, кеш
```

Зависимость: `BFProto ← BFNetworking ← BFCore ← Main App`

### Структура приложения

```
Barkfluff/
├── App/
│   ├── BarkfluffApp.swift      # @main, lifecycle
│   └── DI/DependencyContainer  # DI
├── Navigation/
│   ├── AppCoordinator.swift    # @Observable, состояния: loading → serverSelection → authentication → main
│   └── RootView.swift
├── Features/                   # MVVM per feature
│   ├── Auth/, ChatList/, Conversation/, GroupChat/
│   ├── Profile/, Settings/, UserSearch/, FastAuth/
└── DesignSystem/
    ├── Components/
    └── LiquidGlass/            # macOS 26 glass effects
```

## Navigator

- Адрес: `navigator.barkfluff.com:443` (TLS через nginx)
- Реализация: `BFNetworking/Repositories/NavigatorRepository.swift`
- Транспорт: `HTTP2ClientTransport.Posix`, `transportSecurity: .tls`
- DependencyContainer явно передаёт `host: "navigator.barkfluff.com", port: 443`

## Data Flow

Views → ViewModels → Services → Repositories → gRPC
- Caches: `UserCache`, `ChatCache` (in-memory)
- `TokenRefreshCoordinator` — автоматический рефреш через `AuthInterceptor`

## Liquid Glass (macOS 26 / iOS 26)

**ВАЖНО**: При работе с UI-эффектами читать `LiquidGlassGuide.md` в корне проекта.

Краткие правила:
- `.glassEffect(.regular, in: .capsule)` вместо `.ultraThinMaterial` для контролов
- `.glassEffect(.clear)` — для мелких контролов поверх медиа
- `.buttonStyle(.glass)` / `.buttonStyle(.glassProminent)` — для кнопок
- `GlassEffectContainer` — обязателен при нескольких glass-элементах рядом
- Glass — только для навигационного слоя, НИКОГДА для контента

## Code Conventions

- Комментарии на русском
- Services с `Protocol` суффиксом (`AuthServiceProtocol`)
- ViewModels — `@Observable` классы
- Async/await для всех асинхронных операций

## Сборка

```bash
xcodebuild -project Barkfluff.xcodeproj -scheme Barkfluff -configuration Debug build
open Barkfluff.xcodeproj
```
