# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

BarkFluff is a macOS messaging application built with SwiftUI, using gRPC for backend communication. The app supports real-time messaging, file sharing, and group chats.

## Build Commands

```bash
# Build the project
xcodebuild -project Barkfluff.xcodeproj -scheme Barkfluff -configuration Debug build

# Build for release
xcodebuild -project Barkfluff.xcodeproj -scheme Barkfluff -configuration Release build

# Open in Xcode
open Barkfluff.xcodeproj
```

## Architecture

### Package Structure

The project is organized as an Xcode project with three local Swift Package dependencies:

```
Packages/
├── BFProto/      # Protocol buffer definitions and generated gRPC code
├── BFNetworking/ # gRPC client, connection management, repositories
└── BFCore/       # Business logic: services, models, cache, utilities
```

Dependency flow: `BFProto ← BFNetworking ← BFCore ← Main App`

### App Structure (MVVM)

```
Barkfluff/
├── App/                    # Entry point and DI
│   ├── BarkfluffApp.swift  # @main, app lifecycle
│   └── DI/                 # DependencyContainer
├── Navigation/             # Coordinator pattern
│   ├── AppCoordinator.swift    # Global app state management
│   └── RootView.swift          # Root view with state routing
├── Features/               # Feature modules (MVVM per feature)
│   ├── Auth/              # Login/Register
│   ├── ChatList/          # Chat list sidebar
│   ├── Conversation/      # Individual chat view
│   ├── GroupChat/         # Group chat creation
│   ├── Profile/           # User profile
│   ├── Settings/          # App settings
│   ├── UserSearch/        # User search
│   └── FastAuth/          # Quick authentication
└── DesignSystem/          # Reusable UI components
    ├── Components/        # Buttons, inputs, etc.
    └── LiquidGlass/       # Glass morphism components
```

### Key Patterns

**Dependency Injection**: `DependencyContainer` holds all services, repositories, and caches. Injected via SwiftUI environment.

**Navigation**: `AppCoordinator` manages app state (`loading` → `serverSelection` → `authentication` → `main`) and sidebar navigation. Uses `@Observable` macro.

**Data Flow**:
- Views → ViewModels → Services → Repositories → gRPC
- Services wrap business logic; Repositories handle gRPC calls
- Caches (`UserCache`, `ChatCache`) provide in-memory caching

**Token Management**: `TokenRefreshCoordinator` handles automatic token refresh via `AuthInterceptor`. Session expiration triggers logout flow.

## gRPC Services

Protocol definitions are in `Protos/`. Key services:
- `beacon_api` - Server discovery
- `identity_api` - Authentication
- `messages_api` - Message operations
- `users_api` - User management
- `files_api` - File upload/download
- `updates_api` - Real-time updates (streaming)
- `fast_auth_api` - Quick login

## Dependencies

- **grpc-swift** (2.0.0+) - gRPC client
- **swift-protobuf** (1.28.0+) - Protocol buffers
- **KeychainAccess** (4.2.2+) - Secure token storage
- **Nuke** - Image loading/caching (via Xcode project)

## Swift Version

- Swift tools version: 6.2
- Platforms: macOS 26, iOS 26
- Uses `@Observable` macro (SwiftUI observation)

## Code Conventions

- Comments in Russian are used throughout the codebase
- Services have `Protocol` suffix for interfaces (e.g., `AuthServiceProtocol`)
- ViewModels are `@Observable` classes
- Async/await for all asynchronous operations

## Liquid Glass (macOS 26 / iOS 26)

**ВАЖНО:** При работе с UI-эффектами стекла, размытия и полупрозрачности — ВСЕГДА сначала прочитай полный справочник: `LiquidGlassGuide.md` (в корне проекта)

Проект использует нативный Liquid Glass (WWDC 2025). Краткие правила:

- Используй `.glassEffect(.regular, in: .capsule)` вместо старых `.ultraThinMaterial` / `.thinMaterial` для контролов
- `.glassEffect(.clear)` — для мелких контролов поверх медиа-контента
- `.buttonStyle(.glass)` / `.buttonStyle(.glassProminent)` — для кнопок
- `GlassEffectContainer` — обязательно при нескольких glass-элементах рядом
- Glass — ТОЛЬКО для навигационного слоя (тулбары, кнопки, панели ввода), НИКОГДА для контента
- Не смешивать `.regular` и `.clear` в одном наборе контролов
- Полная документация с примерами, багами и обходными путями — в `LiquidGlassGuide.md`
