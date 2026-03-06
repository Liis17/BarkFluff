# План: Просмотр профиля пользователя (UserProfileView)

## Обзор

Страница просмотра профиля пользователя или группы. Открывается:
- Из чата (клик на аватар в header)
- Из списка участников группы
- Из "Saved Messages" / "Чат с собой" (свой профиль в режиме просмотра)

## Ссылки на связанные планы

- **SHARED_MEDIA_PLAN.md** - Общие файлы чата (медиа, документы)
- **MESSENGER_PLAN.md** - Мессенджер (базовая структура)
- **AUTH_PLAN.md** - Авторизация (модели User, Session)

---

## Этап 1: Модели данных

### 1.1 Добавить модели бейджей в `Models/User.h`

```cpp
// Badge - тип баджа
struct Badge {
    qint32 id = 0;
    QString name;
    QString description;
    QString imageUrl;
    QDateTime createdDate;
    bool isActive = true;
};

// UserBadge - бадж пользователя с приоритетом
struct UserBadge {
    Badge badge;
    qint32 priority = 1000;
    QDateTime assignedDate;
    
    // Для сортировки по приоритету
    bool operator<(const UserBadge& other) const {
        return priority < other.priority;
    }
};
```

### 1.2 Обновить модель User

Добавить поля:
```cpp
class User {
    // ... существующие поля ...
    QList<UserBadge> badges;      // Бейджи пользователя
    qint32 storageLimitGb = 0;    // Лимит хранилища в ГБ
};
```

### 1.3 Добавить метод в `Connection/UsersClient.h`

```cpp
// Получить бейджи пользователя
QList<UserBadge> getUserBadges(qint64 userId, const QString& accessToken, 
                                std::optional<qint32> limit = std::nullopt);
```

---

## Этап 2: UserProfileView - страница просмотра

### 2.1 Файлы

| Файл | Описание |
|------|----------|
| `UI/UserProfileView.h` | Заголовок |
| `UI/UserProfileView.cpp` | Реализация |

### 2.2 Конструктор

```cpp
class UserProfileView : public QWidget {
    Q_OBJECT
public:
    // Просмотр профиля из чата
    explicit UserProfileView(
        const ServiceInfo& usersConfig,
        const ServiceInfo& onlinerConfig,
        const QString& accessToken,
        qint64 currentUserId,    // ID текущего пользователя
        const Chat& chat,        // Чат (для определения типа и участников)
        QWidget* parent = nullptr
    );
    
signals:
    void backRequested();
    void openChatRequested(qint64 chatId);  // Для перехода в чат из профиля
    void editProfileRequested();            // Для редактирования своего профиля
};
```

### 2.3 Структура UI

```
┌─────────────────────────────────────┐
│ Header: ← Профиль                   │
├─────────────────────────────────────┤
│                                     │
│         [Аватар 96x96]              │
│         Иван Иванов                 │
│         @ivan_ivanov                │
│         ● В сети                    │  <- OnlineStatusWidget
│                                     │
├─────────────────────────────────────┤
│ О себе                              │
│ "Разработчик из Москвы..."          │
│                                     │
│ Значки                              │
│ [⭐verified] [beta-tester]          │  <- FlowLayout бейджей
│                                     │
│ ID пользователя                     │
│ 12345                               │
│                                     │
│ Дата регистрации                    │
│ 15 января 2024                      │
│                                     │
├─────────────────────────────────────┤
│ [Отправить сообщение]               │  <- Только для чужих профилей
│ [Редактировать профиль]             │  <- Только для своего профиля
│                                     │
├─────────────────────────────────────┤
│ Участники (5)              ▼        │  <- Только для групп
│ ┌───────────────────────────────┐   │
│ │ [avatar] Иван Иванов (admin)  │   │
│ │ [avatar] Петр Петров          │   │
│ │ [avatar] ...                  │   │
│ │ Показать всех...              │   │
│ └───────────────────────────────┘   │
│                                     │
├─────────────────────────────────────┤
│ Вложения                            │
│ [Медиа] [Файлы]                     │  <- Заглушка
│                                     │
│ Функция общих файлов чата будет     │
│ реализована в отдельной задаче.     │
│ См. SHARED_MEDIA_PLAN.md            │
│                                     │
└─────────────────────────────────────┘
```

### 2.4 Секции

#### Header Section (ProfileHeaderWidget)
- Аватар 96x96 (кликабельный для полного размера)
- Имя/Фамилия (или название группы)
- Username (для DM)
- Онлайн-статус (OnlineStatusWidget)
- Количество участников (для групп)

#### Info Section (ProfileInfoWidget)
- Био (если есть)
- Бейджи (FlowLayout с BadgeChipWidget)
- ID пользователя
- Дата регистрации (русский формат)
- Лимит хранилища (если > 0)

#### Actions Section
- Для DM: "Отправить сообщение" (закрыть профиль, фокус на input)
- Для своего профиля: "Редактировать профиль" (открыть ProfilePage)

#### Members Section (только для групп)
- Список участников (первые 10)
- Кнопка "Показать всех" (открывает MembersListView)
- Пагинация при скролле

#### Shared Media Section (заглушка)
- Табы "Медиа" / "Файлы" (disabled)
- Placeholder с текстом: "Общие файлы чата будут добавлены позже. См. SHARED_MEDIA_PLAN.md"

---

## Этап 3: Виджеты

### 3.1 BadgeChipWidget

```cpp
// UI/Widgets/BadgeChipWidget.h
class BadgeChipWidget : public QWidget {
    Q_OBJECT
public:
    explicit BadgeChipWidget(const UserBadge& badge, QWidget* parent = nullptr);
    
private:
    QLabel* iconLabel_;
    QLabel* nameLabel_;
};
```

Стиль: скругленный чип с иконкой и названием, tooltip с описанием.

### 3.2 MembersListView (опционально, для групп)

Отдельный диалог/виджет для отображения всех участников с пагинацией.

---

## Этап 4: Интеграция

### 4.1 Открытие из ConversationWidget

В `MessengerPage.cpp` добавить обработку клика на аватар в header чата:

```cpp
// В ConversationWidget::setupHeader()
connect(avatarWidget_, &AvatarWidget::clicked, this, [this]() {
    emit profileRequested(chat_);
});

// В MessengerPage
connect(conversationWidget_, &ConversationWidget::profileRequested, 
        this, &MessengerPage::showUserProfile);
```

### 4.2 Определение "свой/чужой" профиль

```cpp
bool isOwnProfile = (otherMember.userId == currentUserId);
if (isOwnProfile) {
    // Показать кнопку "Редактировать"
} else {
    // Показать кнопку "Отправить сообщение"
}
```

### 4.3 Сохранённые сообщения / Чат с собой

Если chat.id == currentUserId или специальный флаг, открывать профиль в режиме просмотра с кнопкой редактирования.

---

## Этап 5: gRPC вызовы

### 5.1 Загрузка профиля

```cpp
void UserProfileView::loadProfile() {
    // 1. Получить информацию о пользователе
    auto user = usersClient_->getUser(userId_, accessToken_);
    
    // 2. Получить все бейджи (limit = nullopt для всех)
    auto badges = usersClient_->getUserBadges(userId_, accessToken_, std::nullopt);
    user.badges = badges;
    
    // 3. Подписаться на онлайн-статус (для DM)
    if (!chat_.isGroupChat) {
        onlinerClient_->subscribeToStatus(userId_);
    }
}
```

### 5.2 Участники группы

```cpp
void UserProfileView::loadMembers() {
    // Использовать MessagesClient::listChatMembers()
    auto result = messagesClient_->listChatMembers(chat_.id, accessToken_, 0, 10);
    members_ = result.items;
    memberCount_ = result.totalCount;
}
```

---

## Чек-лист реализации

### Базовая версия (MVP)
- [ ] Модели Badge, UserBadge в User.h
- [ ] UsersClient::getUserBadges()
- [ ] UserProfileView.h/cpp (скелет)
- [ ] ProfileHeaderWidget (аватар, имя, статус)
- [ ] ProfileInfoWidget (био, ID, дата)
- [ ] Интеграция: открытие из чата

### Расширенная версия
- [ ] BadgeChipWidget с иконками
- [ ] Members Section для групп
- [ ] Кнопка "Редактировать" для своего профиля
- [ ] Подписка на онлайн-статус

### Future (отдельные задачи)
- [ ] Shared Media (см. SHARED_MEDIA_PLAN.md)
- [ ] Полноэкранный просмотр аватара
- [ ] Поиск по участникам группы

---

## Файлы для создания/изменения

### Новые файлы
- `BarkFluffQt/src/UI/UserProfileView.h`
- `BarkFluffQt/src/UI/UserProfileView.cpp`
- `BarkFluffQt/src/UI/Widgets/BadgeChipWidget.h`
- `BarkFluffQt/src/UI/Widgets/BadgeChipWidget.cpp`

### Изменяемые файлы
- `BarkFluffQt/src/Models/User.h` - добавить Badge, UserBadge, поля
- `BarkFluffQt/src/Connection/UsersClient.h` - добавить getUserBadges()
- `BarkFluffQt/src/Connection/UsersClient.cpp` - реализация
- `BarkFluffQt/src/UI/MessengerPage.h` - сигналы для открытия профиля
- `BarkFluffQt/src/UI/MessengerPage.cpp` - интеграция
- `BarkFluffQt/CMakeLists.txt` - добавить новые файлы