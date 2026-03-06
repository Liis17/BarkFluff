/**
 * @file MessengerPage.cpp
 * @brief Реализация главной страницы мессенджера
 * 
 * Поддерживает optimistic UI, отправку с вложениями, группировку сообщений,
 * хвостики пузырьков в стиле iMessage
 */

#include "MessengerPage.h"
#include "Services/FileCacheService.h"
#include "Services/NotificationService.h"
#include "UI/Widgets/MessageBubbleShape.h"
#include "UI/Widgets/ExpandingTextEdit.h"
#include "Utils/ImageOptimizer.h"

#include <QHBoxLayout>
#include <QVBoxLayout>
#include <QMessageBox>
#include <QScrollBar>
#include <QApplication>
#include <QDebug>
#include <QThread>
#include <QtConcurrent>
#include <QBuffer>
#include <QImage>
#include <QFileDialog>
#include <QClipboard>
#include <QMimeData>
#include <QPixmap>
#include <QNetworkAccessManager>
#include <QNetworkReply>
#include <QNetworkRequest>
#include <QPainter>
#include <QPainterPath>
#include <QProgressBar>
#include <QScreen>
#include <QResizeEvent>
#include <QShortcut>
#include <QKeyEvent>

#include <algorithm>
#include <iostream>
#include <thread>

namespace BarkFluff {

// ============================================================================
// MessengerPage
// ============================================================================

MessengerPage::MessengerPage(
    const ServerConfiguration& config,
    const UserSession& session,
    QWidget* parent
)
    : QWidget(parent)
    , config_(config)
    , session_(session)
{
    // Debug output for session info
    qDebug() << "MessengerPage: Initializing with session:";
    qDebug() << "  - userId =" << session_.userId;
    qDebug() << "  - username =" << session_.username;
    qDebug() << "  - accessToken valid =" << session_.accessToken.isValid();
    
    // Initialize FileCacheService with files service base URL
    QString filesBaseUrl = QString("%1://%2:%3")
        .arg(config_.files.tlsEnabled ? "https" : "http")
        .arg(config_.files.endpoint.host)
        .arg(config_.files.endpoint.port);
    FileCacheService::instance()->setServerBaseUrl(filesBaseUrl);
    qDebug() << "MessengerPage: FileCacheService baseUrl =" << filesBaseUrl;
    
    // Create clients
    messagesClient_ = std::make_unique<MessagesClient>(config_.messages);
    updatesClient_ = std::make_unique<UpdatesClient>(config_.updates);
    usersClient_ = std::make_unique<UsersClient>(config_.users);
    onlinerClient_ = new OnlinerClient(config_.onliner, this);
    
    // Set ourselves online
    onlinerClient_->setOnlineStatus(session_.accessToken.value);
    
    // Setup search debounce timer
    searchDebounceTimer_ = new QTimer(this);
    searchDebounceTimer_->setSingleShot(true);
    searchDebounceTimer_->setInterval(300);
    connect(searchDebounceTimer_, &QTimer::timeout, this, &MessengerPage::performSearch);
    
    setupUI();
    loadChats();
    
    // Start updates
    updatesClient_->start(session_.accessToken.value);
    
    // Connect updates
    connect(updatesClient_.get(), &UpdatesClient::newMessage,
            this, &MessengerPage::onNewMessage);
    connect(updatesClient_.get(), &UpdatesClient::messageRead,
            this, &MessengerPage::onMessageRead);
    
    // Refresh timer (every 30 seconds)
    refreshTimer_ = new QTimer(this);
    connect(refreshTimer_, &QTimer::timeout, this, &MessengerPage::refreshChats);
    refreshTimer_->start(30000);
}

MessengerPage::~MessengerPage() {
    updatesClient_->stop();
}

void MessengerPage::setupUI() {
    auto* mainLayout = new QHBoxLayout(this);
    mainLayout->setContentsMargins(0, 0, 0, 0);
    mainLayout->setSpacing(0);
    
    // Splitter
    splitter_ = new QSplitter(Qt::Horizontal, this);
    
    // Left side - chat list
    chatList_ = new ChatListWidget(splitter_);
    chatList_->setMinimumWidth(250);
    chatList_->setMaximumWidth(400);
    
    connect(chatList_, &ChatListWidget::chatSelected,
            this, &MessengerPage::onChatSelected);
    connect(chatList_, &ChatListWidget::userSelected,
            this, &MessengerPage::onUserSelected);
    connect(chatList_, &ChatListWidget::searchRequested,
            this, &MessengerPage::onSearchRequested);
    connect(chatList_, &ChatListWidget::profileRequested,
            this, &MessengerPage::onProfileRequested);
    connect(chatList_, &ChatListWidget::settingsRequested,
            this, &MessengerPage::settingsRequested);
    
    // Right side - conversation or empty state
    rightStack_ = new QStackedWidget(splitter_);
    
    // Empty state
    emptyStateLabel_ = new QLabel(tr("Выберите чат для начала общения"));
    emptyStateLabel_->setAlignment(Qt::AlignCenter);
    emptyStateLabel_->setStyleSheet(
        "QLabel { color: #888; font-size: 16px; }"
    );
    
    // Create FilesClient for uploads
    auto* filesClient = new FilesClient(config_.files);
    
    // Conversation widget
    conversation_ = new ConversationWidget(
        messagesClient_.get(),
        filesClient,
        onlinerClient_,
        session_.userId,
        session_.accessToken.value,
        rightStack_
    );
    
    connect(conversation_, &ConversationWidget::messageSent,
            this, &MessengerPage::onMessageSent);
    connect(conversation_, &ConversationWidget::messagesLoaded,
            this, [this]() {
        // Помечаем все загруженные сообщения как прочитанные на сервере
        auto messageIds = conversation_->loadedMessageIds();
        if (!messageIds.isEmpty()) {
            QString token = session_.accessToken.value;
            auto* client = messagesClient_.get();
            std::thread([client, messageIds, token]() {
                client->markAsRead(messageIds, token);
            }).detach();
        }
    });
    connect(conversation_, &ConversationWidget::profileRequested,
            this, &MessengerPage::onUserProfileRequested);
    
    rightStack_->addWidget(emptyStateLabel_);
    rightStack_->addWidget(conversation_);
    rightStack_->setCurrentIndex(0);
    
    // Add to splitter
    splitter_->addWidget(chatList_);
    splitter_->addWidget(rightStack_);
    splitter_->setStretchFactor(0, 0);
    splitter_->setStretchFactor(1, 1);
    
    mainLayout->addWidget(splitter_);
}

void MessengerPage::loadChats() {
    if (isLoading_) return;
    isLoading_ = true;
    
    auto result = messagesClient_->listChats(0, 50, session_.accessToken.value);
    
    chats_ = result.chats;
    chatList_->setChats(chats_);
    
    isLoading_ = false;
}

void MessengerPage::refreshChats() {
    loadChats();
}

void MessengerPage::onChatSelected(const Chat& chat) {
    selectedChat_ = chat;
    
    // Уведомляем сервис уведомлений о текущем открытом чате
    NotificationService::instance().setCurrentChatId(chat.id);
    
    // Сбрасываем счётчик непрочитанных сообщений при открытии чата
    if (selectedChat_.countUnread > 0) {
        selectedChat_.countUnread = 0;
        
        // Обновляем чат в локальном списке
        for (auto& c : chats_) {
            if (c.id == selectedChat_.id) {
                c.countUnread = 0;
                break;
            }
        }
        
        // Обновляем отображение в списке чатов
        chatList_->updateChat(selectedChat_);
    }
    
    conversation_->setChat(chat);
    rightStack_->setCurrentIndex(1);
}

void MessengerPage::onNewMessage(const NewMessageEvent& event) {
    qDebug() << "MessengerPage::onNewMessage - received message id =" << event.message.id
             << "senderId =" << event.message.senderId 
             << "session_.userId =" << session_.userId
             << "chatId =" << event.chatId;
    
    // Skip our own messages - they're already added as pending/confirmed
    // Проверяем оба условия: по senderId и по наличию в confirmedMessageIds
    if (event.message.senderId == session_.userId) {
        qDebug() << "MessengerPage::onNewMessage - skipping own message (senderId match)";
        return;
    }
    
    // Дополнительная проверка: если сообщение уже подтверждено, пропускаем
    if (conversation_->isMessageConfirmed(event.message.id)) {
        qDebug() << "MessengerPage::onNewMessage - skipping confirmed message id =" << event.message.id;
        return;
    }
    
    // Find and update chat in list
    QString chatName;
    QPixmap chatAvatar;
    for (auto& chat : chats_) {
        if (chat.id == event.chatId) {
            chat.lastMessageText = event.message.content.text.left(50);
            chat.lastMessageTime = event.message.sentAt;
            chat.lastMessageSenderId = event.message.senderId;
            if (event.message.senderId != session_.userId) {
                chat.countUnread++;
            }
            chatList_->updateChat(chat);
            chatName = chat.title;
            // Load avatar for notification (will be loaded async if needed)
            break;
        }
    }
    
    // Show notification with avatar
    auto& notify = NotificationService::instance();
    if (notify.shouldShowNotification(event.chatId, event.message.senderId, session_.userId)) {
        QString senderName = event.message.senderName;
        if (senderName.isEmpty()) {
            senderName = chatName;
        }
        QString text = notify.settings().showPreview ? event.message.content.text : QString::fromUtf8("Новое сообщение");
        
        // Try to load avatar synchronously from cache
        QPixmap avatar;
        QString avatarUrl;
        for (const auto& chat : chats_) {
            if (chat.id == event.chatId) {
                avatarUrl = chat.picturePreview.isEmpty() ? chat.picture : chat.picturePreview;
                break;
            }
        }
        
        if (!avatarUrl.isEmpty()) {
            qDebug() << "MessengerPage::onNewMessage - loading avatar from URL:" << avatarUrl;
            // Try to get from cache synchronously
            QString localPath = FileCacheService::instance()->getCachedFilePath(
                avatarUrl,
                CachedFileType::Avatar,
                avatarUrl
            );
            qDebug() << "MessengerPage::onNewMessage - localPath from cache:" << localPath;
            if (!localPath.isEmpty()) {
                bool loaded = avatar.load(localPath);
                qDebug() << "MessengerPage::onNewMessage - avatar loaded:" << loaded 
                         << "size:" << avatar.size();
            }
        } else {
            qDebug() << "MessengerPage::onNewMessage - no avatar URL for chat:" << event.chatId;
        }
        
        notify.showMessageNotification(
            event.chatId,
            chatName,
            senderName,
            text,
            avatar
        );
    }
    
    // If this chat is currently open, add message
    if (selectedChat_.id == event.chatId) {
        conversation_->addMessage(event.message);
    }
}

void MessengerPage::onMessageRead(const MessageReadEvent& event) {
    updateMessageReadStatus(event.chatId, event.messageId, event.newReadBy);
}

void MessengerPage::onMessageSent(const QString& text) {
    // Оптимистичное обновление чата в списке (не ждём ответа от сервера)
    if (!selectedChat_.id.isEmpty()) {
        for (auto& chat : chats_) {
            if (chat.id == selectedChat_.id) {
                chat.lastMessageText = text.left(50);
                chat.lastMessageTime = QDateTime::currentDateTime();
                chat.lastMessageSenderId = session_.userId;
                chatList_->updateChat(chat);
                break;
            }
        }
    }
}

void MessengerPage::updateChatInList(const Chat& chat) {
    chatList_->updateChat(chat);
}

void MessengerPage::updateMessageReadStatus(
    const QString& chatId,
    qint64 messageId,
    const QList<qint64>& readBy
) {
    if (selectedChat_.id == chatId) {
        conversation_->updateMessageReadStatus(messageId, readBy);
    }
}

void MessengerPage::onSearchRequested(const QString& query) {
    currentSearchQuery_ = query;
    searchDebounceTimer_->start();  // Restart timer (debounce)
}

void MessengerPage::performSearch() {
    if (currentSearchQuery_.length() < 3) {
        chatList_->clearSearchResults();
        return;
    }
    
    qDebug() << "MessengerPage::performSearch - searching for:" << currentSearchQuery_;

    QString accessToken = session_.accessToken.value;
    QString query = currentSearchQuery_;
    UsersClient* client = usersClient_.get();
    
    std::thread([this, client, query, accessToken]() {
        try {
            auto result = client->searchUsers(query, accessToken, 0, 20);

            QMetaObject::invokeMethod(this, [this, result]() {
                chatList_->setSearchResults(result.users);
            });
        } catch (const std::exception& e) {
            qWarning() << "Search failed:" << e.what();
        }
    }).detach();
}

void MessengerPage::onUserSelected(const User& user) {
    qDebug() << "MessengerPage::onUserSelected - user:" << user.id << user.displayName();
    
    // Clear search
    chatList_->searchEdit()->clear();
    chatList_->clearSearchResults();
    
    // Check if we already have a chat with this user
    for (const auto& chat : chats_) {
        if (!chat.isGroupChat && chat.peerId == user.id) {
            // Found existing chat - open it
            onChatSelected(chat);
            return;
        }
    }
    
    // No existing chat - get or create via API
    QString token = session_.accessToken.value;
    MessagesClient* msgClient = messagesClient_.get();
    
    std::thread([this, user, msgClient, token]() {
        try {
            QString chatId = msgClient->getPersonChatId(user.id, token);
            
            QMetaObject::invokeMethod(this, [this, user, chatId]() {
                Chat chat = createChatFromUser(user, chatId);
                
                // Add to local list
                chats_.prepend(chat);
                chatList_->setChats(chats_);
                
                // Open the chat
                onChatSelected(chat);
            });
        } catch (const std::exception& e) {
            qWarning() << "Failed to get/create chat:" << e.what();
            
            // Show error to user
            QString errorMsg = QString::fromStdString(e.what());
            QMetaObject::invokeMethod(this, [errorMsg]() {
                QMessageBox::warning(nullptr, QString::fromUtf8("Ошибка"), 
                    QString::fromUtf8("Не удалось открыть чат: %1").arg(errorMsg));
            });
        }
    }).detach();
}

Chat MessengerPage::createChatFromUser(const User& user, const QString& chatId) {
    Chat chat;
    chat.id = chatId;
    chat.title = user.displayName();
    chat.isGroupChat = false;
    chat.peerId = user.id;
    chat.picture = user.profilePicture;
    chat.picturePreview = user.profilePicturePreview;
    return chat;
}

void MessengerPage::onProfileRequested() {
    // Create profile page if not exists
    if (!profilePage_) {
        profilePage_ = new ProfilePage(
            config_.users,
            session_.accessToken.value,
            session_.userId,
            rightStack_
        );
        connect(profilePage_, &ProfilePage::backRequested,
                this, &MessengerPage::onProfileBack);
        rightStack_->addWidget(profilePage_);
    }
    
    rightStack_->setCurrentWidget(profilePage_);
}

void MessengerPage::onProfileBack() {
    rightStack_->setCurrentIndex(1);  // Conversation widget
}

void MessengerPage::onUserProfileRequested() {
    // Создаем UserProfileView для текущего выбранного чата
    if (userProfileView_) {
        userProfileView_->deleteLater();
    }
    
    userProfileView_ = new UserProfileView(
        config_.users,
        config_.onliner,
        config_.messages,
        config_.files,
        session_.accessToken.value,
        session_.userId,
        selectedChat_,
        this
    );
    
    connect(userProfileView_, &UserProfileView::backRequested,
            this, &MessengerPage::onUserProfileBack);
    connect(userProfileView_, &UserProfileView::editProfileRequested,
            this, &MessengerPage::onEditProfileFromView);
    
    rightStack_->addWidget(userProfileView_);
    rightStack_->setCurrentWidget(userProfileView_);
}

void MessengerPage::onUserProfileBack() {
    rightStack_->setCurrentIndex(1);  // Conversation widget
    
    if (userProfileView_) {
        userProfileView_->deleteLater();
        userProfileView_ = nullptr;
    }
}

void MessengerPage::onEditProfileFromView() {
    // Переход к редактированию профиля
    onProfileRequested();
}

void MessengerPage::openChatById(const QString& chatId) {
    // Ищем чат в списке
    for (const auto& chat : chats_) {
        if (chat.id == chatId) {
            onChatSelected(chat);
            return;
        }
    }
    
    // Если чат не найден - обновляем список и пробуем снова
    loadChats();
    
    // Повторный поиск после загрузки
    for (const auto& chat : chats_) {
        if (chat.id == chatId) {
            onChatSelected(chat);
            return;
        }
    }
    
    qWarning() << "MessengerPage::openChatById - chat not found:" << chatId;
}

void MessengerPage::markChatAsRead(const QString& chatId) {
    // Получаем ID сообщений для mark as read
    if (selectedChat_.id == chatId && conversation_) {
        auto messageIds = conversation_->loadedMessageIds();
        if (!messageIds.isEmpty()) {
            QString token = session_.accessToken.value;
            auto* client = messagesClient_.get();
            std::thread([client, messageIds, token]() {
                client->markAsRead(messageIds, token);
            }).detach();
        }
    }
}

// ============================================================================
// UserSearchRowWidget
// ============================================================================

UserSearchRowWidget::UserSearchRowWidget(const User& user, QWidget* parent)
    : QWidget(parent)
    , user_(user)
{
    setupUI();
    loadAvatar();
}

void UserSearchRowWidget::setupUI() {
    auto* layout = new QHBoxLayout(this);
    layout->setContentsMargins(12, 8, 12, 8);
    layout->setSpacing(10);
    
    // Avatar
    avatarLabel_ = new QLabel;
    avatarLabel_->setFixedSize(40, 40);
    avatarLabel_->setAlignment(Qt::AlignCenter);
    avatarLabel_->setStyleSheet(
        "QLabel { background-color: #e0e0e0; border-radius: 20px; font-weight: bold; }"
    );
    avatarLabel_->setText(user_.avatarInitials());
    layout->addWidget(avatarLabel_);
    
    // Content
    auto* contentLayout = new QVBoxLayout;
    contentLayout->setSpacing(2);
    
    // Name
    nameLabel_ = new QLabel(user_.displayName());
    nameLabel_->setStyleSheet("font-weight: bold;");
    contentLayout->addWidget(nameLabel_);
    
    // Username
    usernameLabel_ = new QLabel("@" + user_.username);
    usernameLabel_->setStyleSheet("color: #666; font-size: 12px;");
    contentLayout->addWidget(usernameLabel_);
    
    layout->addLayout(contentLayout, 1);
}

void UserSearchRowWidget::loadAvatar() {
    if (!user_.profilePicturePreview.isEmpty()) {
        auto* cacheService = FileCacheService::instance();
        
        cacheService->getCachedFilePathAsync(
            user_.profilePicturePreview,
            CachedFileType::Avatar,
            user_.profilePicturePreview,
            [this](const QString& localPath) {
                if (localPath.isEmpty()) return;
                
                QPixmap pixmap(localPath);
                if (!pixmap.isNull()) {
                    QPixmap scaled = pixmap.scaled(
                        40, 40, 
                        Qt::KeepAspectRatioByExpanding, 
                        Qt::SmoothTransformation
                    );
                    
                    QPixmap rounded(40, 40);
                    rounded.fill(Qt::transparent);
                    
                    QPainter painter(&rounded);
                    painter.setRenderHint(QPainter::Antialiasing, true);
                    painter.setRenderHint(QPainter::SmoothPixmapTransform, true);
                    
                    QPainterPath path;
                    path.addEllipse(0, 0, 40, 40);
                    painter.setClipPath(path);
                    painter.drawPixmap(0, 0, scaled);
                    
                    QMetaObject::invokeMethod(this, [this, rounded]() {
                        avatarLabel_->setPixmap(rounded);
                        avatarLabel_->setText("");
                    });
                }
            }
        );
    }
}

// ============================================================================
// ChatListWidget
// ============================================================================

ChatListWidget::ChatListWidget(QWidget* parent)
    : QWidget(parent)
{
    auto* layout = new QVBoxLayout(this);
    layout->setContentsMargins(0, 0, 0, 0);
    layout->setSpacing(0);
    
    // Header with search and profile button
    auto* headerLayout = new QHBoxLayout;
    headerLayout->setContentsMargins(0, 0, 0, 0);
    headerLayout->setSpacing(0);
    
    // Search box
    searchEdit_ = new QLineEdit;
    searchEdit_->setPlaceholderText(tr("Поиск..."));
    searchEdit_->setStyleSheet(
        "QLineEdit { padding: 10px 12px; border: none; border-bottom: 1px solid #e0e0e0; "
        "background-color: #f5f5f5; font-size: 14px; }"
        "QLineEdit:focus { background-color: white; }"
    );
    connect(searchEdit_, &QLineEdit::textChanged,
            this, &ChatListWidget::onSearchTextChanged);
    headerLayout->addWidget(searchEdit_, 1);
    
    // Settings button
    auto* settingsBtn = new QPushButton(QString::fromUtf8("⚙"));
    settingsBtn->setFixedSize(44, 44);
    settingsBtn->setStyleSheet(
        "QPushButton { font-size: 20px; border: none; border-bottom: 1px solid #e0e0e0; "
        "background-color: #f5f5f5; }"
        "QPushButton:hover { background-color: #e0e0e0; }"
    );
    connect(settingsBtn, &QPushButton::clicked, this, &ChatListWidget::settingsRequested);
    headerLayout->addWidget(settingsBtn);
    
    // Profile button
    auto* profileBtn = new QPushButton(QString::fromUtf8("👤"));
    profileBtn->setFixedSize(44, 44);
    profileBtn->setStyleSheet(
        "QPushButton { font-size: 20px; border: none; border-bottom: 1px solid #e0e0e0; "
        "background-color: #f5f5f5; }"
        "QPushButton:hover { background-color: #e0e0e0; }"
    );
    connect(profileBtn, &QPushButton::clicked, this, &ChatListWidget::profileRequested);
    headerLayout->addWidget(profileBtn);
    
    layout->addLayout(headerLayout);
    
    // Search results section label
    searchSectionLabel_ = new QLabel(tr("Пользователи"));
    searchSectionLabel_->setStyleSheet(
        "QLabel { padding: 8px 12px; font-size: 12px; color: #666; "
        "background-color: #fafafa; font-weight: bold; }"
    );
    searchSectionLabel_->hide();
    layout->addWidget(searchSectionLabel_);
    
    // Search results list
    searchResultsWidget_ = new QListWidget;
    searchResultsWidget_->setStyleSheet(
        "QListWidget { border: none; background-color: white; }"
        "QListWidget::item { border-bottom: 1px solid #f0f0f0; }"
        "QListWidget::item:selected { background-color: #e3f2fd; }"
    );
    searchResultsWidget_->setVerticalScrollMode(QAbstractItemView::ScrollPerPixel);
    searchResultsWidget_->setHorizontalScrollBarPolicy(Qt::ScrollBarAlwaysOff);
    searchResultsWidget_->hide();
    connect(searchResultsWidget_, &QListWidget::itemClicked,
            this, &ChatListWidget::onSearchItemClicked);
    layout->addWidget(searchResultsWidget_);
    
    // Chats section label
    chatsSectionLabel_ = new QLabel(tr("Сообщения"));
    chatsSectionLabel_->setStyleSheet(
        "QLabel { padding: 8px 12px; font-size: 12px; color: #666; "
        "background-color: #fafafa; font-weight: bold; }"
    );
    layout->addWidget(chatsSectionLabel_);
    
    // Chats list
    listWidget_ = new QListWidget;
    listWidget_->setStyleSheet(
        "QListWidget { border: none; }"
        "QListWidget::item { border-bottom: 1px solid #e0e0e0; }"
        "QListWidget::item:selected { background-color: #e3f2fd; }"
    );
    listWidget_->setVerticalScrollMode(QAbstractItemView::ScrollPerPixel);
    listWidget_->setHorizontalScrollBarPolicy(Qt::ScrollBarAlwaysOff);
    
    connect(listWidget_, &QListWidget::itemClicked,
            this, &ChatListWidget::onItemClicked);
    
    layout->addWidget(listWidget_);
}

void ChatListWidget::setChats(const QList<Chat>& chats) {
    chats_ = chats;
    filteredChats_ = chats;
    applyFilter(searchEdit_->text());
}

void ChatListWidget::sortChats() {
    // Сортируем по lastMessageTime (новые сверху)
    std::sort(filteredChats_.begin(), filteredChats_.end(), [](const Chat& a, const Chat& b) {
        if (a.lastMessageTime.isValid() && b.lastMessageTime.isValid()) {
            return a.lastMessageTime > b.lastMessageTime;
        }
        if (a.lastMessageTime.isValid()) {
            return true;
        }
        if (b.lastMessageTime.isValid()) {
            return false;
        }
        return false;
    });
}

void ChatListWidget::applyFilter(const QString& filter) {
    if (filter.isEmpty()) {
        filteredChats_ = chats_;
    } else {
        filteredChats_.clear();
        for (const auto& chat : chats_) {
            if (chat.title.contains(filter, Qt::CaseInsensitive)) {
                filteredChats_.append(chat);
            }
        }
    }
    
    sortChats();
    
    // Пересоздаём список
    listWidget_->clear();
    
    for (const auto& chat : filteredChats_) {
        auto* item = new QListWidgetItem(listWidget_);
        auto* rowWidget = new ChatRowWidget(chat, listWidget_);
        item->setSizeHint(rowWidget->sizeHint());
        listWidget_->setItemWidget(item, rowWidget);
        item->setData(Qt::UserRole, chat.id);
    }
    
    // Show/hide empty state
    if (filteredChats_.isEmpty() && !filter.isEmpty()) {
        chatsSectionLabel_->setText(tr("Сообщения — не найдено"));
    } else {
        chatsSectionLabel_->setText(tr("Сообщения"));
    }
}

void ChatListWidget::setSearchResults(const QList<User>& users) {
    searchResults_ = users;
    
    searchResultsWidget_->clear();
    
    if (users.isEmpty()) {
        searchSectionLabel_->hide();
        searchResultsWidget_->hide();
        return;
    }
    
    searchSectionLabel_->show();
    searchResultsWidget_->show();
    
    for (const auto& user : users) {
        auto* item = new QListWidgetItem(searchResultsWidget_);
        auto* rowWidget = new UserSearchRowWidget(user, searchResultsWidget_);
        item->setSizeHint(rowWidget->sizeHint());
        searchResultsWidget_->setItemWidget(item, rowWidget);
        item->setData(Qt::UserRole, QVariant::fromValue(user.id));
    }
}

void ChatListWidget::clearSearchResults() {
    searchResults_.clear();
    searchResultsWidget_->clear();
    searchSectionLabel_->hide();
    searchResultsWidget_->hide();
}

void ChatListWidget::updateChat(const Chat& chat) {
    for (int i = 0; i < chats_.size(); ++i) {
        if (chats_[i].id == chat.id) {
            chats_[i] = chat;
            break;
        }
    }
    
    // Re-apply filter and sort
    applyFilter(searchEdit_->text());
}

void ChatListWidget::clearSelection() {
    listWidget_->clearSelection();
    searchResultsWidget_->clearSelection();
}

void ChatListWidget::onItemClicked(QListWidgetItem* item) {
    QString chatId = item->data(Qt::UserRole).toString();
    for (const auto& chat : chats_) {
        if (chat.id == chatId) {
            emit chatSelected(chat);
            break;
        }
    }
}

void ChatListWidget::onSearchItemClicked(QListWidgetItem* item) {
    qint64 userId = item->data(Qt::UserRole).toLongLong();
    for (const auto& user : searchResults_) {
        if (user.id == userId) {
            emit userSelected(user);
            break;
        }
    }
}

void ChatListWidget::onSearchTextChanged(const QString& text) {
    // Emit search signal for server-side search (min 3 chars)
    if (text.length() >= 3) {
        emit searchRequested(text);
    } else {
        clearSearchResults();
    }
    
    // Apply local filter to chats
    applyFilter(text);
}

// ============================================================================
// ChatRowWidget
// ============================================================================

ChatRowWidget::ChatRowWidget(const Chat& chat, QWidget* parent)
    : QWidget(parent)
    , chat_(chat)
{
    setupUI();
    loadAvatar();
}

void ChatRowWidget::setupUI() {
    auto* layout = new QHBoxLayout(this);
    layout->setContentsMargins(12, 8, 12, 8);
    layout->setSpacing(10);
    
    // Avatar
    avatarLabel_ = new QLabel;
    avatarLabel_->setFixedSize(48, 48);
    avatarLabel_->setAlignment(Qt::AlignCenter);
    avatarLabel_->setStyleSheet(
        "QLabel { background-color: #e0e0e0; border-radius: 24px; }"
    );
    avatarLabel_->setText(chat_.avatarInitials());
    layout->addWidget(avatarLabel_);
    
    // Content
    auto* contentLayout = new QVBoxLayout;
    contentLayout->setSpacing(2);
    
    // Name
    nameLabel_ = new QLabel(chat_.title);
    nameLabel_->setStyleSheet("font-weight: bold;");
    contentLayout->addWidget(nameLabel_);
    
    // Last message preview
    messageLabel_ = new QLabel;
    messageLabel_->setStyleSheet("color: #666;");
    if (!chat_.lastMessageText.isEmpty()) {
        messageLabel_->setText(chat_.lastMessageText.left(30));
    } else {
        messageLabel_->setText(tr("Нет сообщений"));
    }
    contentLayout->addWidget(messageLabel_);
    
    layout->addLayout(contentLayout, 1);
    
    // Right side (time + unread)
    auto* rightLayout = new QVBoxLayout;
    rightLayout->setSpacing(4);
    
    // Time
    timeLabel_ = new QLabel;
    timeLabel_->setStyleSheet("color: #999; font-size: 11px;");
    if (chat_.lastMessageTime.isValid()) {
        timeLabel_->setText(chat_.lastMessageTime.toString("HH:mm"));
    }
    rightLayout->addWidget(timeLabel_, 0, Qt::AlignRight);
    
    // Unread badge
    unreadLabel_ = new QLabel;
    unreadLabel_->setAlignment(Qt::AlignCenter);
    unreadLabel_->setFixedSize(20, 20);
    unreadLabel_->setStyleSheet(
        "QLabel { background-color: #2196F3; color: white; "
        "border-radius: 10px; font-size: 11px; font-weight: bold; }"
    );
    if (chat_.countUnread > 0) {
        unreadLabel_->setText(QString::number(chat_.countUnread));
        unreadLabel_->show();
    } else {
        unreadLabel_->hide();
    }
    rightLayout->addWidget(unreadLabel_, 0, Qt::AlignRight);
    
    layout->addLayout(rightLayout);
}

void ChatRowWidget::updateChat(const Chat& chat) {
    chat_ = chat;
    nameLabel_->setText(chat_.title);
    
    if (!chat_.lastMessageText.isEmpty()) {
        messageLabel_->setText(chat_.lastMessageText.left(30));
        timeLabel_->setText(chat_.lastMessageTime.toString("HH:mm"));
    }
    
    if (chat_.countUnread > 0) {
        unreadLabel_->setText(QString::number(chat_.countUnread));
        unreadLabel_->show();
    } else {
        unreadLabel_->hide();
    }
    
    loadAvatar();
}

void ChatRowWidget::loadAvatar() {
    qDebug() << "ChatRowWidget::loadAvatar - chat:" << chat_.id 
             << "title:" << chat_.title 
             << "picture:" << chat_.picture 
             << "isGroupChat:" << chat_.isGroupChat;
    
    // Если есть URL аватарки - загружаем через FileCacheService
    if (!chat_.picture.isEmpty()) {
        auto* cacheService = FileCacheService::instance();
        
        // picture может быть полным URL или просто ID файла
        QString pictureUrl = chat_.picture;
        
        qDebug() << "ChatRowWidget::loadAvatar - loading from URL:" << pictureUrl;
        
        // Сначала пробуем получить из кэша асинхронно
        cacheService->getCachedFilePathAsync(
            pictureUrl,
            CachedFileType::Avatar,
            pictureUrl,  // URL уже может быть полным
            [this](const QString& localPath) {
                qDebug() << "ChatRowWidget::loadAvatar - got localPath:" << localPath;
                
                if (localPath.isEmpty()) {
                    qDebug() << "ChatRowWidget::loadAvatar - localPath is empty";
                    return;
                }
                
                QPixmap pixmap(localPath);
                if (!pixmap.isNull()) {
                    // Масштабируем до размера аватарки с обрезкой в круг
                    QPixmap scaled = pixmap.scaled(
                        48, 48, 
                        Qt::KeepAspectRatioByExpanding, 
                        Qt::SmoothTransformation
                    );
                    
                    // Создаём круглую маску
                    QPixmap rounded(48, 48);
                    rounded.fill(Qt::transparent);
                    
                    QPainter painter(&rounded);
                    painter.setRenderHint(QPainter::Antialiasing, true);
                    painter.setRenderHint(QPainter::SmoothPixmapTransform, true);
                    
                    QPainterPath path;
                    path.addEllipse(0, 0, 48, 48);
                    painter.setClipPath(path);
                    painter.drawPixmap(0, 0, scaled);
                    
                    // Обновляем аватарку в UI потоке
                    QMetaObject::invokeMethod(this, [this, rounded]() {
                        avatarLabel_->setPixmap(rounded);
                        avatarLabel_->setText("");  // Убираем текст с инициалами
                    });
                    
                    qDebug() << "ChatRowWidget::loadAvatar - avatar loaded successfully";
                } else {
                    qDebug() << "ChatRowWidget::loadAvatar - failed to load pixmap from:" << localPath;
                }
            }
        );
    } else {
        qDebug() << "ChatRowWidget::loadAvatar - no picture URL for chat:" << chat_.id;
    }
}

// ============================================================================
// ConversationWidget
// ============================================================================

ConversationWidget::ConversationWidget(
    MessagesClient* messagesClient,
    FilesClient* filesClient,
    OnlinerClient* onlinerClient,
    qint64 currentUserId,
    const QString& accessToken,
    QWidget* parent
)
    : QWidget(parent)
    , messagesClient_(messagesClient)
    , filesClient_(filesClient)
    , onlinerClient_(onlinerClient)
    , currentUserId_(currentUserId)
    , accessToken_(accessToken)
{
    setupUI();
}

void ConversationWidget::setupUI() {
    auto* mainLayout = new QVBoxLayout(this);
    mainLayout->setContentsMargins(0, 0, 0, 0);
    mainLayout->setSpacing(0);
    
    // Header widget with status
    headerWidget_ = new QWidget;
    headerWidget_->setCursor(Qt::PointingHandCursor);  // Cursor hints clickable
    headerWidget_->setMinimumHeight(56);  // Увеличиваем высоту для имени и статуса
    auto* headerLayout = new QHBoxLayout(headerWidget_);
    headerLayout->setContentsMargins(12, 10, 12, 10);
    headerLayout->setSpacing(10);
    
    // Avatar in header (clickable for profile)
    headerAvatarWidget_ = new AvatarWidget(QString(), QString(), 36, headerWidget_);
    headerAvatarWidget_->setUseCacheService(true);  // Используем FileCacheService для загрузки
    headerLayout->addWidget(headerAvatarWidget_);
    
    // Header text and status container
    auto* headerTextLayout = new QVBoxLayout;
    headerTextLayout->setSpacing(2);
    
    headerLabel_ = new QLabel;
    headerLabel_->setStyleSheet("font-size: 16px; font-weight: bold;");
    headerTextLayout->addWidget(headerLabel_);
    
    // Online status widget
    onlineStatusWidget_ = new OnlineStatusWidget(OnlineStatusWidget::Size::Small);
    // setShowText вызывается в setChat() после того как виджет показан
    headerTextLayout->addWidget(onlineStatusWidget_, 0, Qt::AlignLeft);
    
    headerLayout->addLayout(headerTextLayout, 1);
    
    headerWidget_->setStyleSheet(
        "#headerWidget { background-color: #f5f5f5; border-bottom: 1px solid #e0e0e0; }"
    );
    headerWidget_->setObjectName("headerWidget");
    mainLayout->addWidget(headerWidget_);
    
    // Connect avatar click to profile request
    connect(headerAvatarWidget_, &AvatarWidget::clicked, this, &ConversationWidget::profileRequested);
    
    // Messages area with scroll-to-bottom button
    auto* messagesAreaContainer = new QWidget;
    auto* messagesAreaLayout = new QVBoxLayout(messagesAreaContainer);
    messagesAreaLayout->setContentsMargins(0, 0, 0, 0);
    messagesAreaLayout->setSpacing(0);
    
    scrollArea_ = new QScrollArea;
    scrollArea_->setWidgetResizable(true);
    scrollArea_->setHorizontalScrollBarPolicy(Qt::ScrollBarAlwaysOff);
    scrollArea_->setStyleSheet("QScrollArea { border: none; background: white; }");
    
    messagesContainer_ = new QWidget;
    messagesLayout_ = new QVBoxLayout(messagesContainer_);
    messagesLayout_->setContentsMargins(12, 12, 12, 12);
    messagesLayout_->setSpacing(0);  // Управляется margins отдельных строк
    messagesLayout_->addStretch();
    
    scrollArea_->setWidget(messagesContainer_);
    
    connect(scrollArea_->verticalScrollBar(), &QScrollBar::valueChanged,
            this, &ConversationWidget::onScrollValueChanged);
    
    messagesAreaLayout->addWidget(scrollArea_, 1);
    
    mainLayout->addWidget(messagesAreaContainer, 1);
    
    // Scroll to bottom button (positioned in bottom-right of scroll area viewport)
    scrollToBottomBtn_ = new ScrollToBottomButton(scrollArea_->viewport());
    scrollToBottomBtn_->hide();  // Initially hidden
    connect(scrollToBottomBtn_, &QPushButton::clicked, this, [this]() {
        scrollToBottom();
        scrollToBottomBtn_->hideAnimated();
    });
    
    // Install event filter on viewport to handle resize
    scrollArea_->viewport()->installEventFilter(this);
    
    // Attachment preview strip
    attachmentStrip_ = new AttachmentPreviewStrip(this);
    mainLayout->addWidget(attachmentStrip_);
    connect(attachmentStrip_, &AttachmentPreviewStrip::attachmentsChanged,
            this, &ConversationWidget::onAttachmentsChanged);
    
    // Input area
    auto* inputLayout = new QHBoxLayout;
    inputLayout->setContentsMargins(12, 8, 12, 8);
    inputLayout->setSpacing(8);
    
    attachButton_ = new QPushButton("+");
    attachButton_->setFixedSize(36, 36);
    attachButton_->setStyleSheet(
        "QPushButton { font-size: 20px; border: none; background: #f0f0f0; "
        "border-radius: 18px; }"
        "QPushButton:hover { background: #e0e0e0; }"
    );
    connect(attachButton_, &QPushButton::clicked, this, &ConversationWidget::onAttachClicked);
    inputLayout->addWidget(attachButton_);
    
    // Emoji button
    emojiButton_ = new QPushButton(QString::fromUtf8("😀"));
    emojiButton_->setFixedSize(36, 36);
    emojiButton_->setStyleSheet(
        "QPushButton { font-size: 18px; border: none; background: #f0f0f0; "
        "border-radius: 18px; }"
        "QPushButton:hover { background: #e0e0e0; }"
    );
    connect(emojiButton_, &QPushButton::clicked, this, &ConversationWidget::onEmojiClicked);
    inputLayout->addWidget(emojiButton_);
    
    inputEdit_ = new ExpandingTextEdit;
    inputEdit_->setPlaceholderText(tr("Введите сообщение..."));
    inputEdit_->setStyleSheet(
        "QTextEdit { background-color: white; border: 1px solid #e0e0e0; border-radius: 18px; "
        "padding: 8px 12px; }"
    );
    inputLayout->addWidget(inputEdit_, 1);
    
    // Подключаем отправку по Enter
    connect(inputEdit_, &ExpandingTextEdit::sendRequested, this, &ConversationWidget::onSendClicked);
    
    sendButton_ = new QPushButton("➤");
    sendButton_->setFixedSize(36, 36);
    sendButton_->setStyleSheet(
        "QPushButton { font-size: 18px; border: none; background: #2196F3; "
        "color: white; border-radius: 18px; }"
        "QPushButton:hover { background: #1976D2; }"
        "QPushButton:disabled { background: #bdbdbd; }"
    );
    connect(sendButton_, &QPushButton::clicked, this, &ConversationWidget::onSendClicked);
    inputLayout->addWidget(sendButton_);
    
    mainLayout->addLayout(inputLayout);
    
    // Install event filter on inputEdit to intercept Ctrl+V
    inputEdit_->installEventFilter(this);
}

void ConversationWidget::setChat(const Chat& chat) {
    qDebug() << "=== ConversationWidget::setChat START ===";
    qDebug() << "  chatId:" << chat.id << "title:" << chat.title
             << "picturePreview:" << chat.picturePreview
             << "isGroupChat:" << chat.isGroupChat
             << "peerId:" << chat.peerId;
    
    chat_ = chat;
    headerLabel_->setText(chat_.title);
    
    // Update header avatar
    qDebug() << "  headerAvatarWidget_ ptr:" << (void*)headerAvatarWidget_;
    if (headerAvatarWidget_) {
        qDebug() << "  Before setImageUrl - imageUrl:" << headerAvatarWidget_->imageUrl()
                 << "initials:" << headerAvatarWidget_->initials()
                 << "useCache:" << headerAvatarWidget_->useCacheService();
        
        // Сначала установим useCache, потом URL
        headerAvatarWidget_->setUseCacheService(true);
        headerAvatarWidget_->setImageUrl(chat_.picturePreview);
        headerAvatarWidget_->setInitials(chat_.avatarInitials());
        
        qDebug() << "  After setImageUrl - imageUrl:" << headerAvatarWidget_->imageUrl()
                 << "useCache:" << headerAvatarWidget_->useCacheService();
    } else {
        qWarning() << "  headerAvatarWidget_ is NULL!";
    }
    
    // Load online status for peer (private chat)
    qDebug() << "ConversationWidget::setChat - checking online status conditions:"
             << "isGroupChat:" << chat_.isGroupChat
             << "peerId:" << chat_.peerId
             << "onlinerClient_:" << (onlinerClient_ ? "exists" : "null")
             << "onlineStatusWidget_:" << (onlineStatusWidget_ ? "exists" : "null");
    
    // Показываем статус для приватных чатов (peerId может быть 0 если сервер не вернул)
    if (!chat_.isGroupChat && onlinerClient_ && onlineStatusWidget_) {
        qDebug() << "ConversationWidget::setChat - conditions met, showing status widget";
        
        // Показываем виджет статуса
        onlineStatusWidget_->show();
        onlineStatusWidget_->setShowText(true);
        
        // Определяем peerId: либо из чата, либо из первого сообщения не от нас
        qint64 effectivePeerId = chat_.peerId;
        if (effectivePeerId <= 0) {
            // Ищем peerId в загруженных сообщениях
            for (const auto& msg : messages_) {
                if (msg.senderId != currentUserId_) {
                    effectivePeerId = msg.senderId;
                    qDebug() << "ConversationWidget::setChat - derived peerId from messages:" << effectivePeerId;
                    break;
                }
            }
        }
        
        // Если есть peerId, загружаем статус
        if (effectivePeerId > 0) {
            // Get initial status
            OnlinerClient* onliner = onlinerClient_;
            QString token = accessToken_;
            
            std::thread([this, onliner, effectivePeerId, token]() {
                try {
                    auto statuses = onliner->getOnlineStatus({effectivePeerId}, token);
                    if (!statuses.isEmpty()) {
                        QMetaObject::invokeMethod(this, [this, status = statuses.first()]() {
                            if (onlineStatusWidget_) {
                                onlineStatusWidget_->setStatus(status);
                            }
                        });
                    }
                } catch (const std::exception& e) {
                    qWarning() << "Failed to get online status:" << e.what();
                }
            }).detach();
            
            // Subscribe to updates
            onlinerClient_->startSubscription({effectivePeerId}, accessToken_);
            connect(onlinerClient_, &OnlinerClient::statusChanged, this, [this, effectivePeerId](const UserOnlineStatusInfo& status) {
                if (status.userId == effectivePeerId && onlineStatusWidget_) {
                    onlineStatusWidget_->setStatus(status);
                }
            });
        }
    } else if (onlineStatusWidget_) {
        // Group chat - hide status for now
        onlineStatusWidget_->hide();
    }
    
    // ПОЛНОСТЬЮ очищаем все сообщения и состояние
    messages_.clear();
    confirmedMessageIds_.clear();
    hasMoreMessages_ = true;
    isLoading_ = false;  // Сбрасываем флаг загрузки
    
    // ПОЛНОСТЬЮ удаляем все виджеты из layout
    QLayoutItem* item;
    while ((item = messagesLayout_->takeAt(0)) != nullptr) {
        // Удаляем вложенные layout'ы
        if (item->layout()) {
            QLayoutItem* subItem;
            while ((subItem = item->layout()->takeAt(0)) != nullptr) {
                if (subItem->widget()) {
                    subItem->widget()->deleteLater();
                }
                delete subItem;
            }
        }
        if (item->widget()) {
            item->widget()->deleteLater();
        }
        delete item;
    }
    
    // Добавляем stretch заново
    messagesLayout_->addStretch();
    
    // Принудительно обновляем UI перед загрузкой новых сообщений
    QApplication::processEvents();
    
    loadMessages();
}

void ConversationWidget::loadMessages() {
    if (isLoading_ || !hasMoreMessages_) return;
    isLoading_ = true;
    
    // Сохраняем текущий chatId для проверки
    QString loadingChatId = chat_.id;
    qint64 fromId = 0;
    if (!messages_.isEmpty()) {
        fromId = messages_.first().id;
    }
    
    auto result = messagesClient_->listMessages(loadingChatId, fromId, 50, 0, accessToken_);
    
    // Проверяем, что чат не сменился во время загрузки
    if (chat_.id != loadingChatId) {
        qDebug() << "ConversationWidget::loadMessages - chat changed during load, ignoring results";
        isLoading_ = false;
        return;
    }
    
    if (result.messages.size() < 50) {
        hasMoreMessages_ = false;
    }
    
    // Prepend messages (только для текущего чата)
    for (int i = result.messages.size() - 1; i >= 0; --i) {
        const auto& msg = result.messages[i];
        // Дополнительная проверка chatId каждого сообщения
        if (msg.chatId.isEmpty() || msg.chatId == loadingChatId) {
            messages_.prepend(msg);
        }
    }
    
    displayMessages();
    scrollToBottom();
    
    isLoading_ = false;
    
    // Уведомляем что сообщения загружены (для markAsRead)
    emit messagesLoaded();
    
    // Обновляем статус онлайн после загрузки сообщений (peerId может быть неизвестен до этого)
    updateOnlineStatusAfterLoad();
}

void ConversationWidget::updateOnlineStatusAfterLoad() {
    if (chat_.isGroupChat || !onlinerClient_ || !onlineStatusWidget_) {
        return;
    }
    
    // Определяем peerId: либо из чата, либо из первого сообщения не от нас
    qint64 effectivePeerId = chat_.peerId;
    if (effectivePeerId <= 0) {
        for (const auto& msg : messages_) {
            if (msg.senderId != currentUserId_) {
                effectivePeerId = msg.senderId;
                qDebug() << "ConversationWidget::updateOnlineStatusAfterLoad - derived peerId:" << effectivePeerId;
                break;
            }
        }
    }
    
    if (effectivePeerId > 0) {
        // Get status
        OnlinerClient* onliner = onlinerClient_;
        QString token = accessToken_;
        
        std::thread([this, onliner, effectivePeerId, token]() {
            try {
                auto statuses = onliner->getOnlineStatus({effectivePeerId}, token);
                if (!statuses.isEmpty()) {
                    QMetaObject::invokeMethod(this, [this, status = statuses.first()]() {
                        if (onlineStatusWidget_) {
                            onlineStatusWidget_->setStatus(status);
                        }
                    });
                }
            } catch (const std::exception& e) {
                qWarning() << "Failed to get online status after load:" << e.what();
            }
        }).detach();
        
        // Subscribe to updates
        onlinerClient_->startSubscription({effectivePeerId}, accessToken_);
    }
}

void ConversationWidget::displayMessages() {
    // Полностью очищаем layout с немедленным удалением виджетов
    QLayoutItem* item;
    while ((item = messagesLayout_->takeAt(0)) != nullptr) {
        // Удаляем вложенные layout'ы
        if (item->layout()) {
            QLayoutItem* subItem;
            while ((subItem = item->layout()->takeAt(0)) != nullptr) {
                if (subItem->widget()) {
                    subItem->widget()->deleteLater();
                }
                delete subItem;
            }
        }
        if (item->widget()) {
            item->widget()->deleteLater();
        }
        delete item;
    }
    
    QString currentChatId = chat_.id;  // Запоминаем текущий чат
    
    // Группируем сообщения для определения позиции в группе
    auto groupedItems = MessageGrouper::groupMessages(messages_, currentUserId_);
    
    // Add message bubbles с учётом группировки
    for (const auto& listItem : groupedItems) {
        // Пропускаем разделители дат (пока не реализованы визуально)
        if (listItem.type == MessageListItem::Type::DateSeparator) {
            continue;
        }
        
        const auto& msg = listItem.message;
        const auto& groupInfo = listItem.groupInfo;
        
        // Фильтруем сообщения по chatId
        if (!msg.chatId.isEmpty() && msg.chatId != currentChatId) {
            continue;
        }
        
        auto side = (msg.senderId == currentUserId_) 
            ? MessageBubble::Side::Right 
            : MessageBubble::Side::Left;
        
        auto* bubble = new MessageBubble(msg, side, currentUserId_, this);
        
        // Устанавливаем позицию в группе для динамических скруглений
        bubble->setGroupPosition(
            groupInfo.isFirstInGroup,
            groupInfo.isLastInGroup,
            groupInfo.isOnlyInGroup
        );
        
        // Подключаем сигнал клика на вложение
        connect(bubble, &MessageBubble::attachmentClicked,
                this, &ConversationWidget::onAttachmentClicked);
        
        auto* rowLayout = new QHBoxLayout;
        
        // Динамические отступы: маленький внутри группы, большой между группами
        // top margin: 2px внутри группы, 10px если это первое сообщение в группе
        int topMargin = groupInfo.isFirstInGroup ? 10 : 2;
        rowLayout->setContentsMargins(0, topMargin, 0, 0);
        
        if (side == MessageBubble::Side::Right) {
            rowLayout->addStretch();
            rowLayout->addWidget(bubble, 0, Qt::AlignRight);
        } else {
            rowLayout->addWidget(bubble, 0, Qt::AlignLeft);
            rowLayout->addStretch();
        }
        
        messagesLayout_->addLayout(rowLayout);
    }
    
    messagesLayout_->addStretch();
}

void ConversationWidget::addMessage(const Message& message) {
    // Дедупликация: проверяем, есть ли уже это сообщение
    if (message.id > 0 && confirmedMessageIds_.contains(message.id)) {
        qDebug() << "ConversationWidget::addMessage - duplicate message ignored, id =" << message.id;
        return;
    }
    
    // Также проверяем по localID для pending сообщений
    if (!message.localID.isEmpty()) {
        for (const auto& existingMsg : messages_) {
            if (existingMsg.localID == message.localID) {
                qDebug() << "ConversationWidget::addMessage - duplicate localID ignored:" << message.localID;
                return;
            }
        }
    }
    
    qDebug() << "ConversationWidget::addMessage - adding message id =" << message.id 
             << "senderId =" << message.senderId << "currentUserId_ =" << currentUserId_;
    
    messages_.append(message);
    
    // Перерисовываем все сообщения для правильной группировки
    // Это обеспечит корректные скругления углов при добавлении новых сообщений
    displayMessages();
    
    scrollToBottom();
}

void ConversationWidget::updateMessageReadStatus(qint64 messageId, const QList<qint64>& readBy) {
    // Find bubble and update
    for (int i = 0; i < messagesLayout_->count(); ++i) {
        auto* layoutItem = messagesLayout_->itemAt(i);
        if (layoutItem->layout()) {
            auto* rowLayout = layoutItem->layout();
            for (int j = 0; j < rowLayout->count(); ++j) {
                auto* item = rowLayout->itemAt(j);
                if (auto* bubble = qobject_cast<MessageBubble*>(item->widget())) {
                    // TODO: Update bubble read status
                }
            }
        }
    }
}

void ConversationWidget::clear() {
    messages_.clear();
    hasMoreMessages_ = true;
    
    QLayoutItem* item;
    while ((item = messagesLayout_->takeAt(0)) != nullptr) {
        if (item->widget()) {
            item->widget()->deleteLater();
        }
        delete item;
    }
    messagesLayout_->addStretch();
}

void ConversationWidget::onSendClicked() {
    QString text = inputEdit_->toPlainText().trimmed();
    
    // Get attachments directly from strip (synchronized)
    auto attachments = attachmentStrip_->attachments();
    
    // Check if there's anything to send
    if (text.isEmpty() && attachments.isEmpty()) {
        return;
    }
    
    if (!attachments.isEmpty()) {
        // Send with attachments (optimistic UI + background upload)
        sendMessageWithAttachments(text, attachments);
        selectedAttachments_.clear();  // Clear local cache
    } else {
        // Simple text message (optimistic UI)
        QString localID = createPendingMessage(text, {});
        
        // Capture what we need, not 'this'
        QString chatId = chat_.id;
        QString token = accessToken_;
        MessagesClient* msgClient = messagesClient_;
        
        // Send in background
        std::thread([localID, text, chatId, token, msgClient]() {
            try {
                auto confirmedMsg = msgClient->sendMessage(chatId, text, {}, token);
                // Note: Cannot update UI from background thread
                qDebug() << "Message sent successfully, id:" << confirmedMsg.id;
            } catch (const std::exception& e) {
                qWarning() << "Failed to send message:" << e.what();
            }
        }).detach();
    }
    
    inputEdit_->clear();
}

void ConversationWidget::onScrollValueChanged(int value) {
    // Load more when scrolled to top
    if (value <= 10 && !isLoading_ && hasMoreMessages_) {
        loadMessages();
    }
    
    // Show/hide scroll to bottom button
    auto* scrollbar = scrollArea_->verticalScrollBar();
    bool isAtBottom = scrollbar->value() >= scrollbar->maximum() - 50;
    
    if (isAtBottom) {
        scrollToBottomBtn_->hideAnimated();
        scrollToBottomBtn_->setUnreadCount(0);
    } else {
        scrollToBottomBtn_->showAnimated();
    }
    
    // Mark visible as read
    markVisibleMessagesAsRead();
}

void ConversationWidget::scrollToBottom() {
    QTimer::singleShot(100, [this]() {
        scrollArea_->verticalScrollBar()->setValue(
            scrollArea_->verticalScrollBar()->maximum()
        );
    });
}

void ConversationWidget::markVisibleMessagesAsRead() {
    // TODO: Implement visible message detection and mark as read
}

void ConversationWidget::onAttachClicked() {
    std::cerr << "\n\n\n!!!!!!!!! ========== ATTACH CLICKED ========== !!!!!!!!!\n\n\n" << std::flush;
    qDebug() << "=== ConversationWidget::onAttachClicked ===";
    
    auto files = QFileDialog::getOpenFileUrls(
        this,
        tr("Выберите файлы"),
        QUrl(),
        tr("Все файлы (*);;Изображения (*.png *.jpg *.jpeg *.gif *.webp);;Видео (*.mp4 *.mov *.webm)")
    );
    
    qDebug() << "  Selected files count:" << files.size();
    
    for (const auto& url : files) {
        qDebug() << "  Processing URL:" << url.toString();
        auto attachment = SelectedAttachment::fromUrl(url);
        qDebug() << "  Created attachment, id:" << attachment.id << "displayName:" << attachment.displayName();
        attachmentStrip_->addAttachment(attachment);
    }
    
    qDebug() << "=== onAttachClicked DONE ===";
}

void ConversationWidget::onEmojiClicked() {
    if (!emojiPicker_) {
        emojiPicker_ = new EmojiPicker(this);
        connect(emojiPicker_, &EmojiPicker::emojiSelected,
                this, &ConversationWidget::onEmojiSelected);
    }
    
    // Position the picker above the emoji button
    QPoint buttonPos = emojiButton_->mapToGlobal(QPoint(0, 0));
    
    // Align right edge of picker with right edge of button
    int x = buttonPos.x() + emojiButton_->width() - emojiPicker_->width();
    // Position above button with small gap
    int y = buttonPos.y() - emojiPicker_->height() - 5;
    
    // Ensure picker stays within screen bounds
    QScreen* screen = QApplication::screenAt(buttonPos);
    if (screen) {
        QRect screenRect = screen->availableGeometry();
        
        // Keep within screen horizontally
        if (x < screenRect.left()) {
            x = screenRect.left() + 5;
        }
        
        // If no space above, show below the button
        if (y < screenRect.top()) {
            y = buttonPos.y() + emojiButton_->height() + 5;
        }
    }
    
    emojiPicker_->move(x, y);
    emojiPicker_->show();
}

void ConversationWidget::onEmojiSelected(const QString& emoji) {
    // Insert emoji at current cursor position
    QTextCursor cursor = inputEdit_->textCursor();
    cursor.insertText(emoji);
    inputEdit_->setFocus();
}

void ConversationWidget::onPasteFromClipboard() {
    auto* clipboard = QApplication::clipboard();
    const QMimeData* mimeData = clipboard->mimeData();
    
    if (mimeData->hasImage()) {
        QImage image = qvariant_cast<QImage>(mimeData->imageData());
        if (!image.isNull()) {
            QByteArray imageData;
            QBuffer buffer(&imageData);
            buffer.open(QIODevice::WriteOnly);
            image.save(&buffer, "PNG");
            buffer.close();
            
            auto attachment = SelectedAttachment::fromImageData(imageData, "pasted_image.png");
            attachmentStrip_->addAttachment(attachment);
        }
    }
}

void ConversationWidget::onAttachmentsChanged(const QList<SelectedAttachment>& attachments) {
    selectedAttachments_ = attachments;
    sendButton_->setEnabled(true);  // Enable send even with only attachments
}

QString ConversationWidget::createPendingMessage(
    const QString& text, 
    const QList<SelectedAttachment>& attachments
) {
    auto localAttachments = createLocalAttachments(attachments);
    
    Message pendingMsg = Message::createPendingWithAttachments(
        chat_.id,
        currentUserId_,
        text,
        localAttachments
    );
    
    messages_.append(pendingMsg);
    
    // Инкрементально добавляем сообщение вместо полного перерисовывания
    auto side = (pendingMsg.senderId == currentUserId_) 
        ? MessageBubble::Side::Right 
        : MessageBubble::Side::Left;
    
    auto* bubble = new MessageBubble(pendingMsg, side, currentUserId_, this);
    
    // Insert before stretch
    int stretchIndex = messagesLayout_->count() - 1;
    if (stretchIndex < 0) stretchIndex = 0;
    
    auto* rowLayout = new QHBoxLayout;
    
    // Pending сообщение - считаем как последнее в группе (маленький отступ сверху)
    rowLayout->setContentsMargins(0, 2, 0, 0);
    
    if (side == MessageBubble::Side::Right) {
        rowLayout->addStretch();
        rowLayout->addWidget(bubble, 0, Qt::AlignRight);
    } else {
        rowLayout->addWidget(bubble, 0, Qt::AlignLeft);
        rowLayout->addStretch();
    }
    
    messagesLayout_->insertLayout(stretchIndex, rowLayout);
    scrollToBottom();
    
    return pendingMsg.localID;
}

void ConversationWidget::sendMessageWithAttachments(
    const QString& text,
    const QList<SelectedAttachment>& attachments
) {
    qDebug() << "ConversationWidget::sendMessageWithAttachments - text:" << text.left(30)
             << "attachments:" << attachments.size();
    
    // Create pending message first (optimistic UI)
    QString localID = createPendingMessage(text, attachments);
    qDebug() << "  created pending message with localID:" << localID;
    
    // Capture only what we need, not 'this'
    QString chatId = chat_.id;
    QString token = accessToken_;
    MessagesClient* msgClient = messagesClient_;
    FilesClient* filesClient = filesClient_;
    
    // Run upload and send in background
    std::thread([localID, text, attachments, chatId, token, msgClient, filesClient]() {
        try {
            // Upload all attachments
            QList<QString> fileIds;
            for (const auto& attachment : attachments) {
                qDebug() << "  uploading attachment:" << attachment.displayName()
                         << "type:" << static_cast<int>(attachment.type)
                         << "url:" << attachment.url.toString();

                QByteArray data = attachment.loadData();
                qDebug() << "    loadData returned:" << data.size() << "bytes";

                // Optimize images before upload
                if (attachment.isImage() && !attachment.isGif()) {
                    qDebug() << "    optimizing image...";
                    auto optimized = ImageOptimizer::optimizeFromData(data);
                    if (!optimized.data.isEmpty()) {
                        data = optimized.data;
                        qDebug() << "    after optimization:" << data.size() << "bytes"
                                 << "compression ratio:" << optimized.compressionRatio;
                    }
                }

                if (data.isEmpty()) {
                    qWarning() << "    FAILED to load data from attachment!";
                    // Note: Cannot update UI from background thread without captured this
                    return;
                }

                // Upload (without progress callback since we don't have UI access)
                QString fileId = filesClient->uploadFileWithDedup(
                    attachment.uploadFileType(),
                    data,
                    attachment.displayName(),
                    attachment.contentType(),
                    token,
                    nullptr  // No progress callback from background thread
                );

                fileIds.append(fileId);
            }

            // Send message via gRPC
            qDebug() << "  all files uploaded, fileIds:" << fileIds;

            try {
                qDebug() << "  sending message via gRPC, chatId:" << chatId << "text:" << text.left(30) << "fileIds:" << fileIds.size();

                auto confirmedMsg = msgClient->sendMessage(
                    chatId,
                    text,
                    fileIds,
                    token
                );

                qDebug() << "  message sent successfully, id:" << confirmedMsg.id;
                
                // Success - we can't update UI from here, but the message was sent
            } catch (const std::exception& e) {
                qWarning() << "  failed to send message:" << e.what();
            }

        } catch (const std::exception& e) {
            qWarning() << "  failed to upload attachments:" << e.what();
        }
    }).detach();
}

void ConversationWidget::updatePendingMessage(
    const QString& localID,
    std::function<void(Message&)> update
) {
    for (auto& msg : messages_) {
        if (msg.localID == localID) {
            update(msg);
            displayMessages();
            break;
        }
    }
}

void ConversationWidget::replacePendingMessage(const QString& localID, const Message& confirmed) {
    for (int i = 0; i < messages_.size(); ++i) {
        if (messages_[i].localID == localID) {
            messages_[i] = confirmed;
            confirmedMessageIds_.insert(confirmed.id);
            displayMessages();
            break;
        }
    }
}

void ConversationWidget::retryFailedMessage(const QString& localID) {
    // Find the failed message and retry
    for (const auto& msg : messages_) {
        if (msg.localID == localID && msg.hasSendError()) {
            // Re-send
            // TODO: Store original attachments to retry
            break;
        }
    }
}

void ConversationWidget::deleteFailedMessage(const QString& localID) {
    for (int i = 0; i < messages_.size(); ++i) {
        if (messages_[i].localID == localID && messages_[i].hasSendError()) {
            messages_.removeAt(i);
            displayMessages();
            break;
        }
    }
}

QList<MessageAttachment> ConversationWidget::createLocalAttachments(
    const QList<SelectedAttachment>& attachments
) {
    QList<MessageAttachment> result;
    
    for (const auto& selected : attachments) {
        MessageAttachment att;
        att.fileName = selected.displayName();
        att.attachmentSize = selected.fileSize();
        att.fileId = "local://" + selected.id.toString();  // Temporary ID
        
        // Set type based on selected attachment
        if (selected.isImage()) {
            att.type = MessageAttachmentType::Image;
        } else if (selected.isGif()) {
            att.type = MessageAttachmentType::Gif;
        } else if (selected.isVideo()) {
            att.type = MessageAttachmentType::Video;
        } else {
            att.type = MessageAttachmentType::Document;
        }
        
        result.append(att);
    }
    
    return result;
}

bool ConversationWidget::isMessageConfirmed(qint64 messageId) const {
    return confirmedMessageIds_.contains(messageId);
}

QList<qint64> ConversationWidget::loadedMessageIds() const {
    QList<qint64> ids;
    for (const auto& msg : messages_) {
        if (msg.id > 0) {  // Только подтверждённые сообщения
            ids.append(msg.id);
        }
    }
    return ids;
}

bool ConversationWidget::eventFilter(QObject* watched, QEvent* event) {
    // Intercept Ctrl+V on inputEdit to handle image paste
    if (watched == inputEdit_ && event->type() == QEvent::ShortcutOverride) {
        auto* keyEvent = static_cast<QKeyEvent*>(event);
        if (keyEvent->matches(QKeySequence::Paste)) {
            // Check if clipboard has an image
            auto* clipboard = QApplication::clipboard();
            const QMimeData* mimeData = clipboard->mimeData();
            
            if (mimeData->hasImage()) {
                // Handle image paste ourselves
                onPasteFromClipboard();
                event->accept();
                return true;
            }
            // If no image, let the default text paste proceed
        }
    }
    
    if (watched == scrollArea_->viewport() && event->type() == QEvent::Resize) {
        // Position scroll-to-bottom button in bottom-right corner of viewport
        if (scrollToBottomBtn_) {
            int margin = 16;
            int x = scrollArea_->viewport()->width() - scrollToBottomBtn_->width() - margin;
            int y = scrollArea_->viewport()->height() - scrollToBottomBtn_->height() - margin;
            scrollToBottomBtn_->move(x, y);
            scrollToBottomBtn_->raise();  // Ensure button is on top
        }
    }
    return QWidget::eventFilter(watched, event);
}

void ConversationWidget::resizeEvent(QResizeEvent* event) {
    QWidget::resizeEvent(event);
}

void ConversationWidget::onAttachmentClicked(const QList<MessageAttachment>& attachments, int clickedIndex) {
    qDebug() << "ConversationWidget::onAttachmentClicked - attachments:" << attachments.size() 
             << "clickedIndex:" << clickedIndex;
    
    if (attachments.isEmpty()) return;
    
    // Открываем MediaViewerDialog
    auto* viewer = new MediaViewerDialog(attachments, clickedIndex, this);
    viewer->setAttribute(Qt::WA_DeleteOnClose);
    viewer->show();
}

// ============================================================================
// MessageBubble
// ============================================================================

MessageBubble::MessageBubble(
    const Message& message,
    Side side,
    qint64 currentUserId,
    bool showTail,
    QWidget* parent
)
    : QWidget(parent)
    , message_(message)
    , side_(side)
    , currentUserId_(currentUserId)
    , showTail_(showTail)
{
    setupUI();
}

void MessageBubble::setShowTail(bool show) {
    showTail_ = show;
    update();
}

void MessageBubble::setupUI() {
    // Make widget transparent for custom shape drawing
    setAttribute(Qt::WA_TranslucentBackground, true);
    
    auto* layout = new QVBoxLayout(this);
    // Add margins for tail (6px on the side with tail)
    if (side_ == Side::Right) {
        layout->setContentsMargins(0, 0, 8, 0);  // Space for right tail
    } else {
        layout->setContentsMargins(8, 0, 0, 0);  // Space for left tail
    }
    layout->setSpacing(0);
    
    // Container with extra space for tail
    auto* container = new QWidget;
    auto* containerLayout = new QVBoxLayout(container);
    containerLayout->setContentsMargins(12, 10, 12, 10);
    containerLayout->setSpacing(6);
    
    // Attachments (если есть) - отображаем ПЕРЕД текстом
    if (message_.content.hasAttachments()) {
        qDebug() << "MessageBubble::setupUI - message has" << message_.content.attachments.size() << "attachments";
        auto* attachmentGrid = new AttachmentGridView(message_.content.attachments, container);
        
        // Подключаем сигнал клика на вложение
        connect(attachmentGrid, &AttachmentGridView::attachmentClicked,
                this, &MessageBubble::onAttachmentClicked);
        
        containerLayout->addWidget(attachmentGrid);
    }
    
    // Text (показываем даже если пустой, когда есть вложения)
    if (!message_.content.text.isEmpty() || !message_.content.hasAttachments()) {
        textLabel_ = new QLabel(message_.content.text);
        textLabel_->setWordWrap(true);
        textLabel_->setTextInteractionFlags(Qt::TextSelectableByMouse);
        textLabel_->setMaximumWidth(400);
        textLabel_->setStyleSheet("font-size: 16px;");  // Larger font for better emoji visibility
        containerLayout->addWidget(textLabel_);
    } else {
        textLabel_ = nullptr;
    }
    
    // Progress bar for pending messages with attachments
    if (message_.isSending() && message_.content.hasAttachments()) {
        auto* progressContainer = new QWidget(container);
        auto* progressLayout = new QHBoxLayout(progressContainer);
        progressLayout->setContentsMargins(0, 4, 0, 0);
        
        auto* progressBar = new QProgressBar(progressContainer);
        progressBar->setRange(0, 100);
        progressBar->setValue(static_cast<int>(message_.uploadProgress * 100));
        progressBar->setTextVisible(false);
        progressBar->setFixedHeight(4);
        progressBar->setStyleSheet(
            "QProgressBar { background-color: rgba(255,255,255,0.3); border-radius: 2px; }"
            "QProgressBar::chunk { background-color: rgba(255,255,255,0.8); border-radius: 2px; }"
        );
        progressLayout->addWidget(progressBar);
        containerLayout->addLayout(progressLayout);
    }
    
    // Error message for failed messages
    if (message_.hasSendError() && !message_.sendError.isEmpty()) {
        auto* errorLabel = new QLabel(tr("Ошибка: %1").arg(message_.sendError), container);
        errorLabel->setStyleSheet("color: #f44336; font-size: 11px;");
        containerLayout->addWidget(errorLabel);
        
        // Retry button
        auto* retryBtn = new QPushButton(tr("Повторить"), container);
        retryBtn->setStyleSheet(
            "QPushButton { background-color: #f44336; color: white; "
            "border: none; border-radius: 4px; padding: 4px 12px; font-size: 11px; }"
            "QPushButton:hover { background-color: #d32f2f; }"
        );
        containerLayout->addWidget(retryBtn, 0, Qt::AlignLeft);
    }
    
    // Time and status
    timeLabel_ = new QLabel(formatTime(message_.sentAt));
    timeLabel_->setStyleSheet("font-size: 10px; color: #999;");
    
    auto* footerLayout = new QHBoxLayout;
    footerLayout->setSpacing(4);
    footerLayout->addWidget(timeLabel_);
    
    if (side_ == Side::Right) {
        // Sending indicator
        if (message_.isSending()) {
            statusLabel_ = new QLabel(tr("⏳ Отправка..."), container);
            statusLabel_->setStyleSheet("font-size: 10px; color: rgba(255,255,255,0.7);");
        } else {
            statusLabel_ = new QLabel("✓", container);
            statusLabel_->setStyleSheet("font-size: 10px; color: #999;");
        }
        footerLayout->addWidget(statusLabel_);
        footerLayout->addStretch();
    }
    
    containerLayout->addLayout(footerLayout);
    
    // Make container transparent - background is drawn in paintEvent
    container->setAttribute(Qt::WA_TranslucentBackground, true);
    container->setStyleSheet("QWidget { background: transparent; }");
    
    // Style text based on side
    if (side_ == Side::Right) {
        if (textLabel_) textLabel_->setStyleSheet("font-size: 16px; color: white;");
        if (timeLabel_) timeLabel_->setStyleSheet("font-size: 10px; color: rgba(255,255,255,0.7);");
    } else {
        if (textLabel_) textLabel_->setStyleSheet("font-size: 16px; color: black;");
        if (timeLabel_) timeLabel_->setStyleSheet("font-size: 10px; color: #999;");
    }
    
    layout->addWidget(container);
}

QString MessageBubble::formatTime(const QDateTime& time) const {
    return time.toString("HH:mm");
}

void MessageBubble::paintEvent(QPaintEvent* event) {
    Q_UNUSED(event);
    
    QPainter painter(this);
    painter.setRenderHint(QPainter::Antialiasing, true);
    
    // Рисуем форму пузырька с хвостиком
    QPainterPath path = bubblePath();
    
    // Цвет фона
    QColor bgColor = (side_ == Side::Right) ? QColor("#2196F3") : QColor("#f0f0f0");
    painter.fillPath(path, bgColor);
}

QPainterPath MessageBubble::bubblePath() const {
    return MessageBubbleWidget::createBubblePathWithCorners(
        rect(),
        corners_,
        18.0
    );
}

void MessageBubble::setGroupPosition(bool isFirst, bool isLast, bool isOnly) {
    if (side_ == Side::Right) {
        // Исходящие сообщения (хвостик справа)
        if (isOnly) {
            corners_ = BubbleCorners::outgoingOnly();
        } else if (isFirst) {
            corners_ = BubbleCorners::outgoingFirst();
        } else if (isLast) {
            corners_ = BubbleCorners::outgoingLast();
        } else {
            corners_ = BubbleCorners::outgoingMiddle();
        }
    } else {
        // Входящие сообщения (хвостик слева)
        if (isOnly) {
            corners_ = BubbleCorners::incomingOnly();
        } else if (isFirst) {
            corners_ = BubbleCorners::incomingFirst();
        } else if (isLast) {
            corners_ = BubbleCorners::incomingLast();
        } else {
            corners_ = BubbleCorners::incomingMiddle();
        }
    }
    
    showTail_ = isLast || isOnly;  // Хвостик только у последнего или единственного
    update();
}

void MessageBubble::updateReadStatus(const QList<qint64>& readBy) {
    if (side_ == Side::Right && statusLabel_) {
        // Check if read by others
        bool read = false;
        for (auto uid : readBy) {
            if (uid != message_.senderId) {
                read = true;
                break;
            }
        }
        
        if (read) {
            statusLabel_->setText("✓✓");
            statusLabel_->setStyleSheet("font-size: 10px; color: #4CAF50;");
        }
    }
}

void MessageBubble::onAttachmentClicked(const MessageAttachment& attachment) {
    qDebug() << "MessageBubble::onAttachmentClicked - attachment:" << attachment.fileName;
    
    // Находим индекс кликнутого вложения
    int index = 0;
    for (int i = 0; i < message_.content.attachments.size(); ++i) {
        if (message_.content.attachments[i].fileId == attachment.fileId) {
            index = i;
            break;
        }
    }
    
    // Эмиттим сигнал со всеми вложениями и индексом кликнутого
    emit attachmentClicked(message_.content.attachments, index);
}

// ============================================================================
// MessageInputWidget
// ============================================================================

MessageInputWidget::MessageInputWidget(QWidget* parent)
    : QWidget(parent)
{
    setupUI();
}

void MessageInputWidget::setupUI() {
    auto* layout = new QHBoxLayout(this);
    layout->setContentsMargins(8, 8, 8, 8);
    layout->setSpacing(8);
    
    attachButton_ = new QPushButton("+");
    attachButton_->setFixedSize(36, 36);
    layout->addWidget(attachButton_);
    
    inputEdit_ = new QTextEdit;
    inputEdit_->setPlaceholderText(tr("Сообщение..."));
    inputEdit_->setMaximumHeight(80);
    layout->addWidget(inputEdit_, 1);
    
    sendButton_ = new QPushButton("➤");
    sendButton_->setFixedSize(36, 36);
    connect(sendButton_, &QPushButton::clicked, this, &MessageInputWidget::onSendClicked);
    layout->addWidget(sendButton_);
}

void MessageInputWidget::clear() {
    inputEdit_->clear();
}

void MessageInputWidget::setEnabled(bool enabled) {
    inputEdit_->setEnabled(enabled);
    sendButton_->setEnabled(enabled);
    attachButton_->setEnabled(enabled);
}

void MessageInputWidget::onSendClicked() {
    QString text = inputEdit_->toPlainText().trimmed();
    if (!text.isEmpty()) {
        emit sendClicked(text);
    }
}

} // namespace BarkFluff