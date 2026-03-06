/**
 * @file SecuritySettingsWidget.h
 * @brief Виджет настроек безопасности (2FA, смена пароля)
 */

#pragma once

#include <QWidget>
#include <QLabel>
#include <QPushButton>
#include <QLineEdit>
#include <QFutureWatcher>
#include "Models/SessionInfo.h"
#include "Models/UserSession.h"

namespace BarkFluff {

class IdentityClient;

/**
 * @brief Виджет для управления безопасностью аккаунта
 */
class SecuritySettingsWidget : public QWidget {
    Q_OBJECT

public:
    explicit SecuritySettingsWidget(
        IdentityClient* client,
        const UserSession& session,
        QWidget* parent = nullptr
    );

    void loadOTPStatus();

private slots:
    void onEnable2FAClicked();
    void onDisable2FAClicked();
    void onChangePasswordClicked();
    void onOTPStatusLoaded();

private:
    void setupUI();
    void update2FAStatus();
    void showQRDialog(const QString& qrBase64, const QString& otpCode);
    void showChangePasswordDialog();

    IdentityClient* identityClient_ = nullptr;
    UserSession session_;
    
    OTPStatus otpStatus_;
    bool isLoading_ = false;
    
    QFutureWatcher<OTPStatus>* statusWatcher_ = nullptr;
    
    QLabel* twoFALabel_ = nullptr;
    QLabel* twoFADescription_ = nullptr;
    QPushButton* toggle2FABtn_ = nullptr;
    QPushButton* changePasswordBtn_ = nullptr;
    QLabel* errorLabel_ = nullptr;
};

} // namespace BarkFluff