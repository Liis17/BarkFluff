#include "ProfilePage.h"
#include <QVBoxLayout>
#include <QHBoxLayout>
#include <QFormLayout>
#include <QFileDialog>
#include <QMessageBox>
#include <QScrollArea>
#include <QProgressBar>
#include <QtConcurrent>
#include <QDebug>

namespace BarkFluff {

ProfilePage::ProfilePage(
    const ServiceInfo& usersConfig,
    const QString& accessToken,
    qint64 userId,
    QWidget* parent
)
    : QWidget(parent)
    , accessToken_(accessToken)
    , userId_(userId)
{
    usersClient_ = std::make_unique<UsersClient>(usersConfig);
    
    usernameDebounceTimer_ = new QTimer(this);
    usernameDebounceTimer_->setSingleShot(true);
    usernameDebounceTimer_->setInterval(500);
    connect(usernameDebounceTimer_, &QTimer::timeout, this, &ProfilePage::validateUsernameDebounced);
    
    usernameCheckWatcher_ = new QFutureWatcher<bool>(this);
    connect(usernameCheckWatcher_, &QFutureWatcher<bool>::finished, this, &ProfilePage::onUsernameValidationFinished);
    
    setupUI();
    loadProfile();
}

ProfilePage::~ProfilePage() = default;

void ProfilePage::setupUI() {
    auto* mainLayout = new QVBoxLayout(this);
    mainLayout->setContentsMargins(0, 0, 0, 0);
    mainLayout->setSpacing(0);
    
    // Header with back button
    auto* headerWidget = new QWidget;
    headerWidget->setStyleSheet("QWidget { background-color: #f5f5f5; border-bottom: 1px solid #e0e0e0; }");
    auto* headerLayout = new QHBoxLayout(headerWidget);
    headerLayout->setContentsMargins(12, 8, 12, 8);
    headerLayout->setSpacing(8);
    
    auto* backButton = new QPushButton("←");
    backButton->setFixedSize(36, 36);
    backButton->setStyleSheet(
        "QPushButton { font-size: 18px; border: none; background: transparent; }"
        "QPushButton:hover { background: #e0e0e0; border-radius: 18px; }"
    );
    connect(backButton, &QPushButton::clicked, this, &ProfilePage::backRequested);
    headerLayout->addWidget(backButton);
    
    auto* titleLabel = new QLabel(tr("Профиль"));
    titleLabel->setStyleSheet("font-size: 18px; font-weight: bold;");
    headerLayout->addWidget(titleLabel, 1);
    
    mainLayout->addWidget(headerWidget);
    
    // Scroll area for content
    auto* scrollArea = new QScrollArea;
    scrollArea->setWidgetResizable(true);
    scrollArea->setStyleSheet("QScrollArea { border: none; background: white; }");
    
    auto* contentWidget = new QWidget;
    auto* contentLayout = new QVBoxLayout(contentWidget);
    contentLayout->setContentsMargins(24, 24, 24, 24);
    contentLayout->setSpacing(20);
    
    // Avatar section
    auto* avatarSection = new QWidget;
    auto* avatarLayout = new QVBoxLayout(avatarSection);
    avatarLayout->setSpacing(12);
    avatarLayout->setAlignment(Qt::AlignCenter);
    
    avatarWidget_ = new AvatarWidget("", "", 100);
    connect(avatarWidget_, &AvatarWidget::clicked, this, &ProfilePage::onAvatarClicked);
    avatarLayout->addWidget(avatarWidget_, 0, Qt::AlignCenter);
    
    auto* changeAvatarLabel = new QLabel(tr("Нажмите для смены аватара"));
    changeAvatarLabel->setStyleSheet("color: #666; font-size: 12px;");
    changeAvatarLabel->setAlignment(Qt::AlignCenter);
    avatarLayout->addWidget(changeAvatarLabel);
    
    contentLayout->addWidget(avatarSection);
    
    // Form section
    auto* formWidget = new QWidget;
    auto* formLayout = new QFormLayout(formWidget);
    formLayout->setSpacing(16);
    formLayout->setFormAlignment(Qt::AlignLeft);
    
    // First name
    firstNameEdit_ = new QLineEdit;
    firstNameEdit_->setPlaceholderText(tr("Имя"));
    firstNameEdit_->setStyleSheet("QLineEdit { padding: 8px 12px; border: 1px solid #e0e0e0; border-radius: 6px; }");
    connect(firstNameEdit_, &QLineEdit::textChanged, this, &ProfilePage::onFirstNameChanged);
    formLayout->addRow(tr("Имя:"), firstNameEdit_);
    
    // Last name
    lastNameEdit_ = new QLineEdit;
    lastNameEdit_->setPlaceholderText(tr("Фамилия"));
    lastNameEdit_->setStyleSheet("QLineEdit { padding: 8px 12px; border: 1px solid #e0e0e0; border-radius: 6px; }");
    connect(lastNameEdit_, &QLineEdit::textChanged, this, &ProfilePage::onLastNameChanged);
    formLayout->addRow(tr("Фамилия:"), lastNameEdit_);
    
    // Username with status
    auto* usernameContainer = new QWidget;
    auto* usernameLayout = new QVBoxLayout(usernameContainer);
    usernameLayout->setContentsMargins(0, 0, 0, 0);
    usernameLayout->setSpacing(4);
    
    usernameEdit_ = new QLineEdit;
    usernameEdit_->setPlaceholderText("username");
    usernameEdit_->setStyleSheet("QLineEdit { padding: 8px 12px; border: 1px solid #e0e0e0; border-radius: 6px; }");
    connect(usernameEdit_, &QLineEdit::textChanged, this, &ProfilePage::onUsernameChanged);
    usernameLayout->addWidget(usernameEdit_);
    
    usernameStatusLabel_ = new QLabel;
    usernameStatusLabel_->setStyleSheet("font-size: 11px;");
    usernameLayout->addWidget(usernameStatusLabel_);
    
    formLayout->addRow(tr("Username:"), usernameContainer);
    
    // Bio
    auto* bioContainer = new QWidget;
    auto* bioLayout = new QVBoxLayout(bioContainer);
    bioLayout->setContentsMargins(0, 0, 0, 0);
    bioLayout->setSpacing(4);
    
    bioEdit_ = new QTextEdit;
    bioEdit_->setPlaceholderText(tr("Расскажите о себе..."));
    bioEdit_->setMaximumHeight(100);
    bioEdit_->setStyleSheet("QTextEdit { padding: 8px 12px; border: 1px solid #e0e0e0; border-radius: 6px; }");
    connect(bioEdit_, &QTextEdit::textChanged, this, &ProfilePage::onBioChanged);
    bioLayout->addWidget(bioEdit_);
    
    bioCounterLabel_ = new QLabel("0 / 200");
    bioCounterLabel_->setStyleSheet("font-size: 11px; color: #999;");
    bioCounterLabel_->setAlignment(Qt::AlignRight);
    bioLayout->addWidget(bioCounterLabel_);
    
    formLayout->addRow(tr("О себе:"), bioContainer);
    
    contentLayout->addWidget(formWidget);
    contentLayout->addStretch();
    
    // Save button
    saveButton_ = new QPushButton(tr("Сохранить"));
    saveButton_->setStyleSheet(
        "QPushButton { padding: 12px 24px; background-color: #2196F3; color: white; "
        "border: none; border-radius: 6px; font-size: 14px; font-weight: bold; }"
        "QPushButton:hover { background-color: #1976D2; }"
        "QPushButton:disabled { background-color: #bdbdbd; }"
    );
    saveButton_->setEnabled(false);
    connect(saveButton_, &QPushButton::clicked, this, &ProfilePage::onSaveClicked);
    contentLayout->addWidget(saveButton_, 0, Qt::AlignRight);
    
    scrollArea->setWidget(contentWidget);
    mainLayout->addWidget(scrollArea);
}

void ProfilePage::loadProfile() {
    showLoading(true);
    
    QtConcurrent::run([this]() {
        try {
            auto user = usersClient_->getUser(userId_, accessToken_);
            QMetaObject::invokeMethod(this, [this, user]() {
                currentUser_ = user;
                originalUser_ = user;
                
                firstNameEdit_->setText(user.firstName);
                lastNameEdit_->setText(user.lastName);
                usernameEdit_->setText(user.username);
                bioEdit_->setPlainText(user.bio);
                bioCounterLabel_->setText(QString("%1 / 200").arg(user.bio.length()));
                
                updateAvatar();
                showLoading(false);
            });
        } catch (const std::exception& e) {
            QMetaObject::invokeMethod(this, [this, msg = QString(e.what())]() {
                showLoading(false);
                showError(tr("Не удалось загрузить профиль: %1").arg(msg));
            });
        }
    });
}

void ProfilePage::onSaveClicked() {
    if (!hasChanges()) return;
    
    isSaving_ = true;
    saveButton_->setEnabled(false);
    saveButton_->setText(tr("Сохранение..."));
    
    QtConcurrent::run([this]() {
        try {
            // Save name if changed
            if (currentUser_.firstName != originalUser_.firstName || 
                currentUser_.lastName != originalUser_.lastName) {
                usersClient_->changeName(currentUser_.firstName, currentUser_.lastName, accessToken_);
            }
            
            // Save username if changed
            if (currentUser_.username != originalUser_.username) {
                // Check availability first
                bool exists = usersClient_->checkExistUsername(currentUser_.username);
                if (exists) {
                    throw std::runtime_error("Username уже занят");
                }
                usersClient_->changeUsername(currentUser_.username, accessToken_);
            }
            
            // Save bio if changed
            if (currentUser_.bio != originalUser_.bio) {
                usersClient_->changeBio(currentUser_.bio, accessToken_);
            }
            
            QMetaObject::invokeMethod(this, [this]() {
                originalUser_ = currentUser_;
                isSaving_ = false;
                saveButton_->setText(tr("Сохранить"));
                updateSaveButtonState();
                emit profileUpdated(currentUser_);
            });
        } catch (const std::exception& e) {
            QMetaObject::invokeMethod(this, [this, msg = QString(e.what())]() {
                isSaving_ = false;
                saveButton_->setText(tr("Сохранить"));
                showError(tr("Не удалось сохранить: %1").arg(msg));
                updateSaveButtonState();
            });
        }
    });
}

void ProfilePage::onLogoutClicked() {
    emit logoutRequested();
}

void ProfilePage::onAvatarClicked() {
    auto filePath = QFileDialog::getOpenFileName(
        this,
        tr("Выберите аватар"),
        QString(),
        tr("Изображения (*.png *.jpg *.jpeg *.gif *.webp)")
    );
    
    if (filePath.isEmpty()) return;
    
    // TODO: Upload avatar
    qDebug() << "Avatar selected:" << filePath;
}

void ProfilePage::onFirstNameChanged(const QString& text) {
    currentUser_.firstName = text;
    updateSaveButtonState();
}

void ProfilePage::onLastNameChanged(const QString& text) {
    currentUser_.lastName = text;
    updateSaveButtonState();
}

void ProfilePage::onUsernameChanged(const QString& text) {
    currentUser_.username = text;
    usernameStatus_ = UsernameValidationStatus::None;
    usernameStatusLabel_->clear();
    
    if (text != originalUser_.username && !text.isEmpty()) {
        usernameDebounceTimer_->start();
    }
    
    updateSaveButtonState();
}

void ProfilePage::onBioChanged() {
    QString text = bioEdit_->toPlainText();
    if (text.length() > 200) {
        text = text.left(200);
        bioEdit_->setPlainText(text);
    }
    currentUser_.bio = text;
    bioCounterLabel_->setText(QString("%1 / 200").arg(text.length()));
    updateSaveButtonState();
}

void ProfilePage::validateUsernameDebounced() {
    QString username = usernameEdit_->text();
    
    if (username.isEmpty() || username.length() < 3) {
        usernameStatus_ = UsernameValidationStatus::TooShort;
        usernameStatusLabel_->setText(tr("Минимум 3 символа"));
        usernameStatusLabel_->setStyleSheet("color: #f44336; font-size: 11px;");
        return;
    }
    
    usernameStatusLabel_->setText(tr("Проверка..."));
    usernameStatusLabel_->setStyleSheet("color: #666; font-size: 11px;");
    pendingUsername_ = username;
    
    usernameCheckWatcher_->setFuture(QtConcurrent::run([this, username]() {
        return !usersClient_->checkExistUsername(username);
    }));
}

void ProfilePage::onUsernameValidationFinished() {
    bool available = usernameCheckWatcher_->result();
    
    if (usernameEdit_->text() != pendingUsername_) {
        return;  // User changed username while checking
    }
    
    if (available) {
        usernameStatus_ = UsernameValidationStatus::Available;
        usernameStatusLabel_->setText(tr("✓ Доступен"));
        usernameStatusLabel_->setStyleSheet("color: #4CAF50; font-size: 11px;");
    } else {
        usernameStatus_ = UsernameValidationStatus::Taken;
        usernameStatusLabel_->setText(tr("✗ Занят"));
        usernameStatusLabel_->setStyleSheet("color: #f44336; font-size: 11px;");
    }
    
    updateSaveButtonState();
}

void ProfilePage::updateSaveButtonState() {
    bool canSave = hasChanges() && !isSaving_ && 
                   (usernameStatus_ != UsernameValidationStatus::Taken);
    saveButton_->setEnabled(canSave);
}

bool ProfilePage::hasChanges() const {
    return currentUser_.firstName != originalUser_.firstName ||
           currentUser_.lastName != originalUser_.lastName ||
           currentUser_.username != originalUser_.username ||
           currentUser_.bio != originalUser_.bio;
}

QString ProfilePage::currentInitials() const {
    QString result;
    if (!currentUser_.firstName.isEmpty()) {
        result += currentUser_.firstName.at(0);
    }
    if (!currentUser_.lastName.isEmpty()) {
        result += currentUser_.lastName.at(0);
    }
    return result.toUpper();
}

void ProfilePage::showLoading(bool show) {
    isLoading_ = show;
    setEnabled(!show);
}

void ProfilePage::showError(const QString& message) {
    QMessageBox::warning(this, tr("Ошибка"), message);
}

void ProfilePage::updateAvatar() {
    avatarWidget_->setInitials(currentInitials());
    if (!currentUser_.profilePicture.isEmpty()) {
        avatarWidget_->setImageUrl(currentUser_.profilePicture);
    }
}

} // namespace BarkFluff