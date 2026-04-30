# BarkFluff iOS

SwiftUI приложение для iOS. iOS-версия macOS-клиента.

Расположение: `iOS/Barkfluff/`

## Важно: Референс — macOS клиент и дизайн-документ

**При разработке iOS-версии ВСЕГДА смотреть на [[Клиенты/macOS]] как на референс.**
**UI/UX спецификация всех экранов и сценариев: [[Клиенты/DesignDocument]] (источник: `dd.md`)**
**Карта всех файлов проекта: [[Клиенты/iOS-ProjectMap]]**

Использовать те же:
- Архитектурные паттерны (MVVM, DI, Coordinator)
- Имена сервисов, репозиториев, моделей
- Структуру Features
- Взаимодействие с бэкендом

## Shared Packages

Те же пакеты, что и macOS-клиент (`../../Mac/Barkfluff/Packages/`):
- `BFProto/` — Proto definitions
- `BFNetworking/` — gRPC клиент, репозитории
- `BFCore/` — Бизнес-логика: сервисы, модели, кеш

## iOS-specific

- Навигация: `NavigationStack` вместо `NavigationSplitView`
- Tab bar вместо Sidebar для основного интерфейса
- Адаптивный UI для разных размеров экрана
- Touch-ориентированные жесты

## Структура (копия macOS)

```
Barkfluff/
├── App/
│   ├── BarkfluffApp.swift
│   └── DI/DependencyContainer
├── Navigation/
│   ├── AppCoordinator.swift     # loading → serverSelection → authentication → main
│   └── RootView.swift
├── Features/
│   ├── Auth/, ChatList/, Conversation/
│   └── ...
└── DesignSystem/
```

## Liquid Glass (iOS 26)

Аналогично [[Клиенты/macOS]]. Справочник: `Mac/Barkfluff/LiquidGlassGuide.md`.

## Code Conventions

- Комментарии на русском
- Services с `Protocol` суффиксом
- ViewModels — `@Observable`
- Async/await

## Сборка

```bash
xcodebuild -project Barkfluff.xcodeproj -scheme Barkfluff -destination 'platform=iOS Simulator,name=iPhone 16' build
open Barkfluff.xcodeproj
```
