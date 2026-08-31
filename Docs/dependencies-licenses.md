# Зависимости и лицензии

Реестр сторонних зависимостей всех проектов репозитория с лицензиями — основа для раздела благодарностей (открытое и закрытое ПО).

> Составлено: 2026-07-10. Лицензии указаны по состоянию версий, зафиксированных в проектных файлах. Перед публикацией раздела благодарностей стоит перепроверить лицензии спорных пакетов (помечены ⚠️) на NuGet/GitHub.

---

## Backend — микросервисы (.NET 10)

Сокращения проектов: без префикса `BarkFluff.` / `Barkfluff.`.

| Пакет | Версия | Лицензия | Проекты |
|---|---|---|---|
| AWSSDK.Core | 4.0.7.4 | Apache-2.0 | все Backend-сервисы, все Shared, все Windows |
| AWSSDK.S3 | 4.0.23.5 | Apache-2.0 | ClientStorage, Files, AdminPanel |
| Google.Protobuf | 3.35.0 | BSD-3-Clause | GrpcServer, Files, AdminPanel, WebServer |
| Grpc.AspNetCore.Server | 2.80.0 | Apache-2.0 | GrpcServer, Files |
| Grpc.AspNetCore.Server.Reflection | 2.80.0 | Apache-2.0 | GrpcServer |
| Grpc.AspNetCore.Web | 2.80.0 | Apache-2.0 | Identity, Developers |
| Grpc.Net.Client | 2.80.0 | Apache-2.0 | GrpcServer, AdminPanel, WebServer |
| Grpc.Net.ClientFactory | 2.80.0 | Apache-2.0 | Calls, Bots, Beacon, CloudMessaging, FastAuth, Users, Onliner, Identity, Messages, Files, AdminPanel |
| Grpc.Tools | 2.80.0 | Apache-2.0 | почти все сервисы (build-time, codegen) |
| MediatR | 12.5.0 | Apache-2.0 | GrpcServer |
| MassTransit.RabbitMQ | 8.5.9 | Apache-2.0 | Calls, Bots, Updates, CloudMessaging, Notification, Files, Users, Onliner, Identity, Messages, AdminPanel |
| Microsoft.EntityFrameworkCore (+ Design/Tools/Sqlite) | 10.0.8 | MIT | ClientStorage, Calls, Bots, Developers, Files, Users, Navigator, Settings, Onliner, Identity, Messages |
| Npgsql.EntityFrameworkCore.PostgreSQL | 10.0.2 | PostgreSQL License | Calls, Bots, Developers, Files, Users, Navigator, Settings, Onliner, Identity, Messages |
| Microsoft.AspNetCore.OpenApi | 10.0.8 | MIT | Beacon, Notification, Files, FastAuth, Users, Onliner, Identity, Messages, WebServer, AdminPanel |
| Microsoft.AspNetCore.Authentication.JwtBearer | 10.0.8 | MIT | GrpcServer |
| Microsoft.Extensions.Caching.StackExchangeRedis | 10.0.8 | MIT | Messages |
| System.IdentityModel.Tokens.Jwt | 8.18.0 | MIT | GrpcServer, Identity |
| Serilog.AspNetCore | 10.0.0 | Apache-2.0 | GrpcServer, ClientStorage |
| Serilog.Sinks.Seq | 9.1.0 | Apache-2.0 | GrpcServer |
| Serilog.Enrichers.Environment | 3.0.1 | Apache-2.0 | GrpcServer, ClientStorage |
| Serilog.Enrichers.Thread | 4.0.0 | Apache-2.0 | GrpcServer, ClientStorage |
| BCrypt.Net-Next | 4.2.0 | MIT | Identity |
| Otp.NET | 1.4.1 | MIT | Identity |
| QRCoder | 1.8.0 | MIT | Identity, FastAuth (и WPF-клиент) |
| SixLabors.ImageSharp ⚠️ | 3.1.12 | Six Labors Split License (Apache-2.0 для OSS/малых компаний, иначе коммерческая) | Files (и WPF-клиент, WebApi.Core) |
| FFMpegCore ⚠️ | 5.4.0 | MIT (сам вызывает бинарь FFmpeg — LGPL-2.1/GPL) | Files |
| FirebaseAdmin | 3.5.0 | Apache-2.0 | CloudMessaging |
| Livekit.Server.Sdk.Dotnet | 1.2.2 | Apache-2.0 | Calls |
| Telegram.Bot | 22.10.0.1 | MIT | AdminPanel, WebServer |
| Yarp.ReverseProxy | 2.3.0 | MIT | Web |
| SSH.NET | 2025.1.0 | MIT | AdminPanel |
| LiteDB | 5.0.21 | MIT | AdminPanel (и WPF-клиент) |
| MailKit | 4.17.0 | MIT | AdminPanel |
| Seq.Api | 2025.2.2 | Apache-2.0 | AdminPanel |

### BarkFluff.Web — JS-бандл (`scripts/package.json`)

| Пакет | Версия | Лицензия |
|---|---|---|
| google-protobuf | 3.21.2 | BSD-3-Clause |
| grpc-web | 1.5.0 | Apache-2.0 |
| esbuild | 0.24.0 | MIT |
| livekit-client | 2.19.2 | Apache-2.0 |

AdminPanel UI дополнительно использует **Tailwind CSS** (MIT).

---

## Backend — BarkFluff.Users.Rust

| Крейт | Версия | Лицензия |
|---|---|---|
| tonic, tonic-reflection, tonic-build | 0.12 | MIT |
| prost, prost-types | 0.13 | Apache-2.0 |
| tokio | 1 | MIT |
| tokio-stream | 0.1 | MIT |
| futures-util | 0.3 | MIT / Apache-2.0 |
| sqlx | 0.8 | MIT / Apache-2.0 |
| lapin | 2.5 | MIT |
| tokio-executor-trait | 2 | MIT |
| hmac, sha2 | 0.12 / 0.10 | MIT / Apache-2.0 |
| base64 | 0.22 | MIT / Apache-2.0 |
| uuid | 1 | MIT / Apache-2.0 |
| chrono | 0.4 | MIT / Apache-2.0 |
| serde, serde_json | 1 | MIT / Apache-2.0 |
| tracing, tracing-subscriber | 0.1 / 0.3 | MIT |
| dashmap | 6 | MIT |
| regex | 1 | MIT / Apache-2.0 |
| anyhow, thiserror | 1 / 2 | MIT / Apache-2.0 |
| once_cell | 1 | MIT / Apache-2.0 |
| protoc-bin-vendored | 3 | MIT / Apache-2.0 (включает бинарь protoc — BSD-3-Clause) |

---

## Shared-библиотеки (.NET)

| Проект | Пакеты |
|---|---|
| Shared.SecurityUtilities, Shared.Queue, Shared.Identity, Proto | AWSSDK.Core (Apache-2.0) |
| Shared.Auth | AWSSDK.Core (Apache-2.0), Grpc.Core.Api 2.80.0 (Apache-2.0) |
| Shared.Exceptions | AWSSDK.Core (Apache-2.0), Grpc.Core 2.46.6 (Apache-2.0) |

---

## Windows-клиенты (.NET / WPF)

### BarkFluff.Client.WPF

| Пакет | Версия | Лицензия |
|---|---|---|
| AWSSDK.Core | 4.0.7.4 | Apache-2.0 |
| Emoji.Wpf ⚠️ | 0.3.4 | WTFPL |
| Google.Protobuf | 3.35.0 | BSD-3-Clause |
| Grpc.Net.Client, Grpc.Tools | 2.80.0 | Apache-2.0 |
| LiteDB | 5.0.21 | MIT |
| Microsoft.Toolkit.Uwp.Notifications | 7.1.3 | MIT |
| Newtonsoft.Json | 13.0.4 | MIT |
| QRCoder | 1.8.0 | MIT |
| SixLabors.ImageSharp ⚠️ | 3.1.12 | Six Labors Split License |
| System.Drawing.Common | 10.0.8 | MIT |
| WPF-UI (lepo.co) | 4.3.0 | MIT |
| Microsoft.WindowsAPICodePack.Shell ⚠️ | 1.1.0 | кастомная лицензия Microsoft (Windows API Code Pack) |

### BarkFluff.ClientV2.WPF

NuGet-зависимостей нет (только .NET SDK / WPF).

### Barkfluff.Updater.CLI

| Пакет | Версия | Лицензия |
|---|---|---|
| AWSSDK.Core | 4.0.7.4 | Apache-2.0 |
| SharpZipLib | 1.4.2 | MIT |

### BarkFluff.WebApi.Core

| Пакет | Версия | Лицензия |
|---|---|---|
| AWSSDK.Core | 4.0.7.4 | Apache-2.0 |
| Google.Protobuf | 3.35.0 | BSD-3-Clause |
| Grpc.Net.Client, Grpc.Tools | 2.80.0 | Apache-2.0 |
| SixLabors.ImageSharp ⚠️ | 3.1.12 | Six Labors Split License |
| System.Drawing.Common | 10.0.8 | MIT |

---

## Android-клиенты (Kotlin)

### Модуль `:core`

| Зависимость | Версия | Лицензия |
|---|---|---|
| io.grpc: grpc-okhttp / grpc-protobuf-lite / grpc-stub | 1.60.0 | Apache-2.0 |
| io.grpc: grpc-kotlin-stub | 1.4.1 | Apache-2.0 |
| com.google.protobuf: protobuf-javalite (+ protoc 3.25.1) | 3.25.1 | BSD-3-Clause |
| kotlinx-coroutines-core / -android | 1.7.3 | Apache-2.0 |
| androidx.core:core-ktx | 1.17.0 | Apache-2.0 |
| androidx.security:security-crypto | 1.1.0-alpha06 | Apache-2.0 |
| org.signal: libsignal-android / libsignal-client ⚠️ | 0.86.16 | **AGPL-3.0** |
| com.lambdapioneer.argon2kt:argon2kt | 1.6.0 | MIT (внутри — reference-реализация Argon2, CC0/Apache-2.0) |
| org.apache.tomcat:annotations-api (compileOnly) | 6.0.53 | Apache-2.0 |
| desugar_jdk_libs | 2.1.4 | GPL-2.0 with Classpath Exception |
| Gradle-плагин com.google.protobuf | 0.9.4 | Apache-2.0 |

### Приложение V1 (`Barkfluff.Client.Android/app`)

| Зависимость | Версия | Лицензия |
|---|---|---|
| AndroidX (appcompat, constraintlayout, cardview, recyclerview, fragment, viewpager2, lifecycle-process, work-runtime-ktx, dynamicanimation, camera-*) | см. build.gradle.kts | Apache-2.0 |
| Material Components (com.google.android.material) | 1.13.0 | Apache-2.0 |
| Room (runtime, ktx, compiler) + androidx.sqlite | 2.7.1 / 2.6.2 | Apache-2.0 |
| net.zetetic:sqlcipher-android | 4.15.0 | BSD-style (Zetetic SQLCipher Community Edition) |
| io.coil-kt: coil / coil-video | 2.7.0 | Apache-2.0 |
| com.squareup.okhttp3:okhttp | 4.12.0 | Apache-2.0 |
| com.github.chrisbanes:PhotoView | 2.3.0 | Apache-2.0 |
| com.github.yalantis:ucrop | 2.2.8 | Apache-2.0 |
| androidx.media3 (exoplayer, ui, transformer, effect, common) | 1.3.1 | Apache-2.0 |
| io.livekit: livekit-android / livekit-android-camerax | 2.26.0 | Apache-2.0 |
| com.google.mlkit:barcode-scanning ⚠️ | 17.3.0 | **проприетарная** (Google Play services / ML Kit Terms) |
| Firebase (BoM 34.10.0): firebase-messaging ⚠️ | 34.10.0 | Apache-2.0 (SDK), зависит от проприетарных Google Play services |
| Firebase: firebase-analytics ⚠️ | 34.10.0 | **проприетарная** (Google) |
| kotlinx-coroutines-android | 1.7.3 | Apache-2.0 |
| desugar_jdk_libs | 2.1.4 | GPL-2.0 with Classpath Exception |

Toolchain приложения: Kotlin 2.2.20 (Apache-2.0), AGP 8.9.1 (Apache-2.0), KSP (Apache-2.0), плагин google-services (Apache-2.0).

---

## Frontend/Developers (React + Vite)

| Пакет | Версия | Лицензия |
|---|---|---|
| react, react-dom | ^19.0.0 | MIT |
| @connectrpc/connect, @connectrpc/connect-web | ^1.6.0 | Apache-2.0 |
| @bufbuild/protobuf | ^1.10.0 | Apache-2.0 |
| @bufbuild/buf (dev) | ^1.47.2 | Apache-2.0 |
| @bufbuild/protoc-gen-es, @connectrpc/protoc-gen-connect-es (dev) | ^1.10.0 / ^1.6.0 | Apache-2.0 |
| @vitejs/plugin-react (dev) | ^4.3.0 | MIT |
| vite (dev) | ^6.0.0 | MIT |
| typescript (dev) | ~5.7.0 | Apache-2.0 |
| @types/react, @types/react-dom (dev) | ^19.0.0 | MIT |

---

## Tests (только сборка тестов, в продукт не входят)

| Пакет | Версия | Лицензия | Где |
|---|---|---|---|
| xunit, xunit.runner.visualstudio | 2.9.3 / 3.1.5 | Apache-2.0 | все Tests-проекты |
| FluentAssertions ⚠️ | 8.10.0 | Xceed Community License (8.x платная для коммерческого использования; бесплатна для некоммерческого) | большинство Tests |
| Moq | 4.20.72 | BSD-3-Clause | большинство Tests |
| Microsoft.NET.Test.Sdk | 18.6.0 | MIT | все Tests |
| Microsoft.EntityFrameworkCore.InMemory / .Sqlite | 10.0.8 | MIT | Calls, Users, Onliner, Messages, Identity, Files Tests |
| MassTransit.RabbitMQ | 8.5.9 | Apache-2.0 | часть Tests |
| BCrypt.Net-Next, Otp.NET | 4.2.0 / 1.4.1 | MIT | Identity.Tests |
| junit 4 | 4.13.2 | EPL-1.0 | Android |

---

## Инфраструктура и внешнее ПО (не пакеты, но используются)

| ПО | Лицензия / статус |
|---|---|
| PostgreSQL | PostgreSQL License (открытое) |
| RabbitMQ | MPL-2.0 (открытое) |
| Redis | RSALv2 / SSPL (source-available; версии ≤7.2 — BSD-3-Clause) |
| Seq (Datalust) ⚠️ | проприетарное (есть бесплатный tier) |
| MinIO (dev S3) | AGPL-3.0 |
| nginx | BSD-2-Clause |
| LiveKit server | Apache-2.0 |
| FFmpeg (бинарь для Files) | LGPL-2.1 / GPL-2.0 (зависит от сборки) |
| HostKey S3 (прод) | коммерческий сервис |
| Firebase Cloud Messaging, ML Kit | проприетарные сервисы Google |

---

## ⚠️ На что обратить внимание в благодарностях

1. **libsignal (AGPL-3.0)** — самая строгая лицензия в проекте; обязательное упоминание и соблюдение условий AGPL.
2. **SixLabors.ImageSharp** — Split License: Apache-2.0 только для OSS / компаний с выручкой < 1M USD, иначе нужна коммерческая лицензия.
3. **FluentAssertions 8.x** — с версии 8 лицензия Xceed, платная для коммерческого использования (касается только тестов, в продукт не попадает).
4. **firebase-analytics, ML Kit, Google Play services** — закрытое ПО Google, упоминать в разделе закрытого ПО.
5. **Microsoft.WindowsAPICodePack.Shell** — кастомная лицензия Microsoft, не OSI.
6. **Emoji.Wpf** — WTFPL (формально свободная, но специфичная).
7. **FFmpeg** — если бинарь распространяется вместе с сервисом Files, действует LGPL/GPL.
8. **Seq, HostKey S3** — коммерческие сервисы, раздел закрытого ПО.
