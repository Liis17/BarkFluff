# План разработки: Системные уведомления для BarkFluffQt

## Обзор

Реализация поддержки системных уведомлений о входящих сообщениях для Linux с использованием D-Bus (org.freedesktop.Notifications) и QSystemTrayIcon в качестве fallback.

## Требования

- **Целевая платформа**: Linux
- **Метод уведомлений**: D-Bus (основной), QSystemTrayIcon (fallback)
- **Звук**: Системный (через D-Bus hint `sound-name`)
- **Функционал**:
  - Отображение аватара отправителя
  - Клик по уведомлению открывает чат
  - Кнопка "Прочитано" в уведомлении
  - Не показывать, если окно активно и чат открыт
  - Не показывать для своих сообщений
  - Группировка не требуется

---

## Архитектура

```
┌─────────────────────────────────────────────────────────────────┐
│                      NotificationService                         │
│  (Синглтон, управляет показом уведомлений)                       │
├─────────────────────────────────────────────────────────────────┤
│  ┌─────────────────┐    ┌──────────────────┐                    │
│  │ LinuxDBusBackend│    │ SystemTrayBackend│                    │
│  │ (org.freedesktop│    │ (QSystemTrayIcon │                    │
│  │  .Notifications)│    │  ::showMessage)  │                    │
│  └────────┬────────┘    └────────┬─────────┘                    │
│           │ D-Bus primary      │ Fallback                       │
│           └────────────────────┴─────────────────────────────── │
└─────────────────────────────────────────────────────────────────┘
                                    │
                                    ▼
┌─────────────────────────────────────────────────────────────────┐
│                       MessengerPage                              │
│  onNewMessage() → NotificationService::showMessageNotification()│
└─────────────────────────────────────────────────────────────────┘
```

---

## Файловая структура

```
src/
├── Services/
│   ├── NotificationService.h       # Интерфейс + синглтон
│   ├── NotificationService.cpp
│   └── (существующие FileCacheService, SessionManager)
├── Models/
│   └── NotificationSettings.h      # Модель настроек уведомлений
├── Storage/
│   └── AppSettings.h/cpp           # + расширить для настроек уведомлений
└── UI/Settings/
    └── GeneralSettingsWidget.h/cpp # UI настроек уведомлений
```

---

## Этапы разработки

### 1. Модель настроек уведомлений

**Файл**: `Models/NotificationSettings.h`

```cpp
#pragma once

namespace BarkFluff {

/**
 * @brief Настройки уведомлений
 */
struct NotificationSettings {
    bool enabled = true;       // Уведомления вкл/выкл
    bool showPreview = true;   // Показывать текст сообщения
    bool showAvatar = true;    // Показывать аватар отправителя
    // Звук — системный, не настраивается отдельно
    
    bool operator==(const NotificationSettings& other) const = default;
};

} // namespace BarkFluff
```

**Расширить `AppSettings`**:
- Добавить `NotificationSettings getNotificationSettings() const`
- Добавить `void setNotificationSettings(const NotificationSettings&)`
- Сохранение/загрузка в JSON

---

### 2. Сервис уведомлений

**Файл**: `Services/NotificationService.h`

```cpp
#pragma once

#include "Models/NotificationSettings.h"
#include <QObject>
#include <QPixmap>
#include <QDBusInterface>

class QSystemTrayIcon;

namespace BarkFluff {

/**
 * @brief Сервис системных уведомлений
 * 
 * Использует D-Bus (org.freedesktop.Notifications) как основной метод,
 * QSystemTrayIcon как fallback.
 */
class NotificationService : public QObject {
    Q_OBJECT
    
public:
    static NotificationService& instance();
    
    /// Инициализация (вызвать при старте приложения)
    void initialize();
    
    /// Установить главное окно (для проверки активности)
    void setMainWindow(QWidget* window);
    
    /// Установить текущий открытый чат
    void setCurrentChatId(const QString& chatId);
    
    /// Показать уведомление о новом сообщении
    void showMessageNotification(
        const QString& chatId,
        const QString& chatName,
        const QString& senderName,
        const QString& messageText,
        const QPixmap& avatar = QPixmap()
    );
    
    /// Настройки
    void setSettings(const NotificationSettings& settings);
    NotificationSettings settings() const;
    
    /// Проверка, нужно ли показывать уведомление
    bool shouldShowNotification(const QString& chatId, qint64 senderId, qint64 currentUserId) const;
    
signals:
    /// Клик по уведомлению (открыть чат)
    void notificationClicked(const QString& chatId);
    
    /// Запрос "прочитано" из уведомления
    void markAsReadRequested(const QString& chatId);

private slots:
    void onDBusActionInvoked(uint notificationId, const QString& actionKey);
    void onDBusNotificationClosed(uint notificationId, uint reason);

private:
    NotificationService();
    ~NotificationService() override;
    
    NotificationService(const NotificationService&) = delete;
    NotificationService& operator=(const NotificationService&) = delete;
    
    /// Попытка показать через D-Bus
    bool tryDBusNotification(
        const QString& chatId,
        const QString& title,
        const QString& body,
        const QPixmap& avatar
    );
    
    /// Показ через QSystemTrayIcon (fallback)
    void showTrayNotification(
        const QString& title,
        const QString& body,
        const QPixmap& avatar
    );
    
    /// Проверка, активно ли главное окно
    bool isMainWindowActive() const;
    
    /// Проверка, является ли чат текущим открытым
    bool isCurrentChat(const QString& chatId) const;
    
    /// Преобразование QPixmap в D-Bus image hint
    QVariantMap createImageHint(const QPixmap& pixmap) const;
    
    QWidget* mainWindow_ = nullptr;
    QString currentChatId_;
    NotificationSettings settings_;
    
    // D-Bus
    QDBusInterface* notifyInterface_ = nullptr;
    uint lastNotificationId_ = 0;
    QString lastNotificationChatId_;
    bool dbusAvailable_ = false;
    
    // Fallback
    QSystemTrayIcon* trayIcon_ = nullptr;
};

} // namespace BarkFluff
```

**D-Bus спецификация** (org.freedesktop.Notifications):

Метод `Notify`:
```
uint Notify(
    string app_name,        // "BarkFluff"
    uint replaces_id,       // 0 или ID для замены
    string app_icon,        // "" (используем image-data)
    string summary,         // Заголовок (имя чата/отправителя)
    string body,            // Тело (текст сообщения)
    array[string] actions,  // ["default", "Открыть", "mark-read", "Прочитано"]
    dict hints,             // image-data, sound-name
    int expire_timeout      // -1 (по умолчанию)
)
```

Hints:
- `image-data` — аватар отправителя (структура: width, height, rowstride, has_alpha, bits_per_sample, channels, data)
- `sound-name` — "message-new-instant" (системный звук)

Signals:
- `ActionInvoked(uint id, string action_key)` — обработка кликов
- `NotificationClosed(uint id, uint reason)` — очистка состояния

---

### 3. Интеграция с MessengerPage

**Изменения в `MessengerPage.h`**:
```cpp
// Добавить метод для уведомлений о текущем чате
void setCurrentChatForNotifications(const QString& chatId);
```

**Изменения в `MessengerPage.cpp`**:
```cpp
#include "Services/NotificationService.h"

void MessengerPage::onNewMessage(const NewMessageEvent& event) {
    // ... существующий код обновления UI ...
    
    // Показать уведомление
    auto& notify = NotificationService::instance();
    qint64 currentUserId = session_.userId(); // или другой способ получить
    
    if (notify.shouldShowNotification(event.chatId, event.message.senderId(), currentUserId)) {
        QString chatName = getChatName(event.chatId);
        QString senderName = event.message.senderName();
        QString text = settings().showPreview() ? event.message.text() : "Новое сообщение";
        QPixmap avatar = getAvatarForChat(event.chatId);
        
        notify.showMessageNotification(
            event.chatId, chatName, senderName, text, avatar
        );
    }
}

void MessengerPage::onChatSelected(const Chat& chat) {
    // ... существующий код ...
    
    // Уведомить сервис уведомлений о текущем чате
    NotificationService::instance().setCurrentChatId(chat.id());
}

void MessengerPage::setCurrentChatForNotifications(const QString& chatId) {
    NotificationService::instance().setCurrentChatId(chatId);
}
```

---

### 4. Интеграция с MainWindow

**Изменения в `MainWindow.h`**:
```cpp
private slots:
    void onNotificationClicked(const QString& chatId);
    void onMarkAsReadRequested(const QString& chatId);
```

**Изменения в `MainWindow.cpp`**:
```cpp
#include "Services/NotificationService.h"

void MainWindow::setupMessenger() {
    // ... существующий код ...
    
    // Инициализация уведомлений
    auto& notify = NotificationService::instance();
    notify.setMainWindow(this);
    
    connect(&notify, &NotificationService::notificationClicked,
            this, &MainWindow::onNotificationClicked);
    connect(&notify, &NotificationService::markAsReadRequested,
            this, &MainWindow::onMarkAsReadRequested);
}

void MainWindow::onNotificationClicked(const QString& chatId) {
    // Активировать окно
    show();
    activateWindow();
    raise();
    
    // Открыть нужный чат
    if (messengerPage_) {
        messengerPage_->openChatById(chatId);
    }
}

void MainWindow::onMarkAsReadRequested(const QString& chatId) {
    // Отправить mark as read через MessagesClient
    if (messengerPage_) {
        messengerPage_->markChatAsRead(chatId);
    }
}
```

---

### 5. UI настроек

**Файл**: `UI/Settings/GeneralSettingsWidget.h`

```cpp
#pragma once

#include <QWidget>

class QCheckBox;

namespace BarkFluff {

class GeneralSettingsWidget : public QWidget {
    Q_OBJECT

public:
    explicit GeneralSettingsWidget(QWidget* parent = nullptr);

private slots:
    void onEnabledChanged(int state);
    void onPreviewChanged(int state);
    void onAvatarChanged(int state);

private:
    void setupUI();
    void loadSettings();
    void saveSettings();
    
    QCheckBox* enabledCheck_ = nullptr;
    QCheckBox* previewCheck_ = nullptr;
    QCheckBox* avatarCheck_ = nullptr;
};

} // namespace BarkFluff
```

**Файл**: `UI/Settings/GeneralSettingsWidget.cpp`

```cpp
#include "GeneralSettingsWidget.h"
#include "Storage/AppSettings.h"
#include "Services/NotificationService.h"

#include <QVBoxLayout>
#include <QCheckBox>
#include <QLabel>
#include <QGroupBox>

namespace BarkFluff {

GeneralSettingsWidget::GeneralSettingsWidget(QWidget* parent)
    : QWidget(parent)
{
    setupUI();
    loadSettings();
}

void GeneralSettingsWidget::setupUI() {
    auto* layout = new QVBoxLayout(this);
    layout->setContentsMargins(0, 0, 0, 0);
    
    // Группа уведомлений
    auto* groupBox = new QGroupBox(tr("Уведомления"));
    auto* groupLayout = new QVBoxLayout(groupBox);
    
    enabledCheck_ = new QCheckBox(tr("Показывать уведомления о сообщениях"));
    previewCheck_ = new QCheckBox(tr("Показывать текст сообщения"));
    avatarCheck_ = new QCheckBox(tr("Показывать аватар отправителя"));
    
    groupLayout->addWidget(enabledCheck_);
    groupLayout->addWidget(previewCheck_);
    groupLayout->addWidget(avatarCheck_);
    
    // Примечание о звуке
    auto* soundNote = new QLabel(
        tr("Звук уведомления используется системный")
    );
    soundNote->setStyleSheet("color: gray; font-style: italic;");
    groupLayout->addWidget(soundNote);
    
    layout->addWidget(groupBox);
    layout->addStretch();
    
    // Связи
    connect(enabledCheck_, &QCheckBox::stateChanged, this, &GeneralSettingsWidget::onEnabledChanged);
    connect(previewCheck_, &QCheckBox::stateChanged, this, &GeneralSettingsWidget::onPreviewChanged);
    connect(avatarCheck_, &QCheckBox::stateChanged, this, &GeneralSettingsWidget::onAvatarChanged);
    
    // Зависимость настроек
    connect(enabledCheck_, &QCheckBox::toggled, previewCheck_, &QCheckBox::setEnabled);
    connect(enabledCheck_, &QCheckBox::toggled, avatarCheck_, &QCheckBox::setEnabled);
}

void GeneralSettingsWidget::loadSettings() {
    auto settings = AppSettings::instance().getNotificationSettings();
    
    enabledCheck_->setChecked(settings.enabled);
    previewCheck_->setChecked(settings.showPreview);
    avatarCheck_->setChecked(settings.showAvatar);
    
    previewCheck_->setEnabled(settings.enabled);
    avatarCheck_->setEnabled(settings.enabled);
}

void GeneralSettingsWidget::saveSettings() {
    NotificationSettings settings;
    settings.enabled = enabledCheck_->isChecked();
    settings.showPreview = previewCheck_->isChecked();
    settings.showAvatar = avatarCheck_->isChecked();
    
    AppSettings::instance().setNotificationSettings(settings);
    NotificationService::instance().setSettings(settings);
}

void GeneralSettingsWidget::onEnabledChanged(int state) {
    Q_UNUSED(state);
    saveSettings();
}

void GeneralSettingsWidget::onPreviewChanged(int state) {
    Q_UNUSED(state);
    saveSettings();
}

void GeneralSettingsWidget::onAvatarChanged(int state) {
    Q_UNUSED(state);
    saveSettings();
}

} // namespace BarkFluff
```

---

### 6. Расширение AppSettings

**Добавить в `AppSettings.h`**:
```cpp
#include "Models/NotificationSettings.h"

class AppSettings {
public:
    // ... существующие методы ...
    
    NotificationSettings getNotificationSettings() const;
    void setNotificationSettings(const NotificationSettings& settings);
    
private:
    // ... существующие поля ...
    NotificationSettings notificationSettings_;
};
```

**Добавить в `AppSettings.cpp`**:
```cpp
NotificationSettings AppSettings::getNotificationSettings() const {
    return notificationSettings_;
}

void AppSettings::setNotificationSettings(const NotificationSettings& settings) {
    notificationSettings_ = settings;
    save();
}

// В fromJson():
if (json.contains("notifications")) {
    auto n = json["notifications"].toObject();
    notificationSettings_.enabled = n["enabled"].toBool(true);
    notificationSettings_.showPreview = n["showPreview"].toBool(true);
    notificationSettings_.showAvatar = n["showAvatar"].toBool(true);
}

// В toJson():
QJsonObject n;
n["enabled"] = notificationSettings_.enabled;
n["showPreview"] = notificationSettings_.showPreview;
n["showAvatar"] = notificationSettings_.showAvatar;
json["notifications"] = n;
```

---

### 7. Инициализация в main.cpp

```cpp
#include "Services/NotificationService.h"

int main(int argc, char *argv[]) {
    QApplication app(argc, argv);
    
    // ... существующая инициализация ...
    
    // Загрузка настроек уведомлений
    auto& notify = NotificationService::instance();
    notify.initialize();
    notify.setSettings(AppSettings::instance().getNotificationSettings());
    
    // ... создание MainWindow ...
    
    return app.exec();
}
```

---

### 8. CMakeLists.txt

Добавить зависимости:

```cmake
find_package(Qt6 REQUIRED COMPONENTS
    Core
    Widgets
    Network
    Svg
    Concurrent
    Multimedia
    MultimediaWidgets
    DBus    # <-- Добавить
)

# В SOURCES добавить:
src/Services/NotificationService.cpp

# В HEADERS добавить:
src/Services/NotificationService.h
src/Models/NotificationSettings.h

# В target_link_libraries добавить:
Qt6::DBus
```

---

## Тестирование

### Чек-лист

1. [ ] D-Bus уведомление с аватаром появляется
2. [ ] Клик по уведомлению открывает нужный чат
3. [ ] Кнопка "Прочитано" работает (отправляет mark as read)
4. [ ] Fallback на QSystemTrayIcon при отсутствии D-Bus
5. [ ] Не показывать, если окно активно и чат открыт
6. [ ] Не показывать для своих сообщений
7. [ ] Настройки сохраняются между запусками
8. [ ] При выключенных уведомлениях не показываются
9. [ ] При скрытом тексте показывается "Новое сообщение"
10. [ ] Без аватара показывается иконка приложения

### Тестирование D-Bus

Проверить доступность D-Bus notifications:
```bash
gdbus call --session --dest org.freedesktop.Notifications \
    --object-path /org/freedesktop/Notifications \
    --method org.freedesktop.Notifications.Notify \
    "TestApp" 0 "" "Test Title" "Test Body" \
    "[]" "[]" 5000
```

---

## Оценка объёма

| Компонент | Файлов | Сложность |
|-----------|--------|-----------|
| NotificationSettings | 1 (заголовок) | Низкая |
| AppSettings расширение | 2 | Низкая |
| NotificationService | 2 | Средняя |
| GeneralSettingsWidget | 2 | Низкая |
| Интеграция (MessengerPage, MainWindow, main) | 3 | Низкая |
| CMakeLists.txt | 1 | Низкая |
| **Итого** | ~11 файлов | ~1-2 дня |

---

## Примечания

- Системный звук используется через D-Bus hint `sound-name` со значением "message-new-instant"
- Для QSystemTrayIcon fallback звук не воспроизводится (системное поведение)
- Аватар передаётся как raw image data в формате ARGB32 через hint `image-data`