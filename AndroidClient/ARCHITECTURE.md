# Архитектура Android клиента BarkFluff

## Обзор

Android клиент построен с использованием **Clean Architecture** и следует принципам **SOLID**. Приложение разделено на три основных слоя с четкими границами и зависимостями.

## Слои архитектуры

```
┌─────────────────────────────────────┐
│     Presentation Layer              │
│  (ViewModels + Compose UI)          │
└─────────────────────────────────────┘
              ↓ depends on
┌─────────────────────────────────────┐
│       Domain Layer                  │
│   (Models + Use Cases)              │
└─────────────────────────────────────┘
              ↓ depends on
┌─────────────────────────────────────┐
│        Data Layer                   │
│  (Repositories + Data Sources)      │
└─────────────────────────────────────┘
```

## 1. Presentation Layer

### Компоненты:
- **ViewModels** - управление состоянием UI
- **UI Screens** - Jetpack Compose экраны
- **Navigation** - навигация между экранами

### ViewModels:

#### `AuthViewModel`
- Управляет процессами аутентификации
- **State**: AuthState (isLoading, email, username, password, etc.)
- **Events**: LoginSuccess, RegisterSuccess, ConfirmSuccess, Error
- **Actions**: login(), register(), confirmAccount(), logout()

#### `ChatViewModel`
- Управляет списком чатов и сообщениями
- **State**: ChatListState, ChatState
- **Events**: Error, MessageSent, NewMessage
- **Actions**: loadChats(), loadChat(), sendMessage(), markAsRead(), createGroupChat()
- **Real-time**: subscribeToNewMessages() - gRPC streaming

#### `UserViewModel`
- Управляет профилями пользователей
- **State**: searchResults, currentUser
- **Actions**: searchUsers(), loadUser(), updateProfile()

### UI Screens:

#### Authentication Flow:
```
WelcomeScreen
    ↓
SelectServerScreen → (save server info)
    ↓
LoginScreen → ChatListScreen
    ↓ (or)
RegisterScreen → VerifyEmailScreen → ChatListScreen
```

#### Main App Flow:
```
ChatListScreen
    ├→ ChatScreen (individual chat)
    ├→ NewChatScreen (search users)
    ├→ NewGroupScreen (create group)
    ├→ MyProfileScreen
    │    └→ EditProfileScreen
    └→ SettingsScreen
```

## 2. Domain Layer

### Models:

#### User
```kotlin
data class User(
    val id: Long,
    val firstName: String,
    val lastName: String,
    val username: String,
    val bio: String?,
    val profilePictureUrl: String?,
    val registrationDate: Instant,
    val badges: List<UserBadge>
)
```

#### Chat
```kotlin
data class Chat(
    val id: String,
    val title: String,
    val pictureUrl: String?,
    val isGroupChat: Boolean,
    val lastMessage: Message?,
    val members: List<ChatMember>,
    val unreadCount: Int
)
```

#### Message
```kotlin
data class Message(
    val id: String,
    val senderId: Long,
    val chatId: String?,
    val content: MessageContent,
    val sentAt: Instant,
    val readBy: List<Long>,
    val type: MessageType
)
```

#### AuthToken
```kotlin
data class AuthToken(
    val accessToken: String,
    val refreshToken: String,
    val accessTokenExpiration: Instant,
    val refreshTokenExpiration: Instant
)
```

## 3. Data Layer

### Repositories:

#### `AuthRepository`
Отвечает за аутентификацию и управление сессией.

**Методы**:
- `login(emailOrUsername, password, otpCode?)` → Result<AuthToken>
- `register(email, username, password, firstName, lastName)` → Result<Unit>
- `confirmAccount(email, code)` → Result<AuthToken>
- `refreshToken()` → Result<AuthToken>
- `logout()` → Unit

**Зависимости**:
- Identity gRPC channel
- SessionManager

#### `ChatRepository`
Управляет чатами и сообщениями.

**Методы**:
- `getChats(offset, limit)` → Result<List<Chat>>
- `getMessages(chatId, fromMessageId?, limit)` → Result<List<Message>>
- `sendMessage(recipientId?, chatId?, text?, fileIds)` → Result<Message>
- `markAsRead(messageIds)` → Result<Unit>
- `createGroupChat(title, userIds)` → Result<Chat>
- `subscribeToNewMessages()` → Flow<Message> (gRPC streaming)

**Зависимости**:
- Messages gRPC channel
- Updates gRPC channel

#### `UserRepository`
Управляет пользователями и профилями.

**Методы**:
- `getUser(userId)` → Result<User>
- `searchUsers(query, offset, limit)` → Result<List<User>>
- `updateProfile(firstName?, lastName?, username?, bio?)` → Result<Unit>
- `checkUsernameAvailable(username)` → Result<Boolean>
- `checkEmailAvailable(email)` → Result<Boolean>

**Зависимости**:
- Users gRPC channel

#### `FileRepository`
Управляет загрузкой и скачиванием файлов.

**Методы**:
- `uploadFile(uri, fileType)` → Result<UploadResult>
- `getDownloadUrls(fileIds)` → Result<Map<String, String>>

**Зависимости**:
- Files gRPC channel
- OkHttpClient

### Data Sources:

#### gRPC Clients
Все gRPC клиенты создаются через Hilt DI с использованием `NetworkModule`.

**Каналы**:
- **BeaconChannel** - информация о сервере
- **IdentityChannel** - аутентификация
- **UsersChannel** - пользователи
- **FilesChannel** - файлы
- **MessagesChannel** - сообщения
- **UpdatesChannel** - real-time обновления

**Interceptors**:
- `AuthInterceptor` - добавляет JWT токен и метаданные к запросам
  - Authorization: Bearer {token}
  - X-Device-Id: {deviceId}
  - X-OS: Android {version}
  - X-App-Version: {version}

#### Local Storage

##### SessionManager (DataStore)
Безопасное хранение сессионных данных.

**Хранимые данные**:
- Access Token + Refresh Token
- Token expiration dates
- User ID
- Server Info (endpoints, colors)

**Flow API**:
```kotlin
val authToken: Flow<AuthToken?>
val serverInfo: Flow<ServerInfo?>
val userId: Flow<Long?>
```

## Dependency Injection (Hilt)

### Modules:

#### NetworkModule
Предоставляет gRPC channels и interceptors.

```kotlin
@Provides @Singleton
fun provideIdentityChannel(
    sessionManager: SessionManager,
    authInterceptor: AuthInterceptor
): ManagedChannel
```

## Паттерны и практики

### 1. Single Source of Truth
- SessionManager является единственным источником данных о сессии
- ViewModels являются единственным источником UI state

### 2. Unidirectional Data Flow
```
User Action → ViewModel → Repository → Data Source
    ↑                                        ↓
UI State ← State Update ← Success/Failure ←─┘
```

### 3. Error Handling
Все операции репозитория возвращают `Result<T>`:
```kotlin
suspend fun operation(): Result<T> =
    try {
        Result.success(data)
    } catch (e: Exception) {
        Result.failure(e)
    }
```

### 4. Reactive Programming
- **StateFlow** для UI state
- **SharedFlow** для events
- **Flow** для streaming data

### 5. Coroutines for Async
```kotlin
viewModelScope.launch {
    repository.getData()
        .onSuccess { data -> updateState(data) }
        .onFailure { error -> handleError(error) }
}
```

## Real-time Communication

### gRPC Streaming

Для получения новых сообщений в реальном времени:

```kotlin
fun subscribeToNewMessages(): Flow<Message> = flow {
    val stub = UpdatesApiGrpc.newBlockingStub(updatesChannel)
    val request = SubscribeNewMessagesRequest.newBuilder().build()
    val stream = stub.subscribeNewMessages(request)

    while (stream.hasNext()) {
        emit(convertToMessage(stream.next()))
    }
}
```

ViewModel подписывается при инициализации:
```kotlin
init {
    subscribeToMessages()
}

private fun subscribeToMessages() {
    viewModelScope.launch {
        chatRepository.subscribeToNewMessages()
            .catch { error -> handleError(error) }
            .collect { message -> handleNewMessage(message) }
    }
}
```

## Security

### Token Management
1. Access Token хранится в DataStore (encrypted)
2. Автоматическое обновление при истечении
3. Refresh Token для получения нового Access Token

### Network Security
1. TLS для gRPC соединений (опционально)
2. Certificate pinning (можно добавить)
3. Cleartext traffic только для dev

## Performance Optimization

### 1. Lazy Loading
- Чаты загружаются с пагинацией (offset + limit)
- Сообщения подгружаются по мере прокрутки

### 2. Caching
- Images cached by Coil
- Session data in DataStore
- Room for offline support (планируется)

### 3. Background Work
- WorkManager for синхронизации
- Foreground Service для persistent connection

## Testing Strategy

### Unit Tests
- ViewModels logic
- Repository operations
- Domain models

### Integration Tests
- gRPC communication
- Database operations

### UI Tests
- Navigation flows
- Screen interactions

## Future Improvements

1. **Offline Support**: Room database for caching
2. **Push Notifications**: FCM integration
3. **Voice Messages**: Audio recording and playback
4. **Video Calls**: WebRTC integration
5. **End-to-End Encryption**: Signal Protocol
6. **Multi-account**: Support multiple accounts
7. **Themes**: Custom themes and colors
8. **Animations**: More smooth transitions
