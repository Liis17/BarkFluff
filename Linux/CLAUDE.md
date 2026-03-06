# BarkFluffQt - Техническая документация

Клиент мессенджера BarkFluff на Qt 6 / C++20 с использованием gRPC.

## Содержание

- [Обзор проекта](#обзор-проекта)
- [Технологический стек](#технологический-стек)
- [Архитектура](#архитектура)
- [Code Style Guide](#code-style-guide)
- [Паттерны проектирования](#паттерны-проектирования)
- [Соглашения по слоям](#соглашения-по-слоям)
- [Qt-специфика](#qt-специфика)
- [API Integration](#api-integration)
- [Примеры кода](#примеры-кода)

---

## Обзор проекта

BarkFluffQt — это кроссплатформенный клиент мессенджера BarkFluff, написанный на C++ с использованием фреймворка Qt 6. Приложение поддерживает:
- Авторизацию и регистрацию пользователей
- Личные и групповые чаты
- Отправку текстовых сообщений и вложений
- Отображение онлайн-статуса пользователей
- PIN-код для защиты сессии
- Кэширование файлов

---

## Технологический стек

| Компонент | Технология |
|-----------|------------|
| GUI Framework | Qt 6 (Widgets, Network, Svg, Concurrent, Multimedia) |
| Language | C++20 |
| Build System | CMake 3.20+ |
| RPC | gRPC + Protobuf |
| Encryption | OpenSSL |
| Optional | KDE Frameworks 6 (KConfig) |

### Qt модули

```cmake
Qt6::Core       # Базовые классы, сигналы/слоты
Qt6::Widgets    # UI виджеты
Qt6::Network    # HTTP, network operations
Qt6::Svg        # SVG rendering
Qt6::Concurrent # Многопоточность
Qt6::Multimedia # Аудио/видео
```

---

## Архитектура

Проект следует **слоистой архитектуре** с чётким разделением ответственности:

```
src/
├── Connection/     # Сетевой слой (gRPC клиенты)
├── Models/         # Модели данных
├── UI/             # Презентационный слой
│   ├── Pages       # Страницы (MainWindow, LoginPage, MessengerPage, ...)
│   └── Widgets/    # Переиспользуемые виджеты
├── Services/       # Бизнес-логика (SessionManager, FileCacheService)
├── Storage/        # Хранение данных (SecureStorage, AppSettings)
└── Utils/          # Вспомогательные классы
```

### Диаграмма зависимостей

```
┌─────────────────────────────────────────────────────────┐
│                        UI Layer                         │
│  MainWindow → Pages → Widgets → (Models, Services)     │
└─────────────────────────────────────────────────────────┘
                          │
                          ▼
┌─────────────────────────────────────────────────────────┐
│                     Services Layer                      │
│  SessionManager, FileCacheService → (Storage, Models)  │
└─────────────────────────────────────────────────────────┘
                          │
                          ▼
┌─────────────────────────────────────────────────────────┐
│                   Connection Layer                      │
│  GrpcClient → (NavigatorClient, MessagesClient, ...)   │
└─────────────────────────────────────────────────────────┘
                          │
                          ▼
┌─────────────────────────────────────────────────────────┐
│                     Models Layer                        │
│  Chat, Message, User, ServerConfiguration, ...         │
└─────────────────────────────────────────────────────────┘
```

---

## Code Style Guide

### Namespace

Весь код находится в namespace `BarkFluff`:

```cpp
namespace BarkFluff {

class MyClass {
    // ...
};

} // namespace BarkFluff
```

### Header Guards

Используется `#pragma once`:

```cpp
#pragma once

#include <QString>

namespace BarkFluff {
// ...
}
```

### Именование

| Элемент | Стиль | Пример |
|---------|-------|--------|
| Классы | PascalCase | `MessageBubble`, `SessionManager` |
| Методы | camelCase | `sendMessage()`, `loadChats()` |
| Member variables | camelCase + trailing underscore | `messagesClient_`, `chats_` |
| Локальные переменные | camelCase | `message`, `chatId` |
| Константы | camelCase / SCREAMING_CASE | `groupingInterval`, `MAX_SIZE` |
| Enums | PascalCase | `SendingState::Sending` |
| Namespaces | PascalCase | `BarkFluff` |

### Member Variables

```cpp
class MessengerPage : public QWidget {
private:
    // Pointers with underscore suffix
    MessagesClient* messagesClient_ = nullptr;
    QListWidget* listWidget_ = nullptr;
    
    // Values with underscore suffix
    QList<Chat> chats_;
    bool isLoading_ = false;
    QString currentSearchQuery_;
    
    // Smart pointers
    std::unique_ptr<UpdatesClient> updatesClient_;
};
```

### Includes

Порядок includes:
1. Соответствующий .h файл (для .cpp)
2. Qt headers
3. STL headers
4. gRPC/Protobuf headers
5. Локальные headers

```cpp
#include "MessengerPage.h"           // 1. Corresponding header

#include <QWidget>                    // 2. Qt headers
#include <QList>
#include <memory>                     // 3. STL headers

#include <grpcpp/grpcpp.h>            // 4. gRPC headers

#include "Models/Chat.h"              // 5. Local headers
#include "Connection/MessagesClient.h"
```

### Документирование кода

Используется Doxygen на русском языке:

```cpp
/**
 * @brief Краткое описание класса/метода
 * 
 * Развернутое описание если необходимо.
 * 
 * @param paramName Описание параметра
 * @return Описание возвращаемого значения
 */
```

Пример из кода:

```cpp
/**
 * @brief Виджет аватара пользователя
 * 
 * Отображает круглый аватар с изображением или инициалами.
 * Опционально показывает индикатор онлайн-статуса.
 */
class AvatarWidget : public QWidget {
    // ...
};

/**
 * @brief Сгруппировать сообщения для отображения
 * @param messages Массив сообщений (должен быть отсортирован по дате)
 * @param currentUserID ID текущего пользователя
 * @return Массив элементов для отображения
 */
static QList<MessageListItem> groupMessages(
    const QList<Message>& messages,
    qint64 currentUserID
);
```

### Braces

Opening brace на той же строке:

```cpp
class MyClass : public BaseClass {
public:
    void method() {
        if (condition) {
            // ...
        } else {
            // ...
        }
    }
};
```

### Const correctness

```cpp
// Const references для параметров
void setChats(const QList<Chat>& chats);

// Const методы
QString displayName() const;
bool isConnected() const;

// Const member variables через initializer list
```

---

## Паттерны проектирования

### 1. Singleton

Используется для сервисов, которые должны существовать в единственном экземпляре:

```cpp
class SessionManager : public QObject {
public:
    static SessionManager& instance() {
        static SessionManager instance;
        return instance;
    }
    
    // Удалить copy/move
    SessionManager(const SessionManager&) = delete;
    SessionManager& operator=(const SessionManager&) = delete;
    
private:
    SessionManager() = default;
};

// Использование:
auto& session = SessionManager::instance();
session.saveSession(userSession, config, login);
```

### 2. Template Method

Базовый класс `GrpcClient` определяет общую логику для всех gRPC клиентов:

```cpp
class GrpcClient {
protected:
    std::shared_ptr<grpc::Channel> channel_;
    DeviceMetadata deviceMetadata_;
    
    std::unique_ptr<grpc::ClientContext> createContext(const QString& accessToken = QString());
    std::unique_ptr<grpc::ClientContext> createContextNoDeadline(const QString& accessToken = QString());
    
public:
    GrpcClient(const QString& host, int port, bool tls = true, bool skipTlsVerify = false);
    virtual ~GrpcClient() = default;
    bool isConnected() const;
};

// Наследники
class MessagesClient : public GrpcClient {
    std::unique_ptr<barkfluff::messages::MessagesApi::Stub> stub_;
public:
    MessagesClient(const ServiceInfo& service);
    PagedChatsResult listChats(qint32 offset, qint32 size, const QString& accessToken);
    // ...
};

class UsersClient : public GrpcClient { /* ... */ };
class FilesClient : public GrpcClient { /* ... */ };
```

### 3. Observer (Qt Signals/Slots)

Основной механизм коммуникации между компонентами:

```cpp
class MainWindow : public QMainWindow {
    Q_OBJECT
    
signals:
    void serverSelected(const ServerConfiguration& config);
    
private slots:
    void showLogin(const ServerConfiguration& config);
    void showMessenger(const UserSession& session);
    void onLogout();
};

// Connection
connect(loginPage, &LoginPage::loginSuccess, 
        this, &MainWindow::showMessenger);
```

### 4. Optimistic UI

Мгновенное отображение отправленных сообщений до подтверждения сервера:

```cpp
class Message {
public:
    // Состояние отправки
    SendingState sendingState = SendingState::None;
    QString sendError;
    double uploadProgress = 1.0;
    
    // Создание pending-сообщения для optimistic UI
    static Message createPending(const QString& chatId, qint64 senderId, const QString& text) {
        Message msg;
        msg.localID = QUuid::createUuid().toString(QUuid::WithoutBraces);
        msg.id = -QDateTime::currentMSecsSinceEpoch(); // Отрицательный ID для pending
        msg.sendingState = SendingState::Sending;
        // ...
        return msg;
    }
    
    void markAsSent(qint64 confirmedId) {
        id = confirmedId;
        sendingState = SendingState::None;
    }
    
    void markAsFailed(const QString& error) {
        sendingState = SendingState::Failed;
        sendError = error;
    }
};
```

### 5. Factory Methods

Создание объектов через статические методы:

```cpp
class MessageListItem {
public:
    static MessageListItem dateSeparator(const QDate& date) {
        MessageListItem item;
        item.type = Type::DateSeparator;
        item.date = date;
        return item;
    }
    
    static MessageListItem messageItem(const Message& msg, const MessageGroupInfo& info) {
        MessageListItem item;
        item.type = Type::Message;
        item.message = msg;
        item.groupInfo = info;
        return item;
    }
};
```

### 6. Repository Pattern

Клиенты API инкапсулируют работу с сервером:

```cpp
class MessagesClient : public GrpcClient {
public:
    PagedChatsResult listChats(qint32 offset, qint32 size, const QString& accessToken);
    PagedMessagesResult listMessages(const QString& chatId, ...);
    Message sendMessage(const QString& chatId, const QString& text, ...);
    void markAsRead(const QList<qint64>& messageIds, const QString& accessToken);
    
private:
    Chat convertChat(const barkfluff::messages::Chat& protoChat);
    Message convertMessage(const barkfluff::shared::Message& protoMsg);
};
```

### 7. State Pattern

Для управления состоянием UI:

```cpp
enum class SendingState {
    None,       // Обычное сообщение
    Sending,    // Отправляется
    Failed      // Ошибка
};

enum class SharedMediaFilter {
    Media,
    Documents,
    Links
};
```

---

## Соглашения по слоям

### Connection Layer (`src/Connection/`)

gRPC клиенты для взаимодействия с микросервисами бэкенда:

| Класс | Назначение |
|-------|------------|
| `GrpcClient` | Базовый класс с общей логикой |
| `NavigatorClient` | Получение информации о серверах |
| `BeaconClient` | Heartbeat, поддержка соединения |
| `IdentityClient` | Авторизация, регистрация |
| `MessagesClient` | Чаты, сообщения |
| `FilesClient` | Загрузка/скачивание файлов |
| `UsersClient` | Профили пользователей |
| `UpdatesClient` | Server-sent events (новые сообщения) |
| `OnlinerClient` | Онлайн-статусы |

**Принципы:**
- Наследование от `GrpcClient`
- Конвертация proto → Models в приватных методах
- Возврат Result structs с данными + пагинация

```cpp
struct PagedChatsResult {
    QList<Chat> chats;
    qint32 totalCount = 0;
    bool hasMore = false;
};
```

### Models Layer (`src/Models/`)

Data Transfer Objects без бизнес-логики:

```cpp
class Chat {
public:
    QString id;
    QString title;
    bool isGroupChat = false;
    QList<ChatMember> members;
    
    // Helper methods
    QString displayName() const;
    QString avatarInitials() const;
    
    // Serialization
    QVariant toVariant() const;
    static Chat fromVariant(const QVariant& variant);
};

// Регистрация для использования с QVariant
Q_DECLARE_METATYPE(BarkFluff::Chat)
```

**Принципы:**
- POD-like структуры с публичными полями
- Helper methods для частых операций
- Serialization для кэширования (toVariant/fromVariant)
- Q_DECLARE_METATYPE для Qt type system

### UI Layer (`src/UI/`)

#### Pages

Полноэкранные страницы приложения:

- `MainWindow` - контейнер с QStackedWidget
- `ServerSelectPage` - выбор сервера
- `LoginPage` - вход
- `RegisterPage` - регистрация
- `MessengerPage` - основной интерфейс
- `ProfilePage` - профиль текущего пользователя
- `PinUnlockDialog` - ввод PIN

#### Widgets

Переиспользуемые компоненты:

| Widget | Назначение |
|--------|------------|
| `AvatarWidget` | Круглый аватар с инициалами/картинкой |
| `MessageBubble` | Пузырь сообщения с хвостиком |
| `EmojiPicker` | Выбор эмодзи |
| `OnlineStatusWidget` | Индикатор онлайн-статуса |
| `ScrollToBottomButton` | Кнопка прокрутки вниз |
| `ExpandingTextEdit` | Поле ввода с авто-высотой |
| `FlowLayout` | Flow layout для бейджей |
| `AttachmentWidgets` | Превью вложений |
| `SharedMediaGridView` | Галерея медиа |
| `PinCodeInputWidget` | Ввод PIN-кода |

**Принципы:**
- Q_PROPERTY для свойств, используемых в стилях
- signals/slots для коммуникации
- Parent-child memory management

```cpp
class AvatarWidget : public QWidget {
    Q_OBJECT
    Q_PROPERTY(int size READ size WRITE setSize NOTIFY sizeChanged)
    Q_PROPERTY(QString imageUrl READ imageUrl WRITE setImageUrl NOTIFY imageUrlChanged)
    
public:
    explicit AvatarWidget(QWidget* parent = nullptr);
    
    int size() const { return size_; }
    void setSize(int size);
    
signals:
    void sizeChanged(int size);
    void clicked();
    
protected:
    void paintEvent(QPaintEvent* event) override;
    
private:
    int size_ = 40;
    QString imageUrl_;
};
```

### Services Layer (`src/Services/`)

Бизнес-логика приложения:

#### SessionManager

Координирует хранение сессии:

```cpp
class SessionManager : public QObject {
    Q_OBJECT
public:
    static SessionManager& instance();
    
    bool hasStoredSession() const;
    bool initializeWithPin(const QString& pin);
    void saveSession(const UserSession& session, const ServerConfiguration& config, const QString& login);
    UserSession restoreSession();
    void clearSession();
    
signals:
    void sessionCleared();
};
```

#### FileCacheService

Кэширование файлов на диске:

```cpp
class FileCacheService : public QObject {
    Q_OBJECT
public:
    static FileCacheService* instance();
    
    QString getCachedFilePath(const QString& fileId, CachedFileType type, const QString& url);
    void getCachedFilePathAsync(const QString& fileId, CachedFileType type, 
                                 const QString& url, std::function<void(const QString&)> callback);
    bool isFileCached(const QString& fileId) const;
    
signals:
    void fileCached(const QString& fileId, const QString& localPath, CachedFileType type);
};
```

### Storage Layer (`src/Storage/`)

#### SecureStorage

Шифрованное хранение токенов:

```cpp
class SecureStorage {
public:
    bool initialize(const QString& pin);
    void saveTokens(const QString& accessToken, const QString& refreshToken,
                    const QDateTime& accessExpiry, const QDateTime& refreshExpiry);
    QString getAccessToken() const;
    bool changePin(const QString& oldPin, const QString& newPin);
    void clear();
    
private:
    QByteArray encrypt(const QByteArray& data, const QByteArray& key, QByteArray& iv);
    QByteArray decrypt(const QByteArray& data, const QByteArray& key, const QByteArray& iv);
    QByteArray deriveKey(const QString& pin, const QByteArray& salt);
};
```

#### AppSettings

Нешифрованные настройки (QSettings wrapper):

```cpp
class AppSettings {
public:
    static AppSettings& instance();
    
    QString lastServerUrl() const;
    void setLastServerUrl(const QString& url);
    
    QString lastLogin() const;
    void setLastLogin(const QString& login);
};
```

### Utils Layer (`src/Utils/`)

Вспомогательные классы:

| Class | Назначение |
|-------|------------|
| `ErrorHandler` | Конвертация gRPC ошибок в пользовательские сообщения |
| `Validators` | Валидация username, email, password |
| `MessageGrouper` | Группировка сообщений по дате/отправителю |
| `ImageOptimizer` | Сжатие изображений перед отправкой |
| `JwtUtils` | Парсинг JWT токенов (header-only) |
| `DeviceIdGenerator` | Генерация уникального ID устройства |
| `SystemInfo` | Информация о системе |

---

## Qt-специфика

### Signals/Slots

```cpp
// Old style (избегать)
connect(sender, SIGNAL(valueChanged(int)), receiver, SLOT(updateValue(int)));

// New style (предпочтительно)
connect(sender, &Sender::valueChanged, receiver, &Receiver::updateValue);

// Lambda
connect(button, &QPushButton::clicked, this, [this]() {
    sendMessage();
});

// С параметрами
connect(combo, QOverload<int>::of(&QComboBox::currentIndexChanged), 
        this, &MyClass::onIndexChanged);
```

### Q_PROPERTY

Для свойств, используемых в стилях и QML:

```cpp
Q_PROPERTY(int size READ size WRITE setSize NOTIFY sizeChanged)
Q_PROPERTY(QString imageUrl READ imageUrl WRITE setImageUrl NOTIFY imageUrlChanged)
Q_PROPERTY(bool isOnline READ isOnline WRITE setIsOnline NOTIFY isOnlineChanged)
```

### Memory Management

Qt parent-child ownership:

```cpp
class MessengerPage : public QWidget {
private:
    // Qt-owned (parent в конструкторе)
    QSplitter* splitter_ = nullptr;           // this as parent
    QListWidget* listWidget_ = nullptr;       // splitter as parent
    
    // Manual management (raw pointers с parent)
    MessagesClient* messagesClient_ = nullptr;
    
    // Smart pointers для non-Qt объектов
    std::unique_ptr<UpdatesClient> updatesClient_;
};

// Создание с parent
stackedWidget_ = new QStackedWidget(this);
childWidget = new QWidget(stackedWidget_);
```

### Thread Safety

```cpp
class FileCacheService : public QObject {
private:
    mutable QMutex mutex_;
    QMap<QString, CachedFileInfo> cacheInfo_;
    
public:
    bool isFileCached(const QString& fileId) const {
        QMutexLocker locker(&mutex_);
        return cacheInfo_.contains(fileId);
    }
};
```

### Qt Containers

Предпочтение Qt контейнерам для совместимости:

```cpp
QList<Chat> chats;          // вместо std::vector
QSet<qint64> messageIds;    // вместо std::unordered_set
QMap<QString, QString> map; // вместо std::map
```

Но для gRPC используем STL:

```cpp
std::unique_ptr<MessagesApi::Stub> stub_;
std::shared_ptr<grpc::Channel> channel_;
```

---

## API Integration

### gRPC Client Pattern

```cpp
class MessagesClient : public GrpcClient {
public:
    MessagesClient(const ServiceInfo& service)
        : GrpcClient(service.host, service.port, service.tls)
    {
        stub_ = barkfluff::messages::MessagesApi::NewStub(channel_);
    }
    
    PagedChatsResult listChats(qint32 offset, qint32 size, const QString& accessToken) {
        PagedChatsResult result;
        
        auto context = createContext(accessToken);
        barkfluff::messages::ListChatsRequest request;
        request.set_offset(offset);
        request.set_size(size);
        
        barkfluff::messages::ListChatsResponse response;
        grpc::Status status = stub_->ListChats(context.get(), request, &response);
        
        if (!status.ok()) {
            throw std::runtime_error(ErrorHandler::handleGrpcError(status).toStdString());
        }
        
        for (const auto& chat : response.chats()) {
            result.chats.append(convertChat(chat));
        }
        result.hasMore = response.has_more();
        
        return result;
    }
};
```

### Error Handling

```cpp
class ErrorHandler {
public:
    static QString handleGrpcError(const grpc::Status& status) {
        switch (status.error_code()) {
            case grpc::UNAVAILABLE:
                return connectionError();
            case grpc::DEADLINE_EXCEEDED:
                return timeoutError();
            // ...
        }
    }
};

// Использование
try {
    auto result = messagesClient->listChats(0, 30, accessToken);
} catch (const std::exception& e) {
    showError(QString::fromStdString(e.what()));
}
```

### Authentication Flow

```cpp
// Device metadata в каждом запросе
std::unique_ptr<grpc::ClientContext> GrpcClient::createContext(const QString& accessToken) {
    auto context = std::make_unique<grpc::ClientContext>();
    
    // Base64 encoded metadata
    context->AddMetadata("x-device-id", toBase64(deviceMetadata_.deviceId));
    context->AddMetadata("x-device-name", toBase64(deviceMetadata_.deviceName));
    context->AddMetadata("x-os-name", toBase64(deviceMetadata_.os));
    
    // Auth token
    if (!accessToken.isEmpty()) {
        context->AddMetadata("x-auth-token", accessToken.toStdString());
    }
    
    // Deadline
    context->set_deadline(std::chrono::system_clock::now() + std::chrono::seconds(30));
    
    return context;
}
```

---

## Примеры кода

### Создание нового виджета

```cpp
#pragma once

#include <QWidget>

namespace BarkFluff {

/**
 * @brief Пример кастомного виджета
 */
class MyWidget : public QWidget {
    Q_OBJECT
    Q_PROPERTY(QString value READ value WRITE setValue NOTIFY valueChanged)
    
public:
    explicit MyWidget(QWidget* parent = nullptr);
    
    QString value() const { return value_; }
    void setValue(const QString& value);
    
    QSize sizeHint() const override;
    
signals:
    void valueChanged(const QString& value);
    void clicked();
    
protected:
    void paintEvent(QPaintEvent* event) override;
    void mousePressEvent(QMouseEvent* event) override;
    
private:
    void setupUI();
    
    QString value_;
    QLabel* label_ = nullptr;
};

} // namespace BarkFluff
```

### Добавление нового gRPC клиента

1. Создать класс, наследующий GrpcClient:

```cpp
// NewServiceClient.h
#pragma once

#include "GrpcClient.h"
#include "new_service.grpc.pb.h"

namespace BarkFluff {

class NewServiceClient : public GrpcClient {
    std::unique_ptr<barkfluff::newservice::NewService::Stub> stub_;
    
public:
    NewServiceClient(const ServiceInfo& service);
    
    struct Result {
        bool success = false;
        QString data;
    };
    
    Result doSomething(const QString& param, const QString& accessToken);
};

} // namespace BarkFluff
```

2. Реализовать методы:

```cpp
// NewServiceClient.cpp
#include "NewServiceClient.h"
#include "Utils/ErrorHandler.h"

namespace BarkFluff {

NewServiceClient::NewServiceClient(const ServiceInfo& service)
    : GrpcClient(service.host, service.port, service.tls)
{
    stub_ = barkfluff::newservice::NewService::NewStub(channel_);
}

NewServiceClient::Result NewServiceClient::doSomething(const QString& param, const QString& accessToken) {
    Result result;
    
    auto context = createContext(accessToken);
    barkfluff::newservice::DoSomethingRequest request;
    request.set_param(param.toStdString());
    
    barkfluff::newservice::DoSomethingResponse response;
    grpc::Status status = stub_->DoSomething(context.get(), request, &response);
    
    if (!status.ok()) {
        throw std::runtime_error(ErrorHandler::handleGrpcError(status).toStdString());
    }
    
    result.success = response.success();
    result.data = QString::fromStdString(response.data());
    
    return result;
}

} // namespace BarkFluff
```

3. Добавить в CMakeLists.txt:

```cmake
set(SOURCES
    # ...
    src/Connection/NewServiceClient.cpp
)

set(HEADERS
    # ...
    src/Connection/NewServiceClient.h
)
```

### Создание Model

```cpp
#pragma once

#include <QString>
#include <QVariant>

namespace BarkFluff {

/**
 * @brief Модель данных примера
 */
class MyModel {
public:
    QString id;
    QString name;
    qint64 timestamp = 0;
    
    // Serialization для кэширования
    QVariant toVariant() const {
        QVariantMap map;
        map["id"] = id;
        map["name"] = name;
        map["timestamp"] = timestamp;
        return map;
    }
    
    static MyModel fromVariant(const QVariant& variant) {
        MyModel model;
        QVariantMap map = variant.toMap();
        model.id = map["id"].toString();
        model.name = map["name"].toString();
        model.timestamp = map["timestamp"].toLongLong();
        return model;
    }
};

} // namespace BarkFluff

Q_DECLARE_METATYPE(BarkFluff::MyModel)
```

---

## Сборка

```bash
# Установка зависимостей (Debian/Ubuntu)
./install_deps.sh

# Сборка
mkdir build && cd build
cmake ..
make -j$(nproc)

# Запуск
./BarkFluffQt
```

## Структура Proto файлов

Proto файлы находятся в `../BarkFluffBackend/Shared/BarkFluff.Proto/`:
- `shared.proto` - общие типы
- `navigator_api.proto` - навигация по серверам
- `identity_api.proto` - авторизация
- `messages_api.proto` - сообщения
- `files_api.proto` - файлы
- `users_api.proto` - пользователи
- `onliner_api.proto` - онлайн-статусы
- `updates_api.proto` - серверные события