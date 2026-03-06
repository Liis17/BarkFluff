#include "RegisterPage.h"
#include "Utils/Validators.h"
#include "Utils/JwtUtils.h"
#include <QVBoxLayout>
#include <QHBoxLayout>
#include <QGridLayout>
#include <QtConcurrent>
#include <QPixmap>
#include <QBuffer>

namespace BarkFluff {

RegisterPage::RegisterPage(const ServerConfiguration& serverConfig, QWidget* parent)
    : QWidget(parent)
    , serverConfig_(serverConfig)
{
    identityClient_ = new IdentityClient(serverConfig_.identity);
    usersClient_ = new UsersClient(serverConfig_.users);
    filesClient_ = new FilesClient(serverConfig_.files);
    
    // Setup debounce timers
    usernameDebounceTimer_ = new QTimer(this);
    usernameDebounceTimer_->setSingleShot(true);
    usernameDebounceTimer_->setInterval(500);  // 500ms debounce
    connect(usernameDebounceTimer_, &QTimer::timeout, this, &RegisterPage::checkUsernameAvailability);
    
    emailDebounceTimer_ = new QTimer(this);
    emailDebounceTimer_->setSingleShot(true);
    emailDebounceTimer_->setInterval(500);  // 500ms debounce
    connect(emailDebounceTimer_, &QTimer::timeout, this, &RegisterPage::checkEmailAvailability);
    
    setupUi();
}

void RegisterPage::setupUi() {
    mainLayout_ = new QVBoxLayout(this);
    mainLayout_->setContentsMargins(30, 20, 30, 20);
    mainLayout_->setSpacing(12);
    
    // Server name
    serverLabel_ = new QLabel(serverConfig_.name, this);
    serverLabel_->setStyleSheet("font-size: 12px; color: #888;");
    serverLabel_->setAlignment(Qt::AlignCenter);
    mainLayout_->addWidget(serverLabel_);
    
    // Title
    titleLabel_ = new QLabel(tr("Create Account"), this);
    titleLabel_->setStyleSheet("font-size: 22px; font-weight: bold;");
    titleLabel_->setAlignment(Qt::AlignCenter);
    mainLayout_->addWidget(titleLabel_);
    
    // Step indicator
    stepIndicatorLabel_ = new QLabel(state_.stepIndicator(), this);
    stepIndicatorLabel_->setStyleSheet("font-size: 13px; color: #666;");
    stepIndicatorLabel_->setAlignment(Qt::AlignCenter);
    mainLayout_->addWidget(stepIndicatorLabel_);
    
    // Progress bar
    progressBar_ = new QProgressBar(this);
    progressBar_->setRange(0, 100);
    progressBar_->setValue(static_cast<int>(state_.progressPercentage()));
    progressBar_->setTextVisible(false);
    progressBar_->setFixedHeight(4);
    progressBar_->setStyleSheet(R"(
        QProgressBar {
            background: #e0e0e0;
            border: none;
            border-radius: 2px;
        }
        QProgressBar::chunk {
            background: #3DAEE9;
            border-radius: 2px;
        }
    )");
    mainLayout_->addWidget(progressBar_);
    
    // Stacked widget for steps
    stackedWidget_ = new QStackedWidget(this);
    mainLayout_->addWidget(stackedWidget_);
    
    // Setup all step widgets
    setupStepWidgets();
    
    // Error label
    errorLabel_ = new QLabel(this);
    errorLabel_->setStyleSheet("color: #d32f2f; font-size: 13px;");
    errorLabel_->setAlignment(Qt::AlignCenter);
    errorLabel_->setWordWrap(true);
    errorLabel_->hide();
    mainLayout_->addWidget(errorLabel_);
    
    // Navigation buttons
    auto* navLayout = new QHBoxLayout();
    navLayout->setSpacing(12);
    
    backButton_ = new QPushButton(tr("Back"), this);
    backButton_->setStyleSheet(R"(
        QPushButton {
            padding: 12px 24px;
            background: transparent;
            color: #666;
            border: 1px solid #ccc;
            border-radius: 6px;
            font-size: 14px;
        }
        QPushButton:hover { background: #f5f5f5; }
        QPushButton:disabled { color: #ccc; border-color: #eee; }
    )");
    connect(backButton_, &QPushButton::clicked, this, &RegisterPage::onBackClicked);
    navLayout->addWidget(backButton_);
    
    navLayout->addStretch();
    
    skipButton_ = new QPushButton(tr("Skip"), this);
    skipButton_->setStyleSheet(R"(
        QPushButton {
            padding: 12px 24px;
            background: transparent;
            color: #888;
            border: none;
            font-size: 14px;
        }
        QPushButton:hover { color: #666; }
    )");
    connect(skipButton_, &QPushButton::clicked, this, &RegisterPage::onSkipClicked);
    navLayout->addWidget(skipButton_);
    skipButton_->hide();
    
    nextButton_ = new QPushButton(tr("Continue"), this);
    nextButton_->setStyleSheet(R"(
        QPushButton {
            padding: 12px 32px;
            background: #3DAEE9;
            color: white;
            border: none;
            border-radius: 6px;
            font-size: 14px;
            font-weight: bold;
        }
        QPushButton:hover { background: #2196F3; }
        QPushButton:disabled { background: #ccc; }
    )");
    connect(nextButton_, &QPushButton::clicked, this, &RegisterPage::onNextClicked);
    navLayout->addWidget(nextButton_);
    
    mainLayout_->addLayout(navLayout);
    
    // Back to login link
    auto* backToLoginBtn = new QPushButton(tr("Already have an account? Sign in"), this);
    backToLoginBtn->setFlat(true);
    backToLoginBtn->setStyleSheet("color: #3DAEE9; padding: 8px; font-size: 13px;");
    connect(backToLoginBtn, &QPushButton::clicked, this, &RegisterPage::backToLogin);
    mainLayout_->addWidget(backToLoginBtn);
    
    // Show initial step
    showCurrentStep();
}

void RegisterPage::setupStepWidgets() {
    QString inputStyle = R"(
        QLineEdit {
            padding: 12px;
            border: 1px solid #ddd;
            border-radius: 6px;
            font-size: 14px;
            background: white;
        }
        QLineEdit:focus {
            border-color: #3DAEE9;
        }
    )";
    
    // === Step 1: Personal Info ===
    step1Widget_ = new QWidget();
    auto* step1Layout = new QVBoxLayout(step1Widget_);
    step1Layout->setContentsMargins(0, 0, 0, 0);
    step1Layout->setSpacing(10);
    
    firstNameEdit_ = new QLineEdit();
    firstNameEdit_->setPlaceholderText(tr("First Name"));
    firstNameEdit_->setStyleSheet(inputStyle);
    step1Layout->addWidget(firstNameEdit_);
    
    lastNameEdit_ = new QLineEdit();
    lastNameEdit_->setPlaceholderText(tr("Last Name (optional)"));
    lastNameEdit_->setStyleSheet(inputStyle);
    step1Layout->addWidget(lastNameEdit_);
    
    step1Layout->addStretch();
    stackedWidget_->addWidget(step1Widget_);
    
    // === Step 2: Username ===
    step2Widget_ = new QWidget();
    auto* step2Layout = new QVBoxLayout(step2Widget_);
    step2Layout->setContentsMargins(0, 0, 0, 0);
    step2Layout->setSpacing(8);
    
    usernameEdit_ = new QLineEdit();
    usernameEdit_->setPlaceholderText(tr("Username"));
    usernameEdit_->setStyleSheet(inputStyle);
    connect(usernameEdit_, &QLineEdit::textChanged, this, &RegisterPage::onUsernameTextChanged);
    step2Layout->addWidget(usernameEdit_);
    
    usernameStatus_ = new QLabel();
    usernameStatus_->setStyleSheet("font-size: 12px; color: #888;");
    usernameStatus_->setWordWrap(true);
    step2Layout->addWidget(usernameStatus_);
    
    step2Layout->addStretch();
    stackedWidget_->addWidget(step2Widget_);
    
    // === Step 3: Email ===
    step3Widget_ = new QWidget();
    auto* step3Layout = new QVBoxLayout(step3Widget_);
    step3Layout->setContentsMargins(0, 0, 0, 0);
    step3Layout->setSpacing(8);
    
    emailEdit_ = new QLineEdit();
    emailEdit_->setPlaceholderText(tr("Email Address"));
    emailEdit_->setStyleSheet(inputStyle);
    connect(emailEdit_, &QLineEdit::textChanged, this, &RegisterPage::onEmailTextChanged);
    step3Layout->addWidget(emailEdit_);
    
    emailStatus_ = new QLabel(tr("A confirmation code will be sent to this email"));
    emailStatus_->setStyleSheet("font-size: 12px; color: #888;");
    emailStatus_->setWordWrap(true);
    step3Layout->addWidget(emailStatus_);
    
    step3Layout->addStretch();
    stackedWidget_->addWidget(step3Widget_);
    
    // === Step 4: Confirm Email ===
    step4Widget_ = new QWidget();
    auto* step4Layout = new QVBoxLayout(step4Widget_);
    step4Layout->setSpacing(12);
    
    auto* codeInfoLabel = new QLabel(tr("Enter the 6-digit code sent to your email"));
    codeInfoLabel->setStyleSheet("font-size: 13px; color: #666;");
    codeInfoLabel->setAlignment(Qt::AlignCenter);
    step4Layout->addWidget(codeInfoLabel);
    
    // 6 separate fields for OTP
    auto* codeLayout = new QHBoxLayout();
    codeLayout->setSpacing(8);
    
    QString codeStyle = R"(
        QLineEdit {
            padding: 8px;
            border: 2px solid #ddd;
            border-radius: 8px;
            font-size: 20px;
            font-weight: bold;
            background: white;
            text-align: center;
        }
        QLineEdit:focus {
            border-color: #3DAEE9;
        }
    )";
    
    codeEdit1_ = new QLineEdit(); codeEdit1_->setMaxLength(1); codeEdit1_->setAlignment(Qt::AlignCenter); codeEdit1_->setFixedWidth(44); codeEdit1_->setStyleSheet(codeStyle);
    codeEdit2_ = new QLineEdit(); codeEdit2_->setMaxLength(1); codeEdit2_->setAlignment(Qt::AlignCenter); codeEdit2_->setFixedWidth(44); codeEdit2_->setStyleSheet(codeStyle);
    codeEdit3_ = new QLineEdit(); codeEdit3_->setMaxLength(1); codeEdit3_->setAlignment(Qt::AlignCenter); codeEdit3_->setFixedWidth(44); codeEdit3_->setStyleSheet(codeStyle);
    codeEdit4_ = new QLineEdit(); codeEdit4_->setMaxLength(1); codeEdit4_->setAlignment(Qt::AlignCenter); codeEdit4_->setFixedWidth(44); codeEdit4_->setStyleSheet(codeStyle);
    codeEdit5_ = new QLineEdit(); codeEdit5_->setMaxLength(1); codeEdit5_->setAlignment(Qt::AlignCenter); codeEdit5_->setFixedWidth(44); codeEdit5_->setStyleSheet(codeStyle);
    codeEdit6_ = new QLineEdit(); codeEdit6_->setMaxLength(1); codeEdit6_->setAlignment(Qt::AlignCenter); codeEdit6_->setFixedWidth(44); codeEdit6_->setStyleSheet(codeStyle);
    
    // Auto-advance between code fields
    auto connectCodeField = [this](QLineEdit* current, QLineEdit* next) {
        connect(current, &QLineEdit::textChanged, [current, next](const QString& text) {
            if (text.length() == 1 && next) {
                next->setFocus();
            }
        });
    };
    
    codeLayout->addStretch();
    codeLayout->addWidget(codeEdit1_);
    connectCodeField(codeEdit1_, codeEdit2_);
    codeLayout->addWidget(codeEdit2_);
    connectCodeField(codeEdit2_, codeEdit3_);
    codeLayout->addWidget(codeEdit3_);
    connectCodeField(codeEdit3_, codeEdit4_);
    codeLayout->addWidget(codeEdit4_);
    connectCodeField(codeEdit4_, codeEdit5_);
    codeLayout->addWidget(codeEdit5_);
    connectCodeField(codeEdit5_, codeEdit6_);
    codeLayout->addWidget(codeEdit6_);
    codeLayout->addStretch();
    
    step4Layout->addLayout(codeLayout);
    
    attemptsLabel_ = new QLabel();
    attemptsLabel_->setStyleSheet("font-size: 12px; color: #888;");
    attemptsLabel_->setAlignment(Qt::AlignCenter);
    step4Layout->addWidget(attemptsLabel_);
    
    stackedWidget_->addWidget(step4Widget_);
    
    // === Step 5: Password ===
    step5Widget_ = new QWidget();
    auto* step5Layout = new QVBoxLayout(step5Widget_);
    step5Layout->setSpacing(10);
    
    passwordEdit_ = new QLineEdit();
    passwordEdit_->setPlaceholderText(tr("Password"));
    passwordEdit_->setEchoMode(QLineEdit::Password);
    passwordEdit_->setStyleSheet(inputStyle);
    connect(passwordEdit_, &QLineEdit::textChanged, this, &RegisterPage::updatePasswordStrength);
    step5Layout->addWidget(passwordEdit_);
    
    confirmPasswordEdit_ = new QLineEdit();
    confirmPasswordEdit_->setPlaceholderText(tr("Confirm Password"));
    confirmPasswordEdit_->setEchoMode(QLineEdit::Password);
    confirmPasswordEdit_->setStyleSheet(inputStyle);
    step5Layout->addWidget(confirmPasswordEdit_);
    
    // Strength bar
    strengthBar_ = new QProgressBar();
    strengthBar_->setRange(0, 100);
    strengthBar_->setValue(0);
    strengthBar_->setTextVisible(false);
    strengthBar_->setFixedHeight(6);
    strengthBar_->setStyleSheet(R"(
        QProgressBar {
            background: #e0e0e0;
            border: none;
            border-radius: 3px;
        }
        QProgressBar::chunk {
            border-radius: 3px;
        }
    )");
    step5Layout->addWidget(strengthBar_);
    
    strengthLabel_ = new QLabel(tr("Password strength"));
    strengthLabel_->setStyleSheet("font-size: 12px; color: #888;");
    step5Layout->addWidget(strengthLabel_);
    
    // Requirements
    auto* reqLayout = new QGridLayout();
    reqLayout->setSpacing(4);
    
    QString reqStyle = "font-size: 11px;";
    QString reqMetStyle = "font-size: 11px; color: #4caf50;";
    QString reqNotMetStyle = "font-size: 11px; color: #888;";
    
    reqMinLength_ = new QLabel(tr("✓ At least 8 characters"));
    reqMinLength_->setStyleSheet(reqNotMetStyle);
    reqLayout->addWidget(reqMinLength_, 0, 0);
    
    reqUppercase_ = new QLabel(tr("✓ Uppercase letter"));
    reqUppercase_->setStyleSheet(reqNotMetStyle);
    reqLayout->addWidget(reqUppercase_, 0, 1);
    
    reqLowercase_ = new QLabel(tr("✓ Lowercase letter"));
    reqLowercase_->setStyleSheet(reqNotMetStyle);
    reqLayout->addWidget(reqLowercase_, 1, 0);
    
    reqDigit_ = new QLabel(tr("✓ Digit"));
    reqDigit_->setStyleSheet(reqNotMetStyle);
    reqLayout->addWidget(reqDigit_, 1, 1);
    
    reqSpecial_ = new QLabel(tr("✓ Special character"));
    reqSpecial_->setStyleSheet(reqNotMetStyle);
    reqLayout->addWidget(reqSpecial_, 2, 0);
    
    step5Layout->addLayout(reqLayout);
    
    stackedWidget_->addWidget(step5Widget_);
    
    // === Step 6: Avatar ===
    step6Widget_ = new QWidget();
    auto* step6Layout = new QVBoxLayout(step6Widget_);
    step6Layout->setSpacing(12);
    step6Layout->setAlignment(Qt::AlignCenter);
    
    avatarPreview_ = new QLabel();
    avatarPreview_->setFixedSize(120, 120);
    avatarPreview_->setStyleSheet(R"(
        QLabel {
            background: #f0f0f0;
            border: 2px dashed #ccc;
            border-radius: 60px;
        }
    )");
    avatarPreview_->setAlignment(Qt::AlignCenter);
    avatarPreview_->setText(tr("📷"));
    avatarPreview_->setStyleSheet(avatarPreview_->styleSheet() + "font-size: 40px;");
    step6Layout->addWidget(avatarPreview_, 0, Qt::AlignCenter);
    
    selectAvatarBtn_ = new QPushButton(tr("Select Photo"));
    selectAvatarBtn_->setStyleSheet(R"(
        QPushButton {
            padding: 10px 24px;
            background: white;
            color: #3DAEE9;
            border: 1px solid #3DAEE9;
            border-radius: 6px;
            font-size: 13px;
        }
        QPushButton:hover { background: #e3f2fd; }
    )");
    connect(selectAvatarBtn_, &QPushButton::clicked, [this]() {
        QString file = QFileDialog::getOpenFileName(this, tr("Select Photo"), 
            QString(), tr("Images (*.png *.jpg *.jpeg *.gif)"));
        if (!file.isEmpty()) {
            QPixmap pixmap(file);
            if (!pixmap.isNull()) {
                state_.avatarData.clear();
                QBuffer buffer(&state_.avatarData);
                buffer.open(QIODevice::WriteOnly);
                pixmap.scaled(200, 200, Qt::KeepAspectRatioByExpanding, Qt::SmoothTransformation)
                       .save(&buffer, "JPEG", 85);
                
                avatarPreview_->setPixmap(pixmap.scaled(118, 118, Qt::KeepAspectRatioByExpanding, 
                                                         Qt::SmoothTransformation));
                avatarPreview_->setStyleSheet(avatarPreview_->styleSheet().replace("font-size: 40px;", ""));
            }
        }
    });
    step6Layout->addWidget(selectAvatarBtn_, 0, Qt::AlignCenter);
    
    stackedWidget_->addWidget(step6Widget_);
    
    // === Step 7: Bio ===
    step7Widget_ = new QWidget();
    auto* step7Layout = new QVBoxLayout(step7Widget_);
    step7Layout->setSpacing(8);
    
    bioEdit_ = new QTextEdit();
    bioEdit_->setPlaceholderText(tr("Tell us about yourself..."));
    bioEdit_->setMaximumHeight(120);
    bioEdit_->setStyleSheet(R"(
        QTextEdit {
            padding: 12px;
            border: 1px solid #ddd;
            border-radius: 6px;
            font-size: 14px;
            background: white;
        }
        QTextEdit:focus {
            border-color: #3DAEE9;
        }
    )");
    connect(bioEdit_, &QTextEdit::textChanged, this, &RegisterPage::updateBioCharCount);
    step7Layout->addWidget(bioEdit_);
    
    bioCharCount_ = new QLabel(tr("0 / 200"));
    bioCharCount_->setStyleSheet("font-size: 11px; color: #888;");
    bioCharCount_->setAlignment(Qt::AlignRight);
    step7Layout->addWidget(bioCharCount_);
    
    stackedWidget_->addWidget(step7Widget_);
    
    // === Step 8: 2FA ===
    step8Widget_ = new QWidget();
    auto* step8Layout = new QVBoxLayout(step8Widget_);
    step8Layout->setSpacing(12);
    step8Layout->setAlignment(Qt::AlignCenter);
    
    // 2FA enable prompt (shown first)
    auto* twoFATitle = new QLabel(tr("Two-Factor Authentication"));
    twoFATitle->setStyleSheet("font-size: 16px; font-weight: bold; color: #333;");
    twoFATitle->setAlignment(Qt::AlignCenter);
    step8Layout->addWidget(twoFATitle);
    
    auto* twoFADesc = new QLabel(tr("Add an extra layer of security to your account\nby enabling two-factor authentication."));
    twoFADesc->setStyleSheet("font-size: 13px; color: #666;");
    twoFADesc->setAlignment(Qt::AlignCenter);
    step8Layout->addWidget(twoFADesc);
    
    // Enable button (replaces nextButton for this step)
    enable2FABtn_ = new QPushButton(tr("Enable 2FA"));
    enable2FABtn_->setStyleSheet(R"(
        QPushButton {
            padding: 12px 32px;
            background: #4caf50;
            color: white;
            border: none;
            border-radius: 6px;
            font-size: 14px;
            font-weight: bold;
        }
        QPushButton:hover { background: #43a047; }
        QPushButton:disabled { background: #ccc; }
    )");
    connect(enable2FABtn_, &QPushButton::clicked, this, &RegisterPage::loadQrCode);
    step8Layout->addWidget(enable2FABtn_, 0, Qt::AlignCenter);
    
    // QR code section (hidden initially)
    qrCodeLabel_ = new QLabel();
    qrCodeLabel_->setFixedSize(180, 180);
    qrCodeLabel_->setStyleSheet("background: white; border: 1px solid #ddd;");
    qrCodeLabel_->setAlignment(Qt::AlignCenter);
    qrCodeLabel_->hide();
    step8Layout->addWidget(qrCodeLabel_, 0, Qt::AlignCenter);
    
    otpCodeLabel_ = new QLabel();
    otpCodeLabel_->setStyleSheet("font-size: 12px; color: #888; font-family: monospace;");
    otpCodeLabel_->setAlignment(Qt::AlignCenter);
    otpCodeLabel_->setTextInteractionFlags(Qt::TextSelectableByMouse);
    otpCodeLabel_->hide();
    step8Layout->addWidget(otpCodeLabel_);
    
    auto* otpConfirmLabel = new QLabel(tr("Enter the 6-digit code from the app:"));
    otpConfirmLabel->setStyleSheet("font-size: 12px; color: #666;");
    otpConfirmLabel->setAlignment(Qt::AlignCenter);
    otpConfirmLabel->hide();
    step8Layout->addWidget(otpConfirmLabel);
    otpConfirmLabelFor2FA_ = otpConfirmLabel;  // Save reference
    
    otpConfirmEdit_ = new QLineEdit();
    otpConfirmEdit_->setMaxLength(6);
    otpConfirmEdit_->setAlignment(Qt::AlignCenter);
    otpConfirmEdit_->setFixedWidth(150);
    otpConfirmEdit_->setStyleSheet(inputStyle);
    otpConfirmEdit_->hide();
    step8Layout->addWidget(otpConfirmEdit_, 0, Qt::AlignCenter);
    
    stackedWidget_->addWidget(step8Widget_);
    
    // === Step 9: Completion ===
    step9Widget_ = new QWidget();
    auto* step9Layout = new QVBoxLayout(step9Widget_);
    step9Layout->setSpacing(16);
    step9Layout->setAlignment(Qt::AlignCenter);
    
    auto* successIcon = new QLabel("✅");
    successIcon->setStyleSheet("font-size: 64px;");
    successIcon->setAlignment(Qt::AlignCenter);
    step9Layout->addWidget(successIcon);
    
    completionMessage_ = new QLabel(tr("Registration Complete!\n\nYour account has been created successfully."));
    completionMessage_->setStyleSheet("font-size: 16px; color: #333;");
    completionMessage_->setAlignment(Qt::AlignCenter);
    completionMessage_->setWordWrap(true);
    step9Layout->addWidget(completionMessage_);
    
    stackedWidget_->addWidget(step9Widget_);
}

void RegisterPage::updateProgress() {
    stepIndicatorLabel_->setText(state_.stepIndicator());
    progressBar_->setValue(static_cast<int>(state_.progressPercentage()));
    titleLabel_->setText(stepTitle(state_.currentStep));
}

void RegisterPage::showCurrentStep() {
    clearError();
    updateProgress();
    
    QWidget* targetWidget = nullptr;
    
    switch (state_.currentStep) {
        case RegistrationStep::PersonalInfo:
            targetWidget = step1Widget_;
            firstNameEdit_->setText(state_.firstName);
            lastNameEdit_->setText(state_.lastName);
            break;
        case RegistrationStep::Username:
            targetWidget = step2Widget_;
            usernameEdit_->setText(state_.username);
            usernameStatus_->clear();
            break;
        case RegistrationStep::Email:
            targetWidget = step3Widget_;
            emailEdit_->setText(state_.email);
            break;
        case RegistrationStep::ConfirmEmail:
            targetWidget = step4Widget_;
            codeEdit1_->clear(); codeEdit2_->clear(); codeEdit3_->clear();
            codeEdit4_->clear(); codeEdit5_->clear(); codeEdit6_->clear();
            codeEdit1_->setFocus();
            if (state_.verificationAttempts > 0) {
                attemptsLabel_->setText(tr("Attempts remaining: %1").arg(state_.remainingAttempts()));
            } else {
                attemptsLabel_->clear();
            }
            break;
        case RegistrationStep::Password:
            targetWidget = step5Widget_;
            passwordEdit_->clear();
            confirmPasswordEdit_->clear();
            updatePasswordStrength();
            break;
        case RegistrationStep::Avatar:
            targetWidget = step6Widget_;
            break;
        case RegistrationStep::Bio:
            targetWidget = step7Widget_;
            bioEdit_->setPlainText(state_.bio);
            updateBioCharCount();
            break;
        case RegistrationStep::TwoFA:
            targetWidget = step8Widget_;
            otpConfirmEdit_->clear();
            // Reset to initial state (show enable button, hide QR)
            enable2FABtn_->show();
            qrCodeLabel_->hide();
            otpCodeLabel_->hide();
            otpConfirmLabelFor2FA_->hide();
            otpConfirmEdit_->hide();
            break;
        case RegistrationStep::Completion:
            targetWidget = step9Widget_;
            nextButton_->setText(tr("Get Started"));
            break;
    }
    
    if (targetWidget) {
        stackedWidget_->setCurrentWidget(targetWidget);
    }
    
    // Update navigation buttons
    backButton_->setVisible(state_.currentStep != RegistrationStep::PersonalInfo);
    skipButton_->setVisible(isStepSkippable(state_.currentStep));
    
    if (state_.currentStep == RegistrationStep::Completion) {
        nextButton_->setText(tr("Get Started"));
    } else {
        nextButton_->setText(tr("Continue"));
    }
}

void RegisterPage::clearError() {
    errorLabel_->hide();
    errorLabel_->clear();
    state_.errorMessage.clear();
}

void RegisterPage::showError(const QString& message) {
    state_.errorMessage = message;
    errorLabel_->setText(message);
    errorLabel_->show();
}

void RegisterPage::setControlsEnabled(bool enabled) {
    nextButton_->setEnabled(enabled);
    backButton_->setEnabled(enabled);
    skipButton_->setEnabled(enabled);
    state_.isLoading = !enabled;
}

void RegisterPage::onNextClicked() {
    if (state_.isLoading) return;
    
    switch (state_.currentStep) {
        case RegistrationStep::PersonalInfo: processStep1PersonalInfo(); break;
        case RegistrationStep::Username: processStep2Username(); break;
        case RegistrationStep::Email: processStep3Email(); break;
        case RegistrationStep::ConfirmEmail: processStep4ConfirmEmail(); break;
        case RegistrationStep::Password: processStep5Password(); break;
        case RegistrationStep::Avatar: processStep6Avatar(); break;
        case RegistrationStep::Bio: processStep7Bio(); break;
        case RegistrationStep::TwoFA: processStep8TwoFA(); break;
        case RegistrationStep::Completion: processStep9Completion(); break;
    }
}

void RegisterPage::onBackClicked() {
    if (state_.isLoading) return;
    clearError();
    state_.currentStep = previousStep(state_.currentStep);
    showCurrentStep();
}

void RegisterPage::onSkipClicked() {
    if (state_.isLoading) return;
    clearError();
    state_.currentStep = nextStep(state_.currentStep);
    showCurrentStep();
}

//=== Step Processing ===

void RegisterPage::processStep1PersonalInfo() {
    QString firstName = firstNameEdit_->text().trimmed();
    QString lastName = lastNameEdit_->text().trimmed();
    
    auto fnResult = PersonalInfoValidator::validateFirstName(firstName);
    if (!fnResult.isValid) {
        showError(fnResult.errorMessage);
        return;
    }
    
    auto lnResult = PersonalInfoValidator::validateLastName(lastName);
    if (!lnResult.isValid) {
        showError(lnResult.errorMessage);
        return;
    }
    
    state_.firstName = firstName;
    state_.lastName = lastName;
    clearError();
    state_.currentStep = nextStep(state_.currentStep);
    showCurrentStep();
}

void RegisterPage::onUsernameTextChanged() {
    QString username = usernameEdit_->text().trimmed();
    
    // Reset status when text changes
    usernameStatus_->clear();
    clearError();
    
    // Validate format first
    auto result = UsernameValidator::validate(username);
    if (!result.isValid) {
        usernameStatus_->setText(result.errorMessage);
        usernameStatus_->setStyleSheet("font-size: 12px; color: #d32f2f;");
        usernameDebounceTimer_->stop();
        return;
    }
    
    // Start debounce timer for availability check
    usernameStatus_->setText(tr("Checking..."));
    usernameStatus_->setStyleSheet("font-size: 12px; color: #888;");
    usernameDebounceTimer_->start();
}

void RegisterPage::checkUsernameAvailability() {
    QString username = usernameEdit_->text().trimmed();
    if (username.isEmpty()) return;
    
    QtConcurrent::run([this, username]() {
        try {
            bool exists = usersClient_->checkExistUsername(UsernameValidator::normalize(username));
            
            QMetaObject::invokeMethod(this, [this, exists]() {
                if (exists) {
                    usernameStatus_->setText(tr("❌ This username is already taken"));
                    usernameStatus_->setStyleSheet("font-size: 12px; color: #d32f2f;");
                } else {
                    usernameStatus_->setText(tr("✓ Username is available"));
                    usernameStatus_->setStyleSheet("font-size: 12px; color: #4caf50;");
                }
            });
        } catch (const std::exception& e) {
            QMetaObject::invokeMethod(this, [this, msg = QString::fromStdString(e.what())]() {
                usernameStatus_->setText(tr("Error checking"));
                usernameStatus_->setStyleSheet("font-size: 12px; color: #d32f2f;");
            });
        }
    });
}

void RegisterPage::processStep2Username() {
    QString username = usernameEdit_->text().trimmed();
    
    auto result = UsernameValidator::validate(username);
    if (!result.isValid) {
        usernameStatus_->setText(result.errorMessage);
        usernameStatus_->setStyleSheet("font-size: 12px; color: #d32f2f;");
        return;
    }
    
    // Check if already verified as available
    if (usernameStatus_->text().contains("available")) {
        state_.username = username;
        state_.currentStep = nextStep(state_.currentStep);
        showCurrentStep();
        return;
    }
    
    // Need to check availability
    setControlsEnabled(false);
    usernameStatus_->setText(tr("Checking availability..."));
    usernameStatus_->setStyleSheet("font-size: 12px; color: #888;");
    
    QtConcurrent::run([this, username]() {
        try {
            bool exists = usersClient_->checkExistUsername(UsernameValidator::normalize(username));
            
            QMetaObject::invokeMethod(this, [this, username, exists]() {
                setControlsEnabled(true);
                
                if (exists) {
                    usernameStatus_->setText(tr("❌ This username is already taken"));
                    usernameStatus_->setStyleSheet("font-size: 12px; color: #d32f2f;");
                } else {
                    usernameStatus_->setText(tr("✓ Username is available"));
                    usernameStatus_->setStyleSheet("font-size: 12px; color: #4caf50;");
                    state_.username = username;
                    state_.currentStep = nextStep(state_.currentStep);
                    showCurrentStep();
                }
            });
        } catch (const std::exception& e) {
            QMetaObject::invokeMethod(this, [this, msg = QString::fromStdString(e.what())]() {
                setControlsEnabled(true);
                usernameStatus_->setText(tr("Error checking username"));
                usernameStatus_->setStyleSheet("font-size: 12px; color: #d32f2f;");
            });
        }
    });
}

void RegisterPage::onEmailTextChanged() {
    QString email = emailEdit_->text().trimmed();
    
    // Reset status when text changes
    emailStatus_->setText(tr("A confirmation code will be sent to this email"));
    emailStatus_->setStyleSheet("font-size: 12px; color: #888;");
    clearError();
    
    // Validate format first
    auto result = EmailValidator::validate(email);
    if (!result.isValid) {
        emailStatus_->setText(result.errorMessage);
        emailStatus_->setStyleSheet("font-size: 12px; color: #d32f2f;");
        emailDebounceTimer_->stop();
        return;
    }
    
    // Start debounce timer for availability check
    emailStatus_->setText(tr("Checking..."));
    emailDebounceTimer_->start();
}

void RegisterPage::checkEmailAvailability() {
    QString email = emailEdit_->text().trimmed();
    if (email.isEmpty()) return;
    
    QtConcurrent::run([this, email]() {
        try {
            bool exists = usersClient_->checkExistEmail(EmailValidator::normalize(email));
            
            QMetaObject::invokeMethod(this, [this, exists]() {
                if (exists) {
                    emailStatus_->setText(tr("❌ This email is already registered"));
                    emailStatus_->setStyleSheet("font-size: 12px; color: #d32f2f;");
                } else {
                    emailStatus_->setText(tr("✓ Email is available"));
                    emailStatus_->setStyleSheet("font-size: 12px; color: #4caf50;");
                }
            });
        } catch (const std::exception& e) {
            QMetaObject::invokeMethod(this, [this]() {
                emailStatus_->setText(tr("Error checking"));
                emailStatus_->setStyleSheet("font-size: 12px; color: #d32f2f;");
            });
        }
    });
}

void RegisterPage::processStep3Email() {
    QString email = emailEdit_->text().trimmed();
    
    auto result = EmailValidator::validate(email);
    if (!result.isValid) {
        emailStatus_->setText(result.errorMessage);
        emailStatus_->setStyleSheet("font-size: 12px; color: #d32f2f;");
        return;
    }
    
    // Check if already verified as available
    if (emailStatus_->text().contains("available")) {
        setControlsEnabled(false);
        emailStatus_->setText(tr("Creating account..."));
    } else {
        setControlsEnabled(false);
        emailStatus_->setText(tr("Checking availability..."));
    }
    
    QString normalizedEmail = EmailValidator::normalize(email);
    QString normalizedUsername = UsernameValidator::normalize(state_.username);
    
    QtConcurrent::run([this, normalizedEmail, normalizedUsername]() {
        try {
            bool exists = usersClient_->checkExistEmail(normalizedEmail);
            
            if (exists) {
                QMetaObject::invokeMethod(this, [this]() {
                    setControlsEnabled(true);
                    emailStatus_->setText(tr("❌ This email is already registered"));
                    showError(tr("Email is already registered"));
                });
                return;
            }
            
            // Create account
            auto accountResult = identityClient_->createAccount(
                state_.firstName, state_.lastName, normalizedUsername, normalizedEmail);
            
            QMetaObject::invokeMethod(this, [this, accountResult, normalizedEmail]() {
                setControlsEnabled(true);
                state_.email = normalizedEmail;
                state_.codeId = accountResult.codeId;
                emailStatus_->setText(tr("✓ Confirmation code sent"));
                clearError();
                state_.currentStep = nextStep(state_.currentStep);
                showCurrentStep();
            });
        } catch (const std::exception& e) {
            QMetaObject::invokeMethod(this, [this, msg = QString::fromStdString(e.what())]() {
                setControlsEnabled(true);
                emailStatus_->setText(tr("Error"));
                showError(msg);
            });
        }
    });
}

void RegisterPage::processStep4ConfirmEmail() {
    QString code = getOtpCode();
    
    auto result = VerificationCodeValidator::validate(code);
    if (!result.isValid) {
        showError(result.errorMessage);
        return;
    }
    
    // Rate limiting check
    if (!state_.canAttemptVerification()) {
        int remaining = state_.cooldownRemainingSeconds();
        showError(tr("Too many attempts. Please wait %1 minutes.").arg(remaining / 60 + 1));
        return;
    }
    
    setControlsEnabled(false);
    
    QtConcurrent::run([this, code]() {
        try {
            auto confirmResult = identityClient_->confirmAccount(state_.codeId, code);
            
            // Get access token from refresh token
            Token accessToken = identityClient_->createToken(confirmResult.refreshToken.value);
            
            QMetaObject::invokeMethod(this, [this, confirmResult, accessToken]() {
                setControlsEnabled(true);
                state_.recordVerificationAttempt(true);
                state_.refreshToken = confirmResult.refreshToken.value;
                state_.accessToken = accessToken.value;
                clearError();
                state_.currentStep = nextStep(state_.currentStep);
                showCurrentStep();
            });
        } catch (const std::exception& e) {
            QMetaObject::invokeMethod(this, [this, msg = QString::fromStdString(e.what())]() {
                setControlsEnabled(true);
                state_.recordVerificationAttempt(false);
                
                if (state_.remainingAttempts() > 0) {
                    attemptsLabel_->setText(tr("Attempts remaining: %1").arg(state_.remainingAttempts()));
                } else {
                    int cooldown = state_.cooldownRemainingSeconds();
                    attemptsLabel_->setText(tr("Wait %1 minutes before retrying").arg(cooldown / 60 + 1));
                }
                
                showError(msg);
            });
        }
    });
}

void RegisterPage::processStep5Password() {
    QString password = passwordEdit_->text();
    QString confirmPassword = confirmPasswordEdit_->text();
    
    auto result = PasswordValidator::validate(password);
    if (!result.isValid) {
        showError(result.errorMessage);
        return;
    }
    
    auto matchResult = PasswordValidator::validateMatch(password, confirmPassword);
    if (!matchResult.isValid) {
        showError(matchResult.errorMessage);
        return;
    }
    
    setControlsEnabled(false);
    
    QtConcurrent::run([this, password]() {
        try {
            identityClient_->setPassword(password, state_.accessToken);
            
            QMetaObject::invokeMethod(this, [this]() {
                setControlsEnabled(true);
                state_.password.clear();  // Clear for security
                clearError();
                state_.currentStep = nextStep(state_.currentStep);
                showCurrentStep();
            });
        } catch (const std::exception& e) {
            QMetaObject::invokeMethod(this, [this, msg = QString::fromStdString(e.what())]() {
                setControlsEnabled(true);
                showError(msg);
            });
        }
    });
}

void RegisterPage::processStep6Avatar() {
    if (state_.avatarData.isEmpty()) {
        // No avatar selected, skip
        state_.currentStep = nextStep(state_.currentStep);
        showCurrentStep();
        return;
    }
    
    setControlsEnabled(false);
    
    QtConcurrent::run([this]() {
        try {
            QString fileId = filesClient_->uploadFile(
                UploadFileType::UserAvatar, state_.avatarData, "avatar.jpg", state_.accessToken);
            
            usersClient_->setProfilePicture(fileId, state_.accessToken);
            
            QMetaObject::invokeMethod(this, [this, fileId]() {
                setControlsEnabled(true);
                state_.avatarFileId = fileId;
                clearError();
                state_.currentStep = nextStep(state_.currentStep);
                showCurrentStep();
            });
        } catch (const std::exception& e) {
            QMetaObject::invokeMethod(this, [this, msg = QString::fromStdString(e.what())]() {
                setControlsEnabled(true);
                showError(msg);
            });
        }
    });
}

void RegisterPage::processStep7Bio() {
    QString bio = bioEdit_->toPlainText().trimmed();
    
    if (bio.isEmpty()) {
        state_.currentStep = nextStep(state_.currentStep);
        showCurrentStep();
        return;
    }
    
    if (bio.length() > 200) {
        showError(tr("Bio must be 200 characters or less"));
        return;
    }
    
    setControlsEnabled(false);
    
    QtConcurrent::run([this, bio]() {
        try {
            usersClient_->changeBio(bio, state_.accessToken);
            
            QMetaObject::invokeMethod(this, [this, bio]() {
                setControlsEnabled(true);
                state_.bio = bio;
                clearError();
                state_.currentStep = nextStep(state_.currentStep);
                showCurrentStep();
            });
        } catch (const std::exception& e) {
            QMetaObject::invokeMethod(this, [this, msg = QString::fromStdString(e.what())]() {
                setControlsEnabled(true);
                showError(msg);
            });
        }
    });
}

void RegisterPage::processStep8TwoFA() {
    QString otpCode = otpConfirmEdit_->text().trimmed();
    
    if (state_.otpQrCode.isEmpty()) {
        // First time - get QR code
        setControlsEnabled(false);
        
        QtConcurrent::run([this]() {
            try {
                auto otpInfo = identityClient_->enableOTP(state_.accessToken);
                
                QMetaObject::invokeMethod(this, [this, otpInfo]() {
                    setControlsEnabled(true);
                    state_.otpQrCode = otpInfo.qrCodeBase64;
                    state_.otpSecretCode = otpInfo.otpCode;
                    
                    // Display QR code
                    QByteArray imageData = QByteArray::fromBase64(state_.otpQrCode.toUtf8());
                    QPixmap pixmap;
                    pixmap.loadFromData(imageData);
                    qrCodeLabel_->setPixmap(pixmap.scaled(178, 178, Qt::KeepAspectRatio, Qt::SmoothTransformation));
                    
                    otpCodeLabel_->setText(tr("Or enter code manually: %1").arg(state_.otpSecretCode));
                });
            } catch (const std::exception& e) {
                QMetaObject::invokeMethod(this, [this, msg = QString::fromStdString(e.what())]() {
                    setControlsEnabled(true);
                    showError(msg);
                });
            }
        });
        return;
    }
    
    // Validate OTP code
    if (otpCode.length() != 6) {
        showError(tr("Please enter a 6-digit code"));
        return;
    }
    
    setControlsEnabled(false);
    
    QtConcurrent::run([this, otpCode]() {
        try {
            identityClient_->confirmOTP(otpCode, state_.accessToken);
            
            QMetaObject::invokeMethod(this, [this]() {
                setControlsEnabled(true);
                clearError();
                state_.currentStep = nextStep(state_.currentStep);
                showCurrentStep();
            });
        } catch (const std::exception& e) {
            QMetaObject::invokeMethod(this, [this, msg = QString::fromStdString(e.what())]() {
                setControlsEnabled(true);
                showError(msg);
            });
        }
    });
}

void RegisterPage::processStep9Completion() {
    UserSession session;
    session.refreshToken.value = state_.refreshToken;
    session.accessToken.value = state_.accessToken;
    
    // Extract userId and username from JWT token
    session.userId = JwtUtils::extractUserId(state_.accessToken);
    session.username = JwtUtils::extractUsername(state_.accessToken);
    
    emit registerSuccess(session);
}

//=== Helper Methods ===

QString RegisterPage::getOtpCode() const {
    return codeEdit1_->text() + codeEdit2_->text() + codeEdit3_->text() +
           codeEdit4_->text() + codeEdit5_->text() + codeEdit6_->text();
}

void RegisterPage::updatePasswordStrength() {
    QString password = passwordEdit_->text();
    auto reqs = PasswordValidator::getRequirements(password);
    auto level = PasswordValidator::getStrengthLevel(reqs.strengthScore);
    
    strengthBar_->setValue(reqs.strengthScore);
    strengthBar_->setStyleSheet(QString(R"(
        QProgressBar {
            background: #e0e0e0;
            border: none;
            border-radius: 3px;
        }
        QProgressBar::chunk {
            background: %1;
            border-radius: 3px;
        }
    )").arg(level.color.name()));
    
    strengthLabel_->setText(level.message);
    strengthLabel_->setStyleSheet(QString("font-size: 12px; color: %1;").arg(level.color.name()));
    
    // Update requirements
    auto updateReq = [](QLabel* label, bool met) {
        label->setText((met ? "✓ " : "○ ") + label->text().mid(2));
        label->setStyleSheet(met ? "font-size: 11px; color: #4caf50;" : "font-size: 11px; color: #888;");
    };
    
    updateReq(reqMinLength_, reqs.hasMinLength);
    updateReq(reqUppercase_, reqs.hasUpperCase);
    updateReq(reqLowercase_, reqs.hasLowerCase);
    updateReq(reqDigit_, reqs.hasDigit);
    updateReq(reqSpecial_, reqs.hasSpecialChar);
}

void RegisterPage::updateBioCharCount() {
    int count = bioEdit_->toPlainText().length();
    bioCharCount_->setText(tr("%1 / 200").arg(count));
    
    if (count > 200) {
        bioCharCount_->setStyleSheet("font-size: 11px; color: #d32f2f;");
    } else {
        bioCharCount_->setStyleSheet("font-size: 11px; color: #888;");
    }
}

void RegisterPage::loadQrCode() {
    // Hide enable button, show QR code section
    enable2FABtn_->hide();
    qrCodeLabel_->show();
    qrCodeLabel_->setText(tr("Loading..."));
    setControlsEnabled(false);
    
    QtConcurrent::run([this]() {
        try {
            auto otpInfo = identityClient_->enableOTP(state_.accessToken);
            
            QMetaObject::invokeMethod(this, [this, otpInfo]() {
                setControlsEnabled(true);
                state_.otpQrCode = otpInfo.qrCodeBase64;
                state_.otpSecretCode = otpInfo.otpCode;
                
                // Display QR code
                QByteArray imageData = QByteArray::fromBase64(state_.otpQrCode.toUtf8());
                QPixmap pixmap;
                if (pixmap.loadFromData(imageData)) {
                    qrCodeLabel_->setPixmap(pixmap.scaled(178, 178, Qt::KeepAspectRatio, Qt::SmoothTransformation));
                } else {
                    qrCodeLabel_->setText(tr("Failed to load QR code"));
                }
                
                // Show OTP code and confirmation field
                otpCodeLabel_->setText(tr("Or enter code manually: %1").arg(state_.otpSecretCode));
                otpCodeLabel_->show();
                otpConfirmLabelFor2FA_->show();
                otpConfirmEdit_->show();
                otpConfirmEdit_->setFocus();
            });
        } catch (const std::exception& e) {
            QMetaObject::invokeMethod(this, [this, msg = QString::fromStdString(e.what())]() {
                setControlsEnabled(true);
                qrCodeLabel_->setText(tr("Error loading QR"));
                showError(msg);
            });
        }
    });
}

} // namespace BarkFluff
