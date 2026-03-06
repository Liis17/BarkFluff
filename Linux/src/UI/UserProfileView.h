/**
 * @file UserProfileView.h
 * @brief Страница просмотра профиля пользователя
 */

#pragma once

#include <QWidget>
#include <QScrollArea>
#include <QLabel>
#include <QPushButton>
#include <QFutureWatcher>
#include <memory>

#include "Models/User.h"
#include "Models/Chat.h"
#include "Models/ServerConfiguration.h"
#include "Connection/UsersClient.h"
#include "Connection/OnlinerClient.h"
#include "Connection/MessagesClient.h"
#include "Connection/FilesClient.h"
#include "UI/Widgets/AvatarWidget.h"
#include "UI/Widgets/OnlineStatusWidget.h"
#include "UI/Widgets/SharedMediaSection.h"

namespace BarkFluff {

/**
 * @brief Страница просмотра профиля пользователя или группы
 * 
 * Открывается:
 * - Из чата (клик на аватар в header)
 * - Из списка участников группы
 * - Из "Saved Messages" (свой профиль в режиме просмотра)
 */
class UserProfileView : public QWidget {
    Q_OBJECT

public:
    /**
     * @brief Конструктор
     * @param usersConfig Конфигурация Users сервиса
     * @param onlinerConfig Конфигурация Onliner сервиса
     * @param messagesConfig Конфигурация Messages сервиса
     * @param filesConfig Конфигурация Files сервиса
     * @param accessToken Токен авторизации
     * @param currentUserId ID текущего пользователя
     * @param chat Чат (для определения типа и участников)
     * @param parent Родительский виджет
     */
    explicit UserProfileView(
        const ServiceInfo& usersConfig,
        const ServiceInfo& onlinerConfig,
        const ServiceInfo& messagesConfig,
        const ServiceInfo& filesConfig,
        const QString& accessToken,
        qint64 currentUserId,
        const Chat& chat,
        QWidget* parent = nullptr
    );
    
    ~UserProfileView() override;

signals:
    void backRequested();
    void openChatRequested(qint64 chatId);
    void editProfileRequested();

private slots:
    void onBackClicked();
    void onActionClicked();
    void onProfileLoaded();
    void onBadgesLoaded();
    void onLoadMediaRequested(const QString& chatId, int offset, int size);
    void onLoadDocumentsRequested(const QString& chatId, int offset, int size);
    void onMediaItemClicked(const SharedMediaItem& item, int index);
    void onDocumentItemClicked(const SharedMediaItem& item, int index);

private:
    void setupUI();
    void loadProfile();
    void loadBadges();
    void updateUI();
    void showLoading(bool show);
    void showError(const QString& message);
    QString formatRegistrationDate() const;

    // Services
    QString accessToken_;
    qint64 currentUserId_;
    Chat chat_;
    std::unique_ptr<UsersClient> usersClient_;
    OnlinerClient* onlinerClient_ = nullptr;
    std::unique_ptr<MessagesClient> messagesClient_;
    ServiceInfo filesConfig_;
    
    // Profile state
    User user_;
    bool isOwnProfile_ = false;
    bool isLoading_ = false;
    
    // Async loaders
    QFutureWatcher<User>* profileWatcher_ = nullptr;
    QFutureWatcher<QList<UserBadge>>* badgesWatcher_ = nullptr;
    
    // UI Components - Header
    QWidget* headerWidget_ = nullptr;
    QPushButton* backButton_ = nullptr;
    
    // UI Components - Profile
    QScrollArea* scrollArea_ = nullptr;
    QWidget* contentWidget_ = nullptr;
    AvatarWidget* avatarWidget_ = nullptr;
    QLabel* nameLabel_ = nullptr;
    QLabel* usernameLabel_ = nullptr;
    OnlineStatusWidget* onlineStatusWidget_ = nullptr;
    
    // UI Components - Info
    QLabel* bioLabel_ = nullptr;
    QWidget* badgesContainer_ = nullptr;
    QLabel* userIdLabel_ = nullptr;
    QLabel* registrationLabel_ = nullptr;
    QLabel* storageLabel_ = nullptr;
    
    // UI Components - Actions
    QPushButton* actionButton_ = nullptr;
    
    // UI Components - Shared Media
    SharedMediaSection* sharedMediaSection_ = nullptr;
    
    // UI Components - Loading/Error
    QWidget* loadingWidget_ = nullptr;
    QLabel* errorLabel_ = nullptr;
};

} // namespace BarkFluff