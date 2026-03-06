#pragma once

#include <QWidget>
#include <QLineEdit>
#include <QPushButton>
#include <QLabel>
#include "Models/ServerConfiguration.h"
#include "Models/UserSession.h"
#include "Connection/IdentityClient.h"

namespace BarkFluff {

class LoginPage : public QWidget {
    Q_OBJECT
    
public:
    explicit LoginPage(const ServerConfiguration& serverConfig, QWidget* parent = nullptr);
    
    // Pre-fill login field
    void setLogin(const QString& login);
    
signals:
    void loginSuccess(const UserSession& session);
    void registerRequested();
    
private slots:
    void onLoginClicked();
    void onRegisterClicked();
    
private:
    void setupUi();
    void performLogin(const QString& login, const QString& password, const QString& otpCode = QString());
    void saveSession(const UserSession& session, const QString& login);
    
    ServerConfiguration serverConfig_;
    IdentityClient* identityClient_ = nullptr;
    
    QLineEdit* loginEdit_;
    QLineEdit* passwordEdit_;
    QLineEdit* otpEdit_;
    QPushButton* loginButton_;
    QPushButton* registerButton_;
    QLabel* statusLabel_;
    QLabel* serverNameLabel_;
    
    bool waitingForOtp_ = false;
    QString currentLogin_;  // Store login for session saving
};

} // namespace BarkFluff
