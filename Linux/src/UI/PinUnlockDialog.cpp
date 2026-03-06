#include "PinUnlockDialog.h"
#include <QVBoxLayout>
#include <QHBoxLayout>
#include <QLabel>
#include <QPushButton>

namespace BarkFluff {

PinUnlockDialog::PinUnlockDialog(QWidget* parent)
    : QDialog(parent)
{
    setupUi();
}

void PinUnlockDialog::setupUi() {
    setWindowTitle(tr("Unlock BarkFluff"));
    setFixedSize(360, 400);
    setWindowFlags(windowFlags() & ~Qt::WindowContextHelpButtonHint);
    
    // Apply dialog style
    setStyleSheet(R"(
        QDialog {
            background: white;
        }
    )");
    
    auto* mainLayout = new QVBoxLayout(this);
    mainLayout->setContentsMargins(30, 30, 30, 24);
    mainLayout->setSpacing(16);
    
    // Server name (gray, centered at top)
    serverLabel_ = new QLabel(this);
    serverLabel_->setStyleSheet("font-size: 12px; color: #888;");
    serverLabel_->setAlignment(Qt::AlignCenter);
    mainLayout->addWidget(serverLabel_);
    
    mainLayout->addSpacing(8);
    
    // Welcome title
    welcomeLabel_ = new QLabel(tr("Welcome back!"), this);
    welcomeLabel_->setStyleSheet("font-size: 22px; font-weight: bold; color: #333;");
    welcomeLabel_->setAlignment(Qt::AlignCenter);
    mainLayout->addWidget(welcomeLabel_);
    
    // Subtitle
    auto* subtitleLabel = new QLabel(tr("Enter your PIN to continue"), this);
    subtitleLabel->setStyleSheet("font-size: 13px; color: #666;");
    subtitleLabel->setAlignment(Qt::AlignCenter);
    mainLayout->addWidget(subtitleLabel);
    
    mainLayout->addSpacing(8);
    
    // PIN input widget (default 4 digits)
    pinInput_ = new PinCodeInputWidget(4, this);
    connect(pinInput_, &PinCodeInputWidget::pinEntered, this, [this]() {
        // Emit unlockRequested when PIN is fully entered
        if (unlockButton_->isEnabled()) {
            emit unlockRequested();
        }
    });
    mainLayout->addWidget(pinInput_);
    
    // Error label
    errorLabel_ = new QLabel(this);
    errorLabel_->setStyleSheet("font-size: 12px; color: #d32f2f;");
    errorLabel_->setAlignment(Qt::AlignCenter);
    errorLabel_->setWordWrap(true);
    errorLabel_->hide();
    mainLayout->addWidget(errorLabel_);
    
    mainLayout->addStretch();
    
    // Unlock button
    unlockButton_ = new QPushButton(tr("Unlock"), this);
    unlockButton_->setStyleSheet(R"(
        QPushButton {
            padding: 12px;
            background: #3DAEE9;
            color: white;
            border: none;
            border-radius: 8px;
            font-size: 14px;
            font-weight: bold;
        }
        QPushButton:hover {
            background: #2196F3;
        }
        QPushButton:pressed {
            background: #1976D2;
        }
        QPushButton:disabled {
            background: #ccc;
        }
    )");
    connect(unlockButton_, &QPushButton::clicked, this, &PinUnlockDialog::unlockRequested);
    mainLayout->addWidget(unlockButton_);
    
    // Switch account link
    switchAccountButton_ = new QPushButton(tr("Sign in with a different account"), this);
    switchAccountButton_->setFlat(true);
    switchAccountButton_->setStyleSheet(R"(
        QPushButton {
            color: #888;
            font-size: 12px;
            padding: 8px;
            background: transparent;
            border: none;
        }
        QPushButton:hover {
            color: #666;
            text-decoration: underline;
        }
    )");
    connect(switchAccountButton_, &QPushButton::clicked, this, &QDialog::reject);
    mainLayout->addWidget(switchAccountButton_);
    
    // Set focus to PIN input
    pinInput_->setFocusToFirst();
}

void PinUnlockDialog::setUsername(const QString& username) {
    username_ = username;
    if (username.isEmpty()) {
        welcomeLabel_->setText(tr("Welcome back!"));
    } else {
        welcomeLabel_->setText(tr("Welcome back, %1!").arg(username));
    }
}

void PinUnlockDialog::setServerName(const QString& serverName) {
    serverName_ = serverName;
    if (serverName.isEmpty()) {
        serverLabel_->clear();
        serverLabel_->hide();
    } else {
        serverLabel_->setText(serverName);
        serverLabel_->show();
    }
}

void PinUnlockDialog::setPinLength(int length) {
    pinInput_->setPinLength(length);
    pinInput_->setFocusToFirst();
}

QString PinUnlockDialog::pin() const {
    return pinInput_->pin();
}

void PinUnlockDialog::showError(const QString& message) {
    errorLabel_->setText(message);
    errorLabel_->show();
    pinInput_->clear();
    pinInput_->setFocusToFirst();
}

void PinUnlockDialog::clearError() {
    errorLabel_->hide();
    errorLabel_->clear();
}

void PinUnlockDialog::setInputsEnabled(bool enabled) {
    pinInput_->setEnabled(enabled);
    unlockButton_->setEnabled(enabled);
    switchAccountButton_->setEnabled(enabled);
}

} // namespace BarkFluff