[English](../../../README.md) · [Русский](README.md)

<p align="center">
  <img src="../../../Windows/BarkFluff.Client.WPF/Resources/Images/barkfluff.icon.white-512.png" width="112" alt="Лого">
</p>

<h1 align="center">BarkFluff</h1>

<p align="center">
  <strong>Self-hosted мессенджер, спроектированный как распределённая система реального времени.</strong>
</p>

<p align="center">
  <a href="#скачать">Скачать</a> ·
  <a href="#что-внутри">Платформа</a> ·
  <a href="#клиенты">Клиенты</a> ·
  <a href="../../README.md">Документация</a>
</p>

<p align="center">
  <a href="../../../LICENSE"><img src="https://img.shields.io/github/license/Liis17/BarkFluff?style=flat-square&color=8A2BE2" alt="Лицензия MIT"></a>
  <a href="https://github.com/Liis17/BarkFluff/actions/workflows/build-client-android.yml"><img src="https://github.com/Liis17/BarkFluff/actions/workflows/build-client-android.yml/badge.svg?branch=dev" alt="Android CI"></a>
  <a href="https://github.com/Liis17/BarkFluff/actions/workflows/build-client-wpf.yml"><img src="https://github.com/Liis17/BarkFluff/actions/workflows/build-client-wpf.yml/badge.svg?branch=dev" alt="Windows CI"></a>
  <a href="https://github.com/Liis17/BarkFluff/actions/workflows/build-client-macos.yml"><img src="https://github.com/Liis17/BarkFluff/actions/workflows/build-client-macos.yml/badge.svg?branch=dev" alt="macOS CI"></a>
</p>

---

BarkFluff объединяет нативные клиенты и .NET-бэкенд с gRPC в основе. Клиенты находят сервисы через Beacon, получают события в потоковом режиме и напрямую обращаются к независимым сервисам, которые можно развивать и масштабировать без запутывания всей платформы.

<p align="center">
  <img src="https://github.com/Liis17/BarkFluff/blob/24d7752ec5c73a6af81454be208918f64befbde0/assets/2026-08/27f65b3197f8476984ba83043fd2d9c8.png" width="920">
</p>

## Скачать

<p align="center">
  <a href="https://storage.barkfluff.com/get/barkfluffwindows/release"><img src="../../../docs/images/readme/download-windows.svg" alt="Скачать BarkFluff для Windows" height="88"></a>
  <a href="https://storage.barkfluff.com/get/barkfluffkotlin/release"><img src="../../../docs/images/readme/download-android.svg" alt="Скачать BarkFluff для Android" height="88"></a>
  <a href="https://storage.barkfluff.com/get/barkfluffmacos/release"><img src="../../../docs/images/readme/download-macos.svg" alt="Скачать BarkFluff для macOS" height="88"></a>
</p>

<p align="center">
  <sub>Релизные сборки для Windows, Android и macOS. Остальные клиенты доступны из исходников ниже.</sub>
</p>

## Что внутри

**Единая точка входа.** Beacon даёт клиентам доверенный способ обнаружить платформу; Configuration предоставляет реестр сервисов и runtime-настройки.

**Независимые продуктовые сервисы.** Авторизация, профили, сообщения, файлы, присутствие, звонки, боты, федерация и другое работают как сфокусированные .NET-сервисы — с CQRS и MediatR там, где это уместно.

**Реальное время по умолчанию.** Сервис Updates использует постоянные gRPC-стримы для событий продукта, а RabbitMQ переносит асинхронную работу между сервисами.

**Готовность к эксплуатации.** PostgreSQL хранит состояние, Redis обслуживает горячие данные, MinIO отвечает за объекты, а Docker запускает стек. Нативные и web-клиенты сделаны на Kotlin, WPF, SwiftUI, Qt и web-технологиях.

В [гайде по архитектуре](../../../Obsidian/ClaudeVault/Архитектура.md) описаны порты, аутентификация, доставка событий и соглашения сервисов.

## Клиенты

- **Android** — Kotlin и gRPC-OkHttp · [инструкция по сборке](../../clients/android.md)
- **Windows** — WPF и .NET · [инструкция по сборке](../../clients/windows.md)
- **macOS** — SwiftUI и gRPC-Swift · [инструкция по сборке](../../clients/macos.md)
- **iOS** — SwiftUI и gRPC-Swift · [инструкция по сборке](../../clients/ios.md)
- **Linux** — Qt 6, C++20 и gRPC · [инструкция по сборке](../../clients/linux.md)
- **Web** — gRPC-Web и vanilla-JS SPA · [инструкция по сборке](../../clients/web.md)

## Исследовать репозиторий

> ### 🧩 Платформа
> [`Backend/`](../../../Backend) содержит .NET-микросервисы и web-хосты. В [`Shared/`](../../../Shared) лежат protobuf-контракты и общие .NET-библиотеки.

> ### 📱 Клиенты
> В [`Android/`](../../../Android), [`Windows/`](../../../Windows), [`Mac/`](../../../Mac), [`iOS/`](../../../iOS) и [`Linux/`](../../../Linux) находятся нативные приложения. [`Frontend/`](../../../Frontend) содержит фронтенд портала для разработчиков.

> ### ⚙️ Инфраструктура
> В [`docker/`](../../../docker) лежат локальные стеки платформы и инфраструктуры. В [`Tests/`](../../../Tests) — автоматические и нагрузочные тесты.

> ### 📖 Документация
> [`.readme/`](../../) — публичный хаб запуска; [`Obsidian/ClaudeVault/`](../../../Obsidian/ClaudeVault) — база знаний проекта.

## Статус проекта


> ### 🚧 Активная разработка
> BarkFluff активно развивается. Android V1 — поддерживаемый Android-клиент; проект V2 на Jetpack Compose экспериментальный и должен меняться только в рамках отдельной задачи.

> ### ✅ Состояние сборок
> Статус клиентских workflow показан выше. Полная матрица доступна в [GitHub Actions](https://github.com/Liis17/BarkFluff/actions).

## Справка и документация

> ### 🛠️ Запуск и эксплуатация
> [Инструкция по бэкенду](../../backend.md) · [Порты и переменные окружения](../../../Backend/PORTS_CONFIGURATION.md) · [Справка по Docker](../../../Backend/DOCKER_SETUP.md) · [Реестр метрик](../../../Backend/METRICS.md)

> ### 📚 Изучить систему
> [Документационный хаб](../../README.md) · [Архитектура](../../../Obsidian/ClaudeVault/Архитектура.md) · [База знаний проекта](../../../Obsidian/ClaudeVault/Index.md)

> ### ⚖️ Лицензия
> [Лицензия MIT](../../../LICENSE)
