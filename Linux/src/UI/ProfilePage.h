#pragma once

#include <QWidget>
#include <QLineEdit>
#include <QTextEdit>
#include <QPushButton>
#include <QLabel>
#include <QTimer>
#include <QFutureWatcher>
#include <memory>

#include "Models/User.h"
#include "Models/ServerConfiguration.h"
#include "Models/UserSession.h"
#include "Models/UsernameValidationStatus.h"
#include "Connection/UsersClient.h"
#include "Connection/FilesClient.h"
#include "UI/Widgets/AvatarWidget.h"

namespace BarkFluff {

/**
 * @brief Страница редактирования профиля пользователя
 * 
 * Позволяет редактировать:
 * - Аватар (с загрузкой изображения)
 * - Имя и фамилию
 * - О себе (bio)
 * - Username (с валидацией)
 */
class ProfilePage : public QWidget {
    Q_OBJECT

public:
    explicit ProfilePage(
        const ServiceInfo& usersConfig,
        const QString& accessToken,
        qint64 userId,
        QWidget* parent = nullptr
    );
    ~ProfilePage() override;

    void loadProfile();

signals:
    void logoutRequested();
    void profileUpdated(const User& user);
    void backRequested();

private slots:
    void onSaveClicked();
    void onLogoutClicked();
    void onAvatarClicked();
    void onFirstNameChanged(const QString& text);
    void onLastNameChanged(const QString& text);
    void onUsernameChanged(const QString& text);
    void onBioChanged();
    void validateUsernameDebounced();
    void onUsernameValidationFinished();

private:
    void setupUI();
    void updateSaveButtonState();
    bool hasChanges() const;
    QString currentInitials() const;
    void showLoading(bool show);
    void showError(const QString& message);
    void updateAvatar();

    // Services
    QString accessToken_;
    qint64 userId_;
    std::unique_ptr<UsersClient> usersClient_;
    std::unique_ptr<FilesClient> filesClient_;

    // User data
    User currentUser_;
    User originalUser_;

    // UI State
    bool isLoading_ = false;
    bool isSaving_ = false;
    UsernameValidationStatus usernameStatus_ = UsernameValidationStatus::None;

    // Username validation
    QTimer* usernameDebounceTimer_ = nullptr;
    QFutureWatcher<bool>* usernameCheckWatcher_ = nullptr;
    QString pendingUsername_;

    // UI Components
    AvatarWidget* avatarWidget_ = nullptr;
    QLineEdit* firstNameEdit_ = nullptr;
    QLineEdit* lastNameEdit_ = nullptr;
    QLineEdit* usernameEdit_ = nullptr;
    QLabel* usernameStatusLabel_ = nullptr;
    QTextEdit* bioEdit_ = nullptr;
    QLabel* bioCounterLabel_ = nullptr;
    QPushButton* saveButton_ = nullptr;
    QPushButton* logoutButton_ = nullptr;
    QWidget* loadingOverlay_ = nullptr;
};

} // namespace BarkFluff