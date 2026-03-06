#pragma once

#include <QWidget>
#include <QSplitter>
#include <QStackedWidget>
#include <QLabel>
#include <QListWidget>
#include <QScrollArea>
#include <QLineEdit>
#include <QTextEdit>
#include <QPushButton>
#include <QTimer>
#include <QSet>
#include <QFutureWatcher>
#include <QHttpMultiPart>
#include <QFileDialog>
#include <QClipboard>
#include <QMimeData>

#include "Models/Chat.h"
#include "Models/Message.h"
#include "Models/SelectedAttachment.h"
#include "Models/User.h"
#include "Connection/MessagesClient.h"
#include "Connection/UpdatesClient.h"
#include "Connection/FilesClient.h"
#include "Connection/UsersClient.h"
#include "Models/UserSession.h"
#include "Models/ServerConfiguration.h"
#include "Utils/MessageGrouper.h"
#include "UI/Widgets/AttachmentWidgets.h"
#include "UI/Widgets/EmojiPicker.h"
#include "UI/Widgets/ScrollToBottomButton.h"
#include "UI/Widgets/OnlineStatusWidget.h"
#include "UI/Widgets/MessageBubbleShape.h"
#include "UI/Widgets/ExpandingTextEdit.h"
#include "Connection/OnlinerClient.h"
#include "Utils/ImageOptimizer.h"
#include "UI/ProfilePage.h"
#include "UI/UserProfileView.h"
#include "UI/Widgets/AvatarWidget.h"

namespace BarkFluff {

class ChatListWidget;
class ConversationWidget;
class MessageInputWidget;

/**
 * @brief Главная страница мессенджера
 * 
 * Содержит split-view с списком чатов слева и разговором справа.
 */
class MessengerPage : public QWidget {
    Q_OBJECT
    
public:
    explicit MessengerPage(
        const ServerConfiguration& config,
        const UserSession& session,
        QWidget* parent = nullptr
    );
    ~MessengerPage() override;
    
    /// Открыть чат по ID (для уведомлений)
    void openChatById(const QString& chatId);
    
    /// Пометить чат как прочитанный
    void markChatAsRead(const QString& chatId);
    
signals:
    void logoutRequested();
    void settingsRequested();
    
private slots:
    void onChatSelected(const Chat& chat);
    void onUserSelected(const User& user);
    void onSearchRequested(const QString& query);
    void onNewMessage(const NewMessageEvent& event);
    void onMessageRead(const MessageReadEvent& event);
    void onMessageSent(const QString& text);
    void refreshChats();
    void performSearch();
    void onProfileRequested();
    void onProfileBack();
    void onUserProfileRequested();
    void onUserProfileBack();
    void onEditProfileFromView();
    
private:
    Chat createChatFromUser(const User& user, const QString& chatId = QString());
    void setupUI();
    void loadChats();
    void updateChatInList(const Chat& chat);
    void updateMessageReadStatus(const QString& chatId, qint64 messageId, const QList<qint64>& readBy);
    
    // Services
    std::unique_ptr<MessagesClient> messagesClient_;
    std::unique_ptr<UpdatesClient> updatesClient_;
    std::unique_ptr<UsersClient> usersClient_;
    OnlinerClient* onlinerClient_ = nullptr;
    ServerConfiguration config_;
    UserSession session_;
    
    // State
    QList<Chat> chats_;
    Chat selectedChat_;
    bool isLoading_ = false;
    
    // Search state
    QString currentSearchQuery_;
    QTimer* searchDebounceTimer_ = nullptr;
    
    // UI components
    QSplitter* splitter_ = nullptr;
    ChatListWidget* chatList_ = nullptr;
    ConversationWidget* conversation_ = nullptr;
    QLabel* emptyStateLabel_ = nullptr;
    QStackedWidget* rightStack_ = nullptr;
    ProfilePage* profilePage_ = nullptr;
    UserProfileView* userProfileView_ = nullptr;
    
    // Refresh timer
    QTimer* refreshTimer_ = nullptr;
};

/**
 * @brief Виджет строки пользователя в результатах поиска
 */
class UserSearchRowWidget : public QWidget {
    Q_OBJECT
    
public:
    explicit UserSearchRowWidget(const User& user, QWidget* parent = nullptr);
    
private:
    void setupUI();
    void loadAvatar();
    
    User user_;
    QLabel* avatarLabel_ = nullptr;
    QLabel* nameLabel_ = nullptr;
    QLabel* usernameLabel_ = nullptr;
};

/**
 * @brief Виджет списка чатов с поиском
 */
class ChatListWidget : public QWidget {
    Q_OBJECT
    
public:
    explicit ChatListWidget(QWidget* parent = nullptr);
    
    void setChats(const QList<Chat>& chats);
    void updateChat(const Chat& chat);
    void clearSelection();
    void setSearchResults(const QList<User>& users);
    void clearSearchResults();
    
    QLineEdit* searchEdit() const { return searchEdit_; }
    
signals:
    void chatSelected(const Chat& chat);
    void userSelected(const User& user);
    void searchRequested(const QString& query);
    void profileRequested();
    void settingsRequested();
    
private slots:
    void onItemClicked(QListWidgetItem* item);
    void onSearchItemClicked(QListWidgetItem* item);
    void onSearchTextChanged(const QString& text);
    
private:
    void sortChats();
    void applyFilter(const QString& filter);
    
    QLineEdit* searchEdit_ = nullptr;
    QListWidget* searchResultsWidget_ = nullptr;
    QLabel* searchSectionLabel_ = nullptr;
    QLabel* chatsSectionLabel_ = nullptr;
    QListWidget* listWidget_ = nullptr;
    
    QList<Chat> chats_;
    QList<Chat> filteredChats_;
    QList<User> searchResults_;
};

/**
 * @brief Виджет строки чата в списке
 */
class ChatRowWidget : public QWidget {
    Q_OBJECT
    
public:
    explicit ChatRowWidget(const Chat& chat, QWidget* parent = nullptr);
    
    void updateChat(const Chat& chat);
    
private:
    void setupUI();
    void loadAvatar();
    
    Chat chat_;
    QLabel* avatarLabel_ = nullptr;
    QLabel* nameLabel_ = nullptr;
    QLabel* messageLabel_ = nullptr;
    QLabel* timeLabel_ = nullptr;
    QLabel* unreadLabel_ = nullptr;
};

/**
 * @brief Виджет переписки (сообщения + ввод)
 * 
 * Поддерживает:
 * - Optimistic UI (мгновенное отображение отправленных сообщений)
 * - Отправку с вложениями
 * - Группировку сообщений
 * - Дедупликацию сообщений
 */
class ConversationWidget : public QWidget {
    Q_OBJECT
    
public:
    explicit ConversationWidget(
        MessagesClient* messagesClient,
        FilesClient* filesClient,
        OnlinerClient* onlinerClient,
        qint64 currentUserId,
        const QString& accessToken,
        QWidget* parent = nullptr
    );
    
    void setChat(const Chat& chat);
    void addMessage(const Message& message);
    void replacePendingMessage(const QString& localID, const Message& confirmed);
    void updateMessageReadStatus(qint64 messageId, const QList<qint64>& readBy);
    void clear();
    bool isMessageConfirmed(qint64 messageId) const;  // Публичный метод для проверки дубликатов
    QList<qint64> loadedMessageIds() const;  // Список ID загруженных сообщений
    
signals:
    void messageSent(const QString& text);
    void messagesLoaded();  // Сигнал после загрузки сообщений
    void profileRequested();  // Сигнал для открытия профиля собеседника
    
private slots:
    void onSendClicked();
    void onAttachClicked();
    void onEmojiClicked();
    void onEmojiSelected(const QString& emoji);
    void onPasteFromClipboard();
    void onAttachmentsChanged(const QList<SelectedAttachment>& attachments);
    void onScrollValueChanged(int value);
    void onAttachmentClicked(const QList<MessageAttachment>& attachments, int clickedIndex);
    
protected:
    void resizeEvent(QResizeEvent* event) override;
    bool eventFilter(QObject* watched, QEvent* event) override;
    
private:
    void setupUI();
    void loadMessages();
    void displayMessages();
    void scrollToBottom();
    void markVisibleMessagesAsRead();
    
    // Message sending
    QString createPendingMessage(const QString& text, const QList<SelectedAttachment>& attachments);
    void sendMessageWithAttachments(const QString& text, const QList<SelectedAttachment>& attachments);
    void updatePendingMessage(const QString& localID, std::function<void(Message&)> update);
    void retryFailedMessage(const QString& localID);
    void deleteFailedMessage(const QString& localID);
    
    // Helpers
    QList<MessageAttachment> createLocalAttachments(const QList<SelectedAttachment>& attachments);
    void updateOnlineStatusAfterLoad();  ///< Обновляет статус после загрузки сообщений
    
    MessagesClient* messagesClient_ = nullptr;
    FilesClient* filesClient_ = nullptr;
    OnlinerClient* onlinerClient_ = nullptr;
    Chat chat_;
    qint64 currentUserId_ = 0;
    QString accessToken_;
    
    // Messages state
    QList<Message> messages_;
    QSet<qint64> confirmedMessageIds_;  // Для дедупликации
    bool isLoading_ = false;
    bool hasMoreMessages_ = true;
    
    // Selected attachments (before send)
    QList<SelectedAttachment> selectedAttachments_;
    
    // UI
    QWidget* headerWidget_ = nullptr;
    AvatarWidget* headerAvatarWidget_ = nullptr;
    QLabel* headerLabel_ = nullptr;
    OnlineStatusWidget* onlineStatusWidget_ = nullptr;
    QScrollArea* scrollArea_ = nullptr;
    QWidget* messagesContainer_ = nullptr;
    QVBoxLayout* messagesLayout_ = nullptr;
    ScrollToBottomButton* scrollToBottomBtn_ = nullptr;
    AttachmentPreviewStrip* attachmentStrip_ = nullptr;
    ExpandingTextEdit* inputEdit_ = nullptr;
    QPushButton* sendButton_ = nullptr;
    QPushButton* attachButton_ = nullptr;
    QPushButton* emojiButton_ = nullptr;
    EmojiPicker* emojiPicker_ = nullptr;
};

/**
 * @brief Виджет одного сообщения (пузырёк)
 * 
 * Поддерживает хвостик в стиле iMessage для последнего сообщения в группе
 * и динамические скругления углов в зависимости от позиции в группе
 */
class MessageBubble : public QWidget {
    Q_OBJECT
    
public:
    enum class Side {
        Left,   // Incoming
        Right   // Outgoing
    };
    
    explicit MessageBubble(
        const Message& message,
        Side side,
        qint64 currentUserId,
        bool showTail = true,
        QWidget* parent = nullptr
    );
    
    void updateReadStatus(const QList<qint64>& readBy);
    void setShowTail(bool show);
    
    /**
     * @brief Установить позицию в группе для динамических скруглений
     * @param isFirst Первое сообщение в группе
     * @param isLast Последнее сообщение в группе
     * @param isOnly Единственное сообщение в группе
     */
    void setGroupPosition(bool isFirst, bool isLast, bool isOnly);
    
signals:
    /// Сигнал при клике на вложение (все вложения сообщения, индекс кликнутого)
    void attachmentClicked(const QList<MessageAttachment>& attachments, int clickedIndex);
    
private slots:
    void onAttachmentClicked(const MessageAttachment& attachment);
    
protected:
    void paintEvent(QPaintEvent* event) override;
    
private:
    void setupUI();
    QString formatTime(const QDateTime& time) const;
    QPainterPath bubblePath() const;
    
    Message message_;
    Side side_;
    qint64 currentUserId_ = 0;
    bool showTail_ = true;
    BubbleCorners corners_;  ///< Настройки скругления углов
    
    QLabel* textLabel_ = nullptr;
    QLabel* timeLabel_ = nullptr;
    QLabel* statusLabel_ = nullptr;
};

/**
 * @brief Виджет ввода сообщения
 */
class MessageInputWidget : public QWidget {
    Q_OBJECT
    
public:
    explicit MessageInputWidget(QWidget* parent = nullptr);
    
    void clear();
    void setEnabled(bool enabled);
    
signals:
    void sendClicked(const QString& text);
    
private slots:
    void onSendClicked();
    
private:
    void setupUI();
    
    QTextEdit* inputEdit_ = nullptr;
    QPushButton* sendButton_ = nullptr;
    QPushButton* attachButton_ = nullptr;
};

} // namespace BarkFluff