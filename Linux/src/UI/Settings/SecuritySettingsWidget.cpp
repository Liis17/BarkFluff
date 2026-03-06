/**
 * @file SecuritySettingsWidget.cpp
 * @brief Реализация виджета безопасности
 */

#include "SecuritySettingsWidget.h"
#include "Connection/IdentityClient.h"

#include <QVBoxLayout>
#include <QHBoxLayout>
#include <QDialog>
#include <QDialogButtonBox>
#include <QLineEdit>
#include <QLabel>
#include <QPixmap>
#include <QByteArray>
#include <QMessageBox>
#include <QtConcurrent>

namespace BarkFluff {

SecuritySettingsWidget::SecuritySettingsWidget(
    IdentityClient* client,
    const UserSession& session,
    QWidget* parent
)
    : QWidget(parent)
    , identityClient_(client)
    , session_(session)
{
    statusWatcher_ = new QFutureWatcher<OTPStatus>(this);
    connect(statusWatcher_, &QFutureWatcher<OTPStatus>::finished,
            this, &SecuritySettingsWidget::onOTPStatusLoaded);
    
    setupUI();
}

void SecuritySettingsWidget::setupUI() {
    auto* mainLayout = new QVBoxLayout(this);
    mainLayout->setContentsMargins(24, 24, 24, 24);
    mainLayout->setSpacing(16);
    
    // Title
    auto* titleLabel = new QLabel(tr("Безопасность"), this);
    titleLabel->setStyleSheet(QStringLiteral("font-size: 18px; font-weight: bold; color: #333333;"));
    mainLayout->addWidget(titleLabel);
    
    // 2FA Section
    auto* twoFAFrame = new QWidget(this);
    twoFAFrame->setStyleSheet(R"(
        QWidget {
            background-color: #f5f5f5;
            border-radius: 8px;
        }
    )");
    auto* twoFALayout = new QVBoxLayout(twoFAFrame);
    twoFALayout->setContentsMargins(16, 16, 16, 16);
    twoFALayout->setSpacing(8);
    
    twoFALabel_ = new QLabel(tr("Двухфакторная аутентификация"), twoFAFrame);
    twoFALabel_->setStyleSheet(QStringLiteral("color: #333333; font-weight: bold;"));
    twoFALayout->addWidget(twoFALabel_);
    
    twoFADescription_ = new QLabel(tr("Неактивно"), twoFAFrame);
    twoFADescription_->setStyleSheet(QStringLiteral("color: #888888;"));
    twoFADescription_->setWordWrap(true);
    twoFALayout->addWidget(twoFADescription_);
    
    toggle2FABtn_ = new QPushButton(tr("Включить"), twoFAFrame);
    toggle2FABtn_->setStyleSheet(R"(
        QPushButton {
            background-color: #0078d4;
            color: #ffffff;
            border: none;
            padding: 8px 16px;
            border-radius: 4px;
        }
        QPushButton:hover {
            background-color: #1084d8;
        }
    )");
    connect(toggle2FABtn_, &QPushButton::clicked, this, [this]() {
        if (otpStatus_.isAnyEnabled()) {
            onDisable2FAClicked();
        } else {
            onEnable2FAClicked();
        }
    });
    twoFALayout->addWidget(toggle2FABtn_);
    
    mainLayout->addWidget(twoFAFrame);
    
    // Password Section
    auto* passwordFrame = new QWidget(this);
    passwordFrame->setStyleSheet(R"(
        QWidget {
            background-color: #f5f5f5;
            border-radius: 8px;
        }
    )");
    auto* passwordLayout = new QVBoxLayout(passwordFrame);
    passwordLayout->setContentsMargins(16, 16, 16, 16);
    passwordLayout->setSpacing(8);
    
    auto* passwordLabel = new QLabel(tr("Пароль"), passwordFrame);
    passwordLabel->setStyleSheet(QStringLiteral("color: #333333; font-weight: bold;"));
    passwordLayout->addWidget(passwordLabel);
    
    auto* passwordDesc = new QLabel(tr("Измените пароль для защиты аккаунта"), passwordFrame);
    passwordDesc->setStyleSheet(QStringLiteral("color: #888888;"));
    passwordDesc->setWordWrap(true);
    passwordLayout->addWidget(passwordDesc);
    
    changePasswordBtn_ = new QPushButton(tr("Изменить пароль"), passwordFrame);
    changePasswordBtn_->setStyleSheet(R"(
        QPushButton {
            background-color: #3a3a3a;
            color: #ffffff;
            border: none;
            padding: 8px 16px;
            border-radius: 4px;
        }
        QPushButton:hover {
            background-color: #4a4a4a;
        }
    )");
    connect(changePasswordBtn_, &QPushButton::clicked, this, &SecuritySettingsWidget::onChangePasswordClicked);
    passwordLayout->addWidget(changePasswordBtn_);
    
    mainLayout->addWidget(passwordFrame);
    
    // Error label
    errorLabel_ = new QLabel(this);
    errorLabel_->setStyleSheet(QStringLiteral("color: #ff6b6b; padding: 10px;"));
    errorLabel_->setWordWrap(true);
    errorLabel_->hide();
    mainLayout->addWidget(errorLabel_);
    
    mainLayout->addStretch();
}

void SecuritySettingsWidget::loadOTPStatus() {
    if (isLoading_) return;
    
    isLoading_ = true;
    errorLabel_->hide();
    
    auto future = QtConcurrent::run([this]() {
        try {
            return identityClient_->getOTPStatus(session_.accessToken.value);
        } catch (...) {
            return OTPStatus{};
        }
    });
    
    statusWatcher_->setFuture(future);
}

void SecuritySettingsWidget::onOTPStatusLoaded() {
    isLoading_ = false;
    otpStatus_ = statusWatcher_->result();
    update2FAStatus();
}

void SecuritySettingsWidget::update2FAStatus() {
    if (otpStatus_.authenticatorEnabled) {
        twoFADescription_->setText(tr("Включена аутентификация по приложению"));
        toggle2FABtn_->setText(tr("Отключить"));
        toggle2FABtn_->setStyleSheet(R"(
            QPushButton {
                background-color: #3a3a3a;
                color: #ff6b6b;
                border: 1px solid #ff6b6b;
                padding: 8px 16px;
                border-radius: 4px;
            }
            QPushButton:hover {
                background-color: #ff6b6b;
                color: #ffffff;
            }
        )");
    } else {
        twoFADescription_->setText(tr("Неактивно. Рекомендуется включить для защиты аккаунта."));
        toggle2FABtn_->setText(tr("Включить"));
        toggle2FABtn_->setStyleSheet(R"(
            QPushButton {
                background-color: #0078d4;
                color: #ffffff;
                border: none;
                padding: 8px 16px;
                border-radius: 4px;
            }
            QPushButton:hover {
                background-color: #1084d8;
            }
        )");
    }
}

void SecuritySettingsWidget::onEnable2FAClicked() {
    try {
        auto otpInfo = identityClient_->enableOTP(session_.accessToken.value);
        showQRDialog(otpInfo.qrCodeBase64, otpInfo.otpCode);
    } catch (const std::exception& e) {
        errorLabel_->setText(tr("Ошибка: %1").arg(QString::fromStdString(e.what())));
        errorLabel_->show();
    }
}

void SecuritySettingsWidget::showQRDialog(const QString& qrBase64, const QString& otpCode) {
    auto dialog = new QDialog(this);
    dialog->setWindowTitle(tr("Настройка 2FA"));
    dialog->setMinimumWidth(350);
    dialog->setStyleSheet(QStringLiteral("background-color: #2a2a2a; color: #ffffff;"));
    
    auto* layout = new QVBoxLayout(dialog);
    layout->setSpacing(16);
    
    // QR Code
    auto* qrLabel = new QLabel(dialog);
    QByteArray imageData = QByteArray::fromBase64(qrBase64.toUtf8());
    QPixmap pixmap;
    pixmap.loadFromData(imageData);
    qrLabel->setPixmap(pixmap.scaled(200, 200, Qt::KeepAspectRatio, Qt::SmoothTransformation));
    qrLabel->setAlignment(Qt::AlignCenter);
    layout->addWidget(qrLabel);
    
    // Code for manual entry
    auto* codeLabel = new QLabel(tr("Код для ручного ввода:"), dialog);
    codeLabel->setAlignment(Qt::AlignCenter);
    layout->addWidget(codeLabel);
    
    auto* codeValue = new QLabel(otpCode, dialog);
    codeValue->setStyleSheet(QStringLiteral("font-family: monospace; font-size: 14px; background-color: #1e1e1e; padding: 8px; border-radius: 4px;"));
    codeValue->setTextInteractionFlags(Qt::TextSelectableByMouse);
    codeValue->setAlignment(Qt::AlignCenter);
    layout->addWidget(codeValue);
    
    // OTP confirmation input
    auto* inputLabel = new QLabel(tr("Введите код из приложения для подтверждения:"), dialog);
    layout->addWidget(inputLabel);
    
    auto* otpInput = new QLineEdit(dialog);
    otpInput->setPlaceholderText(tr("000000"));
    otpInput->setMaxLength(6);
    otpInput->setAlignment(Qt::AlignCenter);
    otpInput->setStyleSheet(QStringLiteral("font-size: 18px; padding: 8px; border-radius: 4px; background-color: #1e1e1e; border: 1px solid #3a3a3a; color: #ffffff;"));
    layout->addWidget(otpInput);
    
    // Buttons
    auto* buttons = new QDialogButtonBox(QDialogButtonBox::Ok | QDialogButtonBox::Cancel, dialog);
    buttons->setStyleSheet(QStringLiteral("QPushButton { padding: 8px 16px; border-radius: 4px; }"));
    connect(buttons, &QDialogButtonBox::accepted, dialog, &QDialog::accept);
    connect(buttons, &QDialogButtonBox::rejected, dialog, &QDialog::reject);
    layout->addWidget(buttons);
    
    if (dialog->exec() == QDialog::Accepted) {
        QString code = otpInput->text().trimmed();
        if (code.length() == 6) {
            try {
                identityClient_->confirmOTP(code, session_.accessToken.value);
                loadOTPStatus();
                QMessageBox::information(this, tr("Успех"), tr("Двухфакторная аутентификация включена"));
            } catch (const std::exception& e) {
                errorLabel_->setText(tr("Ошибка подтверждения: %1").arg(QString::fromStdString(e.what())));
                errorLabel_->show();
            }
        }
    }
    
    dialog->deleteLater();
}

void SecuritySettingsWidget::onDisable2FAClicked() {
    auto dialog = new QDialog(this);
    dialog->setWindowTitle(tr("Отключить 2FA"));
    dialog->setMinimumWidth(300);
    dialog->setStyleSheet(QStringLiteral("background-color: #2a2a2a; color: #ffffff;"));
    
    auto* layout = new QVBoxLayout(dialog);
    
    auto* descLabel = new QLabel(tr("Введите код из приложения для подтверждения отключения:"), dialog);
    descLabel->setWordWrap(true);
    layout->addWidget(descLabel);
    
    auto* otpInput = new QLineEdit(dialog);
    otpInput->setPlaceholderText(tr("000000"));
    otpInput->setMaxLength(6);
    otpInput->setAlignment(Qt::AlignCenter);
    otpInput->setStyleSheet(QStringLiteral("font-size: 18px; padding: 8px; border-radius: 4px; background-color: #1e1e1e; border: 1px solid #3a3a3a; color: #ffffff;"));
    layout->addWidget(otpInput);
    
    auto* buttons = new QDialogButtonBox(QDialogButtonBox::Ok | QDialogButtonBox::Cancel, dialog);
    connect(buttons, &QDialogButtonBox::accepted, dialog, &QDialog::accept);
    connect(buttons, &QDialogButtonBox::rejected, dialog, &QDialog::reject);
    layout->addWidget(buttons);
    
    if (dialog->exec() == QDialog::Accepted) {
        QString code = otpInput->text().trimmed();
        try {
            identityClient_->disableOTP(OTPType::Authenticator, code, session_.accessToken.value);
            loadOTPStatus();
            QMessageBox::information(this, tr("Успех"), tr("Двухфакторная аутентификация отключена"));
        } catch (const std::exception& e) {
            errorLabel_->setText(tr("Ошибка: %1").arg(QString::fromStdString(e.what())));
            errorLabel_->show();
        }
    }
    
    dialog->deleteLater();
}

void SecuritySettingsWidget::onChangePasswordClicked() {
    showChangePasswordDialog();
}

void SecuritySettingsWidget::showChangePasswordDialog() {
    auto dialog = new QDialog(this);
    dialog->setWindowTitle(tr("Изменить пароль"));
    dialog->setMinimumWidth(350);
    dialog->setStyleSheet(QStringLiteral("background-color: #2a2a2a; color: #ffffff;"));
    
    auto* layout = new QVBoxLayout(dialog);
    layout->setSpacing(12);
    
    // Old password
    auto* oldLabel = new QLabel(tr("Текущий пароль:"), dialog);
    layout->addWidget(oldLabel);
    
    auto* oldInput = new QLineEdit(dialog);
    oldInput->setEchoMode(QLineEdit::Password);
    oldInput->setStyleSheet(QStringLiteral("padding: 8px; border-radius: 4px; background-color: #1e1e1e; border: 1px solid #3a3a3a; color: #ffffff;"));
    layout->addWidget(oldInput);
    
    // New password
    auto* newLabel = new QLabel(tr("Новый пароль:"), dialog);
    layout->addWidget(newLabel);
    
    auto* newInput = new QLineEdit(dialog);
    newInput->setEchoMode(QLineEdit::Password);
    newInput->setStyleSheet(QStringLiteral("padding: 8px; border-radius: 4px; background-color: #1e1e1e; border: 1px solid #3a3a3a; color: #ffffff;"));
    layout->addWidget(newInput);
    
    // Confirm password
    auto* confirmLabel = new QLabel(tr("Подтвердите пароль:"), dialog);
    layout->addWidget(confirmLabel);
    
    auto* confirmInput = new QLineEdit(dialog);
    confirmInput->setEchoMode(QLineEdit::Password);
    confirmInput->setStyleSheet(QStringLiteral("padding: 8px; border-radius: 4px; background-color: #1e1e1e; border: 1px solid #3a3a3a; color: #ffffff;"));
    layout->addWidget(confirmInput);
    
    // Buttons
    auto* buttons = new QDialogButtonBox(QDialogButtonBox::Ok | QDialogButtonBox::Cancel, dialog);
    connect(buttons, &QDialogButtonBox::accepted, dialog, &QDialog::accept);
    connect(buttons, &QDialogButtonBox::rejected, dialog, &QDialog::reject);
    layout->addWidget(buttons);
    
    if (dialog->exec() == QDialog::Accepted) {
        QString oldPass = oldInput->text();
        QString newPass = newInput->text();
        QString confirmPass = confirmInput->text();
        
        if (newPass != confirmPass) {
            QMessageBox::warning(this, tr("Ошибка"), tr("Пароли не совпадают"));
            dialog->deleteLater();
            return;
        }
        
        if (newPass.length() < 8) {
            QMessageBox::warning(this, tr("Ошибка"), tr("Пароль должен содержать минимум 8 символов"));
            dialog->deleteLater();
            return;
        }
        
        try {
            identityClient_->changePassword(oldPass, newPass, session_.accessToken.value);
            QMessageBox::information(this, tr("Успех"), tr("Пароль успешно изменен"));
        } catch (const std::exception& e) {
            errorLabel_->setText(tr("Ошибка: %1").arg(QString::fromStdString(e.what())));
            errorLabel_->show();
        }
    }
    
    dialog->deleteLater();
}

} // namespace BarkFluff