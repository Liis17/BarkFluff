# BarkFluff Android Client

Современный Android клиент для мессенджера BarkFluff, написанный на Kotlin с использованием Jetpack Compose.

## 🚀 Особенности

- **Современный UI** с Material Design 3 и Jetpack Compose
- **Clean Architecture** с разделением на слои (Data, Domain, Presentation)
- **gRPC коммуникация** с backend сервисами
- **Обмен сообщениями** в реальном времени
- **Поддержка файлов** (изображения, видео, документы)
- **Групповые чаты**
- **Профили пользователей** с возможностью редактирования
- **Двухфакторная аутентификация** (2FA)
- **Безопасное хранение** токенов с использованием DataStore

## 📋 Требования

- Android Studio Hedgehog | 2023.1.1 или новее
- Android SDK 26 (Android 8.0) или выше
- Kotlin 1.9.20
- Gradle 8.2

## 🏗️ Архитектура

Приложение построено по принципам **Clean Architecture** и разделено на три основных слоя:

### Data Layer
- **Repositories**: Реализация бизнес-логики работы с данными
  - `AuthRepository` - аутентификация и регистрация
  - `ChatRepository` - работа с чатами и сообщениями
  - `UserRepository` - управление профилями пользователей
  - `FileRepository` - загрузка и скачивание файлов

- **Data Sources**: gRPC клиенты для взаимодействия с backend
  - Beacon API - информация о сервере
  - Identity API - аутентификация
  - Users API - пользователи
  - Messages API - сообщения
  - Files API - файлы
  - Updates API - обновления в реальном времени

- **Local Storage**:
  - SessionManager (DataStore) - хранение сессии
  - Room Database - кэширование данных

### Domain Layer
- **Models**: Доменные модели
  - `User`, `Chat`, `Message`, `AuthToken`, etc.

### Presentation Layer
- **ViewModels**:
  - `AuthViewModel` - управление состоянием аутентификации
  - `ChatViewModel` - управление чатами и сообщениями
  - `UserViewModel` - управление пользователями

- **UI Screens** (Jetpack Compose):
  - **Auth**: Welcome, Login, Register, VerifyEmail, SelectServer
  - **Chat**: ChatList, Chat, NewChat, NewGroup
  - **Profile**: Profile, MyProfile, EditProfile
  - **Settings**: Settings

## 🛠️ Технологии

- **UI Framework**: Jetpack Compose
- **Architecture Components**: ViewModel, LiveData, Navigation
- **Dependency Injection**: Hilt
- **Networking**: gRPC (OkHttp)
- **Image Loading**: Coil
- **Local Storage**: DataStore, Room
- **Async**: Kotlin Coroutines & Flow
- **Serialization**: Protocol Buffers

## 📦 Установка и запуск

1. Клонируйте репозиторий:
```bash
git clone <repository-url>
cd BarkFluff/AndroidClient
```

2. Откройте проект в Android Studio

3. Синхронизируйте Gradle:
```bash
./gradlew build
```

4. Запустите приложение на эмуляторе или устройстве

## 🔧 Конфигурация

### Подключение к backend

По умолчанию приложение подключается к локальному серверу. Для изменения адреса сервера:

1. При первом запуске выберите "Select Server"
2. Введите адрес вашего backend сервера (например: `example.com:5000`)
3. Нажмите "Connect"

### Proto файлы

Proto файлы автоматически копируются из `Shared/BarkFluff.Proto` при сборке проекта.

## 📱 Основные функции

### Аутентификация
- Регистрация нового пользователя
- Вход с email/username и паролем
- Подтверждение email
- Двухфакторная аутентификация (OTP)
- Восстановление пароля

### Чаты
- Список всех чатов
- Личные сообщения
- Групповые чаты
- Отправка текстовых сообщений
- Прикрепление файлов (изображения, видео, документы)
- Уведомления о новых сообщениях в реальном времени
- Отметка прочитанных сообщений
- Поиск пользователей

### Профиль
- Просмотр профиля пользователя
- Редактирование своего профиля (имя, username, bio)
- Загрузка аватара
- Просмотр значков (badges)

### Настройки
- Управление уведомлениями
- Настройки приватности
- Безопасность и 2FA
- Активные сессии
- Выход из аккаунта

## 🔐 Безопасность

- Все токены хранятся в зашифрованном виде с использованием DataStore
- Поддержка TLS для gRPC соединений
- Двухфакторная аутентификация
- Безопасная передача файлов

## 📄 Структура проекта

```
app/src/main/kotlin/com/barkfluff/messenger/
├── data/
│   ├── local/
│   │   └── SessionManager.kt
│   ├── remote/
│   │   └── interceptor/
│   │       └── AuthInterceptor.kt
│   └── repository/
│       ├── AuthRepository.kt
│       ├── ChatRepository.kt
│       ├── UserRepository.kt
│       └── FileRepository.kt
├── domain/
│   └── model/
│       ├── User.kt
│       ├── Chat.kt
│       ├── Message.kt
│       └── AuthToken.kt
├── presentation/
│   ├── navigation/
│   │   ├── Screen.kt
│   │   └── NavGraph.kt
│   ├── screens/
│   │   ├── auth/
│   │   ├── chat/
│   │   ├── profile/
│   │   ├── settings/
│   │   └── splash/
│   ├── viewmodel/
│   │   ├── AuthViewModel.kt
│   │   ├── ChatViewModel.kt
│   │   └── UserViewModel.kt
│   ├── theme/
│   │   ├── Theme.kt
│   │   └── Type.kt
│   └── MainActivity.kt
├── di/
│   └── NetworkModule.kt
└── BarkFluffApp.kt
```

## 🎨 UI/UX

Приложение использует Material Design 3 с поддержкой:
- Темной и светлой темы
- Dynamic Colors (Android 12+)
- Плавных анимаций
- Адаптивной верстки

## 🐛 Известные проблемы

- Отправка голосовых сообщений в разработке
- Видеозвонки еще не реализованы
- Некоторые настройки недоступны

## 🤝 Вклад в проект

Мы приветствуем вклад в развитие проекта!

## 📝 Лицензия

[Укажите лицензию проекта]

## 📞 Контакты

[Укажите контакты для связи]

---

**Версия**: 1.0.0
**Последнее обновление**: 2024
