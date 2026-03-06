#pragma once

#include <QWidget>
#include <QLineEdit>
#include <QPushButton>
#include <QLabel>
#include <QStackedWidget>
#include <QProgressBar>
#include <QTextEdit>
#include <QFileDialog>
#include <QVBoxLayout>
#include <QTimer>
#include "Models/ServerConfiguration.h"
#include "Models/UserSession.h"
#include "Models/RegistrationState.h"
#include "Connection/IdentityClient.h"
#include "Connection/UsersClient.h"
#include "Connection/FilesClient.h"

namespace BarkFluff {

class RegisterPage : public QWidget {
    Q_OBJECT
    
public:
    explicit RegisterPage(const ServerConfiguration& serverConfig, QWidget* parent = nullptr);
    
signals:
    void registerSuccess(const UserSession& session);
    void backToLogin();
    
private slots:
    void onNextClicked();
    void onBackClicked();
    void onSkipClicked();
    void onUsernameTextChanged();
    void onEmailTextChanged();
    void checkUsernameAvailability();
    void checkEmailAvailability();
    
private:
    void setupUi();
    void setupStepWidgets();
    void updateProgress();
    void showCurrentStep();
    void clearError();
    void showError(const QString& message);
    void setControlsEnabled(bool enabled);
    
    // Step processing
    void processStep1PersonalInfo();
    void processStep2Username();
    void processStep3Email();
    void processStep4ConfirmEmail();
    void processStep5Password();
    void processStep6Avatar();
    void processStep7Bio();
    void processStep8TwoFA();
    void processStep9Completion();
    
    // Server configuration
    ServerConfiguration serverConfig_;
    
    // Clients
    IdentityClient* identityClient_ = nullptr;
    UsersClient* usersClient_ = nullptr;
    FilesClient* filesClient_ = nullptr;
    
    // State
    RegistrationState state_;
    
    // Main layout
    QVBoxLayout* mainLayout_ = nullptr;
    
    // Header
    QLabel* serverLabel_ = nullptr;
    QLabel* titleLabel_ = nullptr;
    QLabel* stepIndicatorLabel_ = nullptr;
    QProgressBar* progressBar_ = nullptr;
    
    // Stacked widget for steps
    QStackedWidget* stackedWidget_ = nullptr;
    
    // Error display
    QLabel* errorLabel_ = nullptr;
    
    // Navigation buttons
    QPushButton* backButton_ = nullptr;
    QPushButton* nextButton_ = nullptr;
    QPushButton* skipButton_ = nullptr;
    
    // === Step 1: Personal Info ===
    QWidget* step1Widget_ = nullptr;
    QLineEdit* firstNameEdit_ = nullptr;
    QLineEdit* lastNameEdit_ = nullptr;
    
    // === Step 2: Username ===
    QWidget* step2Widget_ = nullptr;
    QLineEdit* usernameEdit_ = nullptr;
    QLabel* usernameStatus_ = nullptr;
    
    // === Step 3: Email ===
    QWidget* step3Widget_ = nullptr;
    QLineEdit* emailEdit_ = nullptr;
    QLabel* emailStatus_ = nullptr;
    
    // === Step 4: Confirm Email ===
    QWidget* step4Widget_ = nullptr;
    QLineEdit* codeEdit1_ = nullptr;
    QLineEdit* codeEdit2_ = nullptr;
    QLineEdit* codeEdit3_ = nullptr;
    QLineEdit* codeEdit4_ = nullptr;
    QLineEdit* codeEdit5_ = nullptr;
    QLineEdit* codeEdit6_ = nullptr;
    QLabel* attemptsLabel_ = nullptr;
    
    // === Step 5: Password ===
    QWidget* step5Widget_ = nullptr;
    QLineEdit* passwordEdit_ = nullptr;
    QLineEdit* confirmPasswordEdit_ = nullptr;
    QProgressBar* strengthBar_ = nullptr;
    QLabel* strengthLabel_ = nullptr;
    QLabel* reqMinLength_ = nullptr;
    QLabel* reqUppercase_ = nullptr;
    QLabel* reqLowercase_ = nullptr;
    QLabel* reqDigit_ = nullptr;
    QLabel* reqSpecial_ = nullptr;
    
    // === Step 6: Avatar ===
    QWidget* step6Widget_ = nullptr;
    QLabel* avatarPreview_ = nullptr;
    QPushButton* selectAvatarBtn_ = nullptr;
    
    // === Step 7: Bio ===
    QWidget* step7Widget_ = nullptr;
    QTextEdit* bioEdit_ = nullptr;
    QLabel* bioCharCount_ = nullptr;
    
    // === Step 8: 2FA ===
    QWidget* step8Widget_ = nullptr;
    QPushButton* enable2FABtn_ = nullptr;
    QLabel* qrCodeLabel_ = nullptr;
    QLabel* otpCodeLabel_ = nullptr;
    QLabel* otpConfirmLabelFor2FA_ = nullptr;
    QLineEdit* otpConfirmEdit_ = nullptr;
    
    // === Step 9: Completion ===
    QWidget* step9Widget_ = nullptr;
    QLabel* completionMessage_ = nullptr;
    
    // Debounce timers
    QTimer* usernameDebounceTimer_ = nullptr;
    QTimer* emailDebounceTimer_ = nullptr;
    
    // Helper methods
    QString getOtpCode() const;
    void updatePasswordStrength();
    void updateBioCharCount();
    void loadQrCode();
};

} // namespace BarkFluff
