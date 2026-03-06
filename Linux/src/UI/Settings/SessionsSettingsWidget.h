/**
 * @file SessionsSettingsWidget.h
 * @brief Виджет управления активными сессиями
 */

#pragma once

#include <QWidget>
#include <QListWidget>
#include <QLabel>
#include <QPushButton>
#include <QFutureWatcher>
#include "Models/SessionInfo.h"
#include "Models/UserSession.h"

namespace BarkFluff {

class IdentityClient;

/**
 * @brief Виджет для просмотра и управления активными сессиями
 */
class SessionsSettingsWidget : public QWidget {
    Q_OBJECT

public:
    explicit SessionsSettingsWidget(
        IdentityClient* client,
        const UserSession& session,
        QWidget* parent = nullptr
    );

    void loadSessions();

private slots:
    void onTerminateSession(const QString& deviceId);
    void onTerminateAllOther();
    void onSessionsLoaded();

private:
    void setupUI();
    void updateSessionList();
    QString osIconName(const QString& os) const;

    IdentityClient* identityClient_ = nullptr;
    UserSession session_;
    
    QList<SessionInfo> sessions_;
    bool isLoading_ = false;
    
    QFutureWatcher<QList<SessionInfo>>* loadWatcher_ = nullptr;
    
    QLabel* loadingLabel_ = nullptr;
    QLabel* emptyLabel_ = nullptr;
    QListWidget* sessionList_ = nullptr;
    QPushButton* terminateAllBtn_ = nullptr;
    QLabel* errorLabel_ = nullptr;
};

} // namespace BarkFluff