#include "MainWindow.h"
#include "ServerSelectPage.h"
#include "LoginPage.h"
#include "RegisterPage.h"
#include "MessengerPage.h"
#include "PinUnlockDialog.h"
#include "UI/Settings/SettingsPage.h"
#include "Services/SessionManager.h"
#include "Services/NotificationService.h"
#include "Storage/AppSettings.h"
#include "Utils/JwtUtils.h"
#include <QMenuBar>
#include <QMenu>
#include <QAction>
#include <QMessageBox>
#include <QStatusBar>
#include <QTimer>
#include <QDebug>
#include <QVBoxLayout>
#include <QProgressBar>
#include <QGraphicsOpacityEffect>
#include <QCloseEvent>

namespace BarkFluff {

MainWindow::MainWindow(QWidget* parent)
    : QMainWindow(parent)
{
    setWindowTitle("BarkFluff");
    setMinimumSize(800, 600);
    resize(1000, 700);
    
    // Create stacked widget for page navigation
    stackedWidget_ = new QStackedWidget(this);
    setCentralWidget(stackedWidget_);
    
    // Try auto-login first
    tryAutoLogin();
}

void MainWindow::tryAutoLogin() {
    auto& sessionManager = SessionManager::instance();
    
    if (sessionManager.hasStoredSession()) {
        // There's a stored session - show PIN dialog
        showPinDialog();
    } else {
        // No stored session - start fresh
        showServerSelect();
    }
}

void MainWindow::showPinDialog() {
    auto& sessionManager = SessionManager::instance();
    
    // Get info for display
    QString serverName = sessionManager.getLastServerName();
    QString login = sessionManager.getLastLogin();
    
    // Create beautiful PIN dialog
    auto* dialog = new PinUnlockDialog(this);
    dialog->setUsername(login);
    dialog->setServerName(serverName);
    
    // Handle unlock requested - check PIN before closing dialog
    connect(dialog, &PinUnlockDialog::unlockRequested, this, [this, dialog]() {
        QString pin = dialog->pin();
        
        // Disable inputs while checking
        dialog->setInputsEnabled(false);
        
        if (!SessionManager::instance().initializeWithPin(pin)) {
            // Wrong PIN - show error but keep dialog open
            dialog->showError(tr("Invalid PIN. Please try again."));
            dialog->setInputsEnabled(true);
            return;
        }
        
        // PIN correct - close dialog and proceed with session restore
        dialog->accept();
    });
    
    // Handle dialog accepted (PIN was correct)
    connect(dialog, &QDialog::accepted, this, [this, dialog]() {
        // Delete dialog and proceed with session restore
        dialog->deleteLater();
        
        // Show loading overlay
        showLoadingOverlay(tr("Restoring session..."));
        
        // Use QTimer::singleShot to defer the work
        QTimer::singleShot(0, this, [this]() {
            auto& sessionManager = SessionManager::instance();
            
            // First, get server config
            ServerConfiguration serverConfig = sessionManager.getStoredServerConfig();
            
            if (!serverConfig.isValid()) {
                QString error = sessionManager.lastError();
                hideLoadingOverlay();
                
                QMessageBox msgBox(this);
                msgBox.setIcon(QMessageBox::Warning);
                msgBox.setWindowTitle(tr("Connection Error"));
                msgBox.setText(tr("Failed to connect to server."));
                msgBox.setInformativeText(error);
                QPushButton* retryBtn = msgBox.addButton(tr("Retry"), QMessageBox::ActionRole);
                QPushButton* selectBtn = msgBox.addButton(tr("Select Server"), QMessageBox::ActionRole);
                QPushButton* exitBtn = msgBox.addButton(tr("Exit"), QMessageBox::RejectRole);
                
                msgBox.exec();
                
                if (msgBox.clickedButton() == retryBtn) {
                    showPinDialog();
                } else if (msgBox.clickedButton() == selectBtn) {
                    showServerSelect();
                } else {
                    close();
                }
                return;
            }
            
            // Store server config
            currentServer_ = serverConfig;
            
            // Restore session
            UserSession session = sessionManager.restoreSession();
            
            if (!session.isValid()) {
                // Access token expired - try to refresh
                Token newToken = sessionManager.refreshAccessToken();
                
                if (newToken.isValid()) {
                    session.accessToken = newToken;
                    // Also update userId/username from new token
                    session.userId = JwtUtils::extractUserId(newToken.value);
                    session.username = JwtUtils::extractUsername(newToken.value);
                } else {
                    // Refresh failed
                    hideLoadingOverlay();
                    
                    QMessageBox msgBox(this);
                    msgBox.setIcon(QMessageBox::Warning);
                    msgBox.setWindowTitle(tr("Session Expired"));
                    msgBox.setText(tr("Your session has expired."));
                    msgBox.setInformativeText(tr("Please login again."));
                    msgBox.addButton(tr("OK"), QMessageBox::AcceptRole);
                    
                    msgBox.exec();
                    
                    // Go to login with saved credentials
                    showLoginWithSavedCredentials(serverConfig, 
                        SessionManager::instance().getLastLogin());
                    return;
                }
            }
            
            // Success - hide loading and show messenger
            currentSession_ = session;
            hideLoadingOverlay();
            showMessenger(session);
        });
    });
    
    // Handle dialog rejected (user clicked "sign in with different account")
    connect(dialog, &QDialog::rejected, this, [this, dialog]() {
        dialog->deleteLater();
        showServerSelect();
    });
    
    // Show as modal dialog on top
    dialog->setModal(true);
    dialog->exec();
}

void MainWindow::showServerSelect() {
    auto* page = new ServerSelectPage(this);
    connect(page, &ServerSelectPage::serverSelected, this, &MainWindow::showLogin);
    connect(page, &ServerSelectPage::serverSelected, this, [this](const ServerConfiguration& config) {
        currentServer_ = config;
    });
    
    stackedWidget_->addWidget(page);
    stackedWidget_->setCurrentWidget(page);
}

void MainWindow::showLogin(const ServerConfiguration& config) {
    // Store server config for session saving
    currentServer_ = config;
    
    auto* page = new LoginPage(config, this);
    connect(page, &LoginPage::loginSuccess, this, [this](const UserSession& session) {
        currentSession_ = session;
        showMessenger(session);
    });
    connect(page, &LoginPage::registerRequested, this, [this, config]() {
        showRegister(config);
    });
    
    stackedWidget_->addWidget(page);
    stackedWidget_->setCurrentWidget(page);
}

void MainWindow::showLoginWithSavedCredentials(const ServerConfiguration& config, const QString& login) {
    auto* page = new LoginPage(config, this);
    page->setLogin(login);  // Pre-fill login
    
    connect(page, &LoginPage::loginSuccess, this, [this](const UserSession& session) {
        currentSession_ = session;
        showMessenger(session);
    });
    connect(page, &LoginPage::registerRequested, this, [this, config]() {
        showRegister(config);
    });
    
    stackedWidget_->addWidget(page);
    stackedWidget_->setCurrentWidget(page);
}

void MainWindow::showRegister(const ServerConfiguration& config) {
    auto* page = new RegisterPage(config, this);
    connect(page, &RegisterPage::registerSuccess, this, [this](const UserSession& session) {
        currentSession_ = session;
        showMessenger(session);
    });
    connect(page, &RegisterPage::backToLogin, this, [this]() {
        showLogin(currentServer_);
    });
    
    stackedWidget_->addWidget(page);
    stackedWidget_->setCurrentWidget(page);
}

void MainWindow::showMessenger(const UserSession& session) {
    // Resize window for messenger
    resize(1200, 800);
    setMinimumSize(900, 600);
    
    // Initialize notification service
    auto& notificationService = NotificationService::instance();
    notificationService.initialize();
    notificationService.setMainWindow(this);
    notificationService.setSettings(AppSettings::instance().getNotificationSettings());
    
    // Connect notification signals
    connect(&notificationService, &NotificationService::notificationClicked,
            this, &MainWindow::onNotificationClicked);
    connect(&notificationService, &NotificationService::markAsReadRequested,
            this, &MainWindow::onMarkAsReadRequested);
    
    // Create messenger page
    messengerPage_ = new MessengerPage(currentServer_, session, this);
    connect(messengerPage_, &MessengerPage::logoutRequested, this, &MainWindow::onLogout);
    connect(messengerPage_, &MessengerPage::settingsRequested, this, &MainWindow::onSettingsRequested);
    
    // Setup menu bar
    auto* menuBar = new QMenuBar(this);
    setMenuBar(menuBar);
    
    // Account menu
    auto* accountMenu = menuBar->addMenu(tr("Аккаунт"));
    
    auto* profileAction = accountMenu->addAction(tr("Профиль"));
    connect(profileAction, &QAction::triggered, this, []() {
        // TODO: Show profile
    });
    
    auto* settingsAction = accountMenu->addAction(tr("Настройки"));
    connect(settingsAction, &QAction::triggered, this, &MainWindow::onSettingsRequested);
    
    accountMenu->addSeparator();
    
    auto* logoutAction = accountMenu->addAction(tr("Выйти"));
    connect(logoutAction, &QAction::triggered, this, &MainWindow::onLogout);
    
    stackedWidget_->addWidget(messengerPage_);
    stackedWidget_->setCurrentWidget(messengerPage_);
}

void MainWindow::onSettingsRequested() {
    if (!settingsPage_) {
        settingsPage_ = new SettingsPage(
            currentServer_,
            currentSession_,
            this
        );
        connect(settingsPage_, &SettingsPage::backRequested, 
                this, &MainWindow::onSettingsBack);
        connect(settingsPage_, &SettingsPage::logoutRequested,
                this, &MainWindow::onLogout);
        stackedWidget_->addWidget(settingsPage_);
    }
    
    stackedWidget_->setCurrentWidget(settingsPage_);
}

void MainWindow::onSettingsBack() {
    if (messengerPage_) {
        stackedWidget_->setCurrentWidget(messengerPage_);
    }
}

void MainWindow::onLogout() {
    // Clear stored session
    SessionManager::instance().clearSession();
    
    // Clear session
    currentSession_ = UserSession();
    
    // Remove menu bar
    setMenuBar(nullptr);
    
    // Reset window size
    resize(450, 700);
    setMinimumSize(400, 600);
    
    // Go back to server select
    showServerSelect();
}

void MainWindow::showLoadingOverlay(const QString& message) {
    if (!loadingOverlay_) {
        loadingOverlay_ = new QWidget(this);
        loadingOverlay_->setStyleSheet("background: rgba(255, 255, 255, 230);");
        
        auto* layout = new QVBoxLayout(loadingOverlay_);
        layout->setAlignment(Qt::AlignCenter);
        
        // Progress bar as loading indicator
        auto* progressBar = new QProgressBar();
        progressBar->setRange(0, 0);  // Indeterminate mode
        progressBar->setTextVisible(false);
        progressBar->setFixedWidth(200);
        progressBar->setFixedHeight(4);
        progressBar->setStyleSheet(R"(
            QProgressBar {
                border: none;
                background: #e0e0e0;
                border-radius: 2px;
            }
            QProgressBar::chunk {
                background: #3DAEE9;
                border-radius: 2px;
            }
        )");
        layout->addWidget(progressBar, 0, Qt::AlignCenter);
        
        // Loading text
        auto* loadingLabel = new QLabel();
        loadingLabel->setAlignment(Qt::AlignCenter);
        loadingLabel->setStyleSheet("font-size: 14px; color: #666; margin-top: 16px;");
        loadingLabel->setProperty("class", "loadingLabel");
        layout->addWidget(loadingLabel);
    }
    
    // Update loading text
    auto* loadingLabel = loadingOverlay_->findChild<QLabel*>("loadingLabel");
    if (loadingLabel) {
        loadingLabel->setText(message);
    }
    
    // Position overlay
    loadingOverlay_->setGeometry(rect());
    loadingOverlay_->show();
    loadingOverlay_->raise();
}

void MainWindow::hideLoadingOverlay() {
    if (loadingOverlay_) {
        loadingOverlay_->hide();
    }
}

void MainWindow::closeEvent(QCloseEvent* event) {
    // Clean up messenger page before closing to prevent segfault
    // This ensures gRPC streams are stopped before widgets are destroyed
    for (int i = 0; i < stackedWidget_->count(); ++i) {
        if (auto* messenger = qobject_cast<MessengerPage*>(stackedWidget_->widget(i))) {
            stackedWidget_->removeWidget(messenger);
            messenger->deleteLater();
            break;
        }
    }
    
    // Accept the close event
    event->accept();
}

void MainWindow::onNotificationClicked(const QString& chatId) {
    // Activate window
    show();
    activateWindow();
    raise();
    
    // Open the chat
    if (messengerPage_) {
        messengerPage_->openChatById(chatId);
    }
}

void MainWindow::onMarkAsReadRequested(const QString& chatId) {
    // Mark chat as read via MessagesClient
    if (messengerPage_) {
        messengerPage_->markChatAsRead(chatId);
    }
}

} // namespace BarkFluff
