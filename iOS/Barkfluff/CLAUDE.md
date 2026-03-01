# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

BarkFluff iOS — это приложение для обмена сообщениями на SwiftUI, использующее gRPC для связи с бэкендом. Это iOS-версия macOS-клиента.

## Reference: macOS Client

**ВАЖНО:** При разработке iOS-версии ВСЕГДА смотри на macOS-клиент как на референс:

```
/Users/fooxboy/RiderProjects/BarkFluff/Mac/Barkfluff/
```

Используй те же:
- Архитектурные паттерны (MVVM, DI, Coordinator)
- Имена сервисов, репозиториев, моделей
- Структуру Features
- Взаимодействие с бэкендом

## Shared Packages

Проект использует те же локальные пакеты, что и macOS-клиент:

```
../../Mac/Barkfluff/Packages/
├── BFProto/      # Protocol buffer definitions и сгенерированный gRPC код
├── BFNetworking/ # gRPC клиент, соединения, репозитории
└── BFCore/       # Бизнес-логика: сервисы, модели, кеш, утилиты
```

## Architecture (MVVM)

Копируй структуру из macOS-клиента:

```
Barkfluff/
├── App/                    # Entry point и DI
│   ├── BarkfluffApp.swift
│   └── DI/                 # DependencyContainer
├── Navigation/             # Coordinator pattern
│   ├── AppCoordinator.swift
│   └── RootView.swift
├── Features/               # Feature modules (MVVM)
│   ├── Auth/
│   ├── ChatList/
│   ├── Conversation/
│   ├── Profile/
│   └── ...
└── DesignSystem/           # Reusable UI components
```

## Key Patterns (из macOS-клиента)

- **Dependency Injection**: `DependencyContainer` со всеми сервисами
- **Navigation**: `AppCoordinator` с состояниями (`loading` → `serverSelection` → `authentication` → `main`)
- **Data Flow**: Views → ViewModels → Services → Repositories → gRPC
- **Caches**: `UserCache`, `ChatCache` для in-memory кеширования

## iOS-specific Differences

- Навигация: `NavigationStack` вместо `NavigationSplitView`
- Tab bar вместо Sidebar для основного интерфейса
- Адаптивный UI для разных размеров экрана
- Touch-ориентированные жесты

## Liquid Glass (iOS 26)

Используй нативный Liquid Glass как в macOS-клиенте. Справочник:
`/Users/fooxboy/RiderProjects/BarkFluff/Mac/Barkfluff/LiquidGlassGuide.md`

## Build Commands

```bash
# Build
xcodebuild -project Barkfluff.xcodeproj -scheme Barkfluff -destination 'platform=iOS Simulator,name=iPhone 16' build

# Open in Xcode
open Barkfluff.xcodeproj
```

## Code Conventions

- Комментарии на русском (как в macOS-клиенте)
- Services с `Protocol` суффиксом (например, `AuthServiceProtocol`)
- ViewModels — `@Observable` классы
- Async/await для асинхронных операций
