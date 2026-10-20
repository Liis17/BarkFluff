[English](../../../README.md) · [Русский](README.md)

<p align="center">
  <img src="../../../Windows/BarkFluff.Client.WPF/Resources/Images/barkfluff_logo.png" width="112" alt="Логотип BarkFluff">
</p>

<h1 align="center">BarkFluff</h1>

<p align="center">
  <strong>Самостоятельно размещаемый мессенджер, спроектированный как распределённая система реального времени.</strong>
</p>

<p align="center">
  <a href="#скачать">Скачать</a> ·
  <a href="#что-внутри">Платформа</a> ·
  <a href="#клиенты">Клиенты</a> ·
  <a href="../../../Obsidian/ClaudeVault/Index.md">Заметки проекта</a>
</p>

<p align="center">
  <a href="../../../LICENSE"><img src="https://img.shields.io/github/license/Liis17/BarkFluff?style=flat-square&color=8A2BE2" alt="Лицензия MIT"></a>
  <a href="https://github.com/Liis17/BarkFluff/actions/workflows/build-client-android.yml"><img src="https://github.com/Liis17/BarkFluff/actions/workflows/build-client-android.yml/badge.svg?branch=master" alt="Android CI"></a>
  <a href="https://github.com/Liis17/BarkFluff/actions/workflows/build-client-winui.yml"><img src="https://github.com/Liis17/BarkFluff/actions/workflows/build-client-winui.yml/badge.svg?branch=master" alt="WinUI CI"></a>
  <a href="https://github.com/Liis17/BarkFluff/actions/workflows/build-client-macos.yml"><img src="https://github.com/Liis17/BarkFluff/actions/workflows/build-client-macos.yml/badge.svg?branch=master" alt="macOS CI"></a>
  <a href="https://github.com/Liis17/BarkFluff/actions/workflows/build-backend-web.yml"><img src="https://github.com/Liis17/BarkFluff/actions/workflows/build-backend-web.yml/badge.svg?branch=master" alt="Web CI"></a>
</p>

---

BarkFluff объединяет нативные клиенты и .NET-бэкенд с gRPC в основе. Клиенты находят сервисы через Beacon, получают события в потоковом режиме и напрямую обращаются к независимым сервисам, которые можно развивать и масштабировать без запутывания всей платформы.

<p align="center">
  <img src="https://github.com/Liis17/BarkFluff/blob/24d7752ec5c73a6af81454be208918f64befbde0/assets/2026-08/27f65b3197f8476984ba83043fd2d9c8.png" width="920">
</p>

## Скачать

[Скачать для Windows](https://storage.barkfluff.com/get/barkfluffwindows/release) · [Скачать для Android](https://storage.barkfluff.com/get/barkfluffkotlin/release) · [Скачать для macOS](https://storage.barkfluff.com/get/barkfluffmacos/release)

<p align="center">
  <sub>Релизные сборки для Windows, Android и macOS. Остальные клиенты доступны из исходников ниже.</sub>
</p>

## Что внутри

**Единая точка входа.** Beacon возвращает клиентам адреса сервисов ноды, а Settings хранит их runtime-настройки.

**Независимые продуктовые сервисы.** Авторизация, профили, сообщения, файлы, присутствие, звонки, боты, федерация и другое работают как сфокусированные .NET-сервисы — с CQRS и MediatR там, где это уместно.

**Реальное время по умолчанию.** Сервис Updates использует постоянные gRPC-стримы для событий продукта, а RabbitMQ переносит асинхронную работу между сервисами.

**Готовность к эксплуатации.** PostgreSQL хранит состояние, Redis обслуживает горячие данные, MinIO отвечает за объекты, а Docker запускает стек. Нативные и web-клиенты сделаны на Kotlin, WinUI, SwiftUI, Qt и web-технологиях.

В [заметках по архитектуре](../../../Obsidian/ClaudeVault/Architecture.md) описаны порты, аутентификация, доставка событий и соглашения сервисов.

## Клиенты

- **Android** — Kotlin и gRPC-OkHttp · [заметки проекта](../../../Obsidian/ClaudeVault/Clients/Android.md)
- **Windows** — WinUI 3 и .NET · [заметки проекта](../../../Obsidian/ClaudeVault/Clients/Windows-WinUI.md) · устаревший WPF-клиент
- **macOS** — SwiftUI и gRPC-Swift · [заметки проекта](../../../Obsidian/ClaudeVault/Clients/macOS.md)
- **iOS** — SwiftUI и gRPC-Swift · [заметки проекта](../../../Obsidian/ClaudeVault/Clients/iOS.md)
- **Linux** — Qt 6, C++20 и gRPC · [заметки проекта](../../../Obsidian/ClaudeVault/Clients/Linux-Qt.md)
- **Web** — gRPC-Web и vanilla-JS SPA · [заметки проекта](../../../Obsidian/ClaudeVault/Clients/Web.md)

## Исследовать репозиторий

> ### 🧩 Платформа
> [`Backend/`](../../../Backend) содержит .NET-микросервисы и web-хосты. В [`Shared/`](../../../Shared) лежат protobuf-контракты и общие .NET-библиотеки.

> ### 📱 Клиенты
> В [`Android/`](../../../Android), [`Windows/`](../../../Windows), [`Mac/`](../../../Mac), [`iOS/`](../../../iOS) и [`Linux/`](../../../Linux) находятся нативные приложения. [`Frontend/`](../../../Frontend) содержит фронтенд портала для разработчиков.

> ### ⚙️ Инфраструктура
> В [`Docker/`](../../../Docker) лежат локальные стеки платформы и инфраструктуры. В [`Tests/`](../../../Tests) — автоматические и нагрузочные тесты.

> ### 📖 Документация
> [База знаний проекта](../../../Obsidian/ClaudeVault/Index.md) содержит заметки об архитектуре, компонентах и тестировании.

## Статус проекта


> ### 🚧 Активная разработка
> BarkFluff активно развивается. Android-клиент собирается из модуля `app-v1`; Jetpack Compose используется в отдельных экранах наряду с Views/XML.

> ### ✅ Состояние сборок
> Статусы выбранных workflow показаны выше. Полный список доступен в [GitHub Actions](https://github.com/Liis17/BarkFluff/actions).

## Справка и документация

> ### 🛠️ Запуск и эксплуатация
> [Архитектура и развёртывание](../../../Obsidian/ClaudeVault/Architecture.md) · [Настройки](../../../Obsidian/ClaudeVault/Backend/Settings.md) · [Интерфейс первичной настройки](../../../Obsidian/ClaudeVault/Backend/Setup.md) · [Тестирование](../../../Obsidian/ClaudeVault/Testing.md)

> ### 🤖 Интеграция бота
> [Заметки о Bot API и сервисе](../../../Obsidian/ClaudeVault/Backend/Bots.md) — возможности, аутентификация и REST-эндпоинты для внешних ботов.

> ### 📚 Изучить систему
> [База знаний проекта](../../../Obsidian/ClaudeVault/Index.md)

> ### ⚖️ Лицензия
> [Лицензия MIT](../../../LICENSE)
