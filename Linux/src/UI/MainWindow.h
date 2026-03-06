#pragma once

#include <QMainWindow>
#include <QStackedWidget>
#include <QLabel>
#include <QCloseEvent>
#include "Models/ServerConfiguration.h"
#include "Models/UserSession.h"

namespace BarkFluff {

class MessengerPage;
class SettingsPage;

class MainWindow : public QMainWindow {
    Q_OBJECT
    
public:
    explicit MainWindow(QWidget* parent = nullptr);
    
signals:
    void serverSelected(const ServerConfiguration& config);
    
private slots:
    void showServerSelect();
    void showLogin(const ServerConfiguration& config);
    void showLoginWithSavedCredentials(const ServerConfiguration& config, const QString& login);
    void showRegister(const ServerConfiguration& config);
    void showMessenger(const UserSession& session);
    void onLogout();
    void onSettingsRequested();
    void onSettingsBack();
    void onNotificationClicked(const QString& chatId);
    void onMarkAsReadRequested(const QString& chatId);
    
protected:
    void closeEvent(QCloseEvent* event) override;
    
private:
    void tryAutoLogin();
    void showPinDialog();
    void showLoadingOverlay(const QString& message);
    void hideLoadingOverlay();
    
    QStackedWidget* stackedWidget_;
    QWidget* loadingOverlay_ = nullptr;
    ServerConfiguration currentServer_;
    UserSession currentSession_;
    MessengerPage* messengerPage_ = nullptr;
    SettingsPage* settingsPage_ = nullptr;
};

} // namespace BarkFluff
