#include "LoginPage.h"
#include "Utils/JwtUtils.h"
#include "Services/SessionManager.h"
#include "Storage/AppSettings.h"
#include <QVBoxLayout>
#include <QHBoxLayout>
#include <QProgressBar>
#include <QInputDialog>
#include <QMessageBox>
#include <QtConcurrent>
#include <QDebug>

namespace BarkFluff {

LoginPage::LoginPage(const ServerConfiguration& serverConfig, QWidget* parent)
    : QWidget(parent)
    , serverConfig_(serverConfig)
{
    identityClient_ = new IdentityClient(serverConfig.identity);
    setupUi();
}

void LoginPage::setupUi() {
    auto* layout = new QVBoxLayout(this);
    layout->setContentsMargins(30, 30, 30, 30);
    layout->setSpacing(15);
    
    // Server name
    serverNameLabel_ = new QLabel(serverConfig_.name, this);
    serverNameLabel_->setStyleSheet("font-size: 14px; color: #666;");
    serverNameLabel_->setAlignment(Qt::AlignCenter);
    layout->addWidget(serverNameLabel_);
    
    // Title
    auto* titleLabel = new QLabel(tr("Welcome"), this);
    titleLabel->setStyleSheet("font-size: 24px; font-weight: bold;");
    titleLabel->setAlignment(Qt::AlignCenter);
    layout->addWidget(titleLabel);
    
    layout->addSpacing(20);
    
    // Login field
    loginEdit_ = new QLineEdit(this);
    loginEdit_->setPlaceholderText(tr("Username or Email"));
    loginEdit_->setStyleSheet("padding: 12px; border: 1px solid #ccc; border-radius: 6px; font-size: 14px;");
    layout->addWidget(loginEdit_);
    
    // Password field
    passwordEdit_ = new QLineEdit(this);
    passwordEdit_->setPlaceholderText(tr("Password"));
    passwordEdit_->setEchoMode(QLineEdit::Password);
    passwordEdit_->setStyleSheet("padding: 12px; border: 1px solid #ccc; border-radius: 6px; font-size: 14px;");
    layout->addWidget(passwordEdit_);
    
    // OTP field (hidden by default)
    otpEdit_ = new QLineEdit(this);
    otpEdit_->setPlaceholderText(tr("2FA Code"));
    otpEdit_->setMaxLength(6);
    otpEdit_->setStyleSheet("padding: 12px; border: 1px solid #ccc; border-radius: 6px; font-size: 14px;");
    otpEdit_->hide();
    layout->addWidget(otpEdit_);
    
    // Status label
    statusLabel_ = new QLabel(this);
    statusLabel_->setStyleSheet("color: #d32f2f;");
    statusLabel_->setAlignment(Qt::AlignCenter);
    statusLabel_->hide();
    layout->addWidget(statusLabel_);
    
    layout->addSpacing(10);
    
    // Login button
    loginButton_ = new QPushButton(tr("Login"), this);
    loginButton_->setStyleSheet(R"(
        QPushButton {
            padding: 12px;
            background: #3DAEE9;
            color: white;
            border: none;
            border-radius: 6px;
            font-size: 14px;
            font-weight: bold;
        }
        QPushButton:hover {
            background: #2196F3;
        }
        QPushButton:disabled {
            background: #ccc;
        }
    )");
    connect(loginButton_, &QPushButton::clicked, this, &LoginPage::onLoginClicked);
    layout->addWidget(loginButton_);
    
    // Register button
    registerButton_ = new QPushButton(tr("Create Account"), this);
    registerButton_->setFlat(true);
    registerButton_->setStyleSheet("color: #3DAEE9; padding: 10px;");
    connect(registerButton_, &QPushButton::clicked, this, &LoginPage::onRegisterClicked);
    layout->addWidget(registerButton_);
    
    // Enter key handling
    connect(loginEdit_, &QLineEdit::returnPressed, this, &LoginPage::onLoginClicked);
    connect(passwordEdit_, &QLineEdit::returnPressed, this, &LoginPage::onLoginClicked);
    connect(otpEdit_, &QLineEdit::returnPressed, this, &LoginPage::onLoginClicked);
}

void LoginPage::setLogin(const QString& login) {
    loginEdit_->setText(login);
    currentLogin_ = login;
    // Focus on password field since login is pre-filled
    passwordEdit_->setFocus();
}

void LoginPage::onLoginClicked() {
    QString login = loginEdit_->text().trimmed();
    QString password = passwordEdit_->text();
    QString otpCode = otpEdit_->text().trimmed();
    
    if (login.isEmpty() || password.isEmpty()) {
        statusLabel_->setText(tr("Please enter login and password"));
        statusLabel_->show();
        return;
    }
    
    if (waitingForOtp_ && otpCode.isEmpty()) {
        statusLabel_->setText(tr("Please enter 2FA code"));
        statusLabel_->show();
        return;
    }
    
    currentLogin_ = login;
    performLogin(login, password, otpCode);
}

void LoginPage::performLogin(const QString& login, const QString& password, const QString& otpCode) {
    loginButton_->setEnabled(false);
    statusLabel_->hide();
    
    QtConcurrent::run([this, login, password, otpCode]() {
        try {
            AuthResult result = identityClient_->auth(login, password, otpCode);
            
            QMetaObject::invokeMethod(this, [this, result]() {
                UserSession session;
                session.accessToken = result.accessToken;
                session.refreshToken = result.refreshToken;
                
                // Extract userId from JWT token
                session.userId = JwtUtils::extractUserId(result.accessToken.value);
                session.username = JwtUtils::extractUsername(result.accessToken.value);
                
                qDebug() << "Login successful: userId =" << session.userId 
                         << "username =" << session.username;
                
                // Save session
                saveSession(session, currentLogin_);
                
                emit loginSuccess(session);
            });
        } catch (const std::exception& e) {
            QString error = QString::fromStdString(e.what());
            
            // Check if 2FA is required
            if (error.contains("2FA") || error.contains("OTP") || error.contains("code")) {
                QMetaObject::invokeMethod(this, [this]() {
                    waitingForOtp_ = true;
                    otpEdit_->show();
                    otpEdit_->setFocus();
                    statusLabel_->setText(tr("Please enter your 2FA code"));
                    statusLabel_->setStyleSheet("color: #FF9800;");
                    statusLabel_->show();
                    loginButton_->setEnabled(true);
                    loginButton_->setText(tr("Verify"));
                });
            } else {
                QMetaObject::invokeMethod(this, [this, error]() {
                    statusLabel_->setText(error);
                    statusLabel_->setStyleSheet("color: #d32f2f;");
                    statusLabel_->show();
                    loginButton_->setEnabled(true);
                });
            }
        }
    });
}

void LoginPage::saveSession(const UserSession& session, const QString& login) {
    auto& sessionManager = SessionManager::instance();
    
    // Check if this is first time setup (no PIN yet)
    if (sessionManager.isFirstTimeSetup()) {
        // Ask user to create a PIN
        bool ok1, ok2;
        QString pin1 = QInputDialog::getText(this, tr("Create PIN"),
            tr("Create a PIN to protect your session:"), QLineEdit::Password, "", &ok1);
        
        if (!ok1 || pin1.isEmpty()) {
            // User cancelled - don't save session
            qDebug() << "LoginPage: PIN creation cancelled, session not saved";
            return;
        }
        
        if (pin1.length() < 4) {
            QMessageBox::warning(this, tr("Invalid PIN"), tr("PIN must be at least 4 characters."));
            return;
        }
        
        QString pin2 = QInputDialog::getText(this, tr("Confirm PIN"),
            tr("Confirm your PIN:"), QLineEdit::Password, "", &ok2);
        
        if (!ok2 || pin1 != pin2) {
            QMessageBox::warning(this, tr("Error"), tr("PINs do not match. Session not saved."));
            return;
        }
        
        // Initialize with new PIN
        if (!sessionManager.initializeWithPin(pin1)) {
            QMessageBox::warning(this, tr("Error"), 
                tr("Failed to initialize secure storage: %1").arg(sessionManager.lastError()));
            return;
        }
        
        // Save PIN length for UI
        sessionManager.setPinLength(pin1.length());
    }
    
    // Save server info to settings (beacon for reconnection)
    AppSettings::instance().setLastServer(
        serverConfig_.beaconEndpoint.host,
        serverConfig_.beaconEndpoint.port,
        serverConfig_.name
    );
    
    // Save session
    sessionManager.saveSession(session, serverConfig_, login);
    
    qDebug() << "LoginPage: Session saved for" << login;
}

void LoginPage::onRegisterClicked() {
    emit registerRequested();
}

} // namespace BarkFluff