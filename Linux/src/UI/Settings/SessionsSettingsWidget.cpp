/**
 * @file SessionsSettingsWidget.cpp
 * @brief Реализация виджета управления сессиями
 */

#include "SessionsSettingsWidget.h"
#include "Connection/IdentityClient.h"
#include "Utils/DeviceIdGenerator.h"

#include <QVBoxLayout>
#include <QHBoxLayout>
#include <QListWidgetItem>
#include <QMessageBox>
#include <QProgressDialog>
#include <QSize>
#include <QtConcurrent>

namespace BarkFluff {

SessionsSettingsWidget::SessionsSettingsWidget(
    IdentityClient* client,
    const UserSession& session,
    QWidget* parent
)
    : QWidget(parent)
    , identityClient_(client)
    , session_(session)
{
    loadWatcher_ = new QFutureWatcher<QList<SessionInfo>>(this);
    connect(loadWatcher_, &QFutureWatcher<QList<SessionInfo>>::finished,
            this, &SessionsSettingsWidget::onSessionsLoaded);
    
    setupUI();
}

void SessionsSettingsWidget::setupUI() {
    auto* mainLayout = new QVBoxLayout(this);
    mainLayout->setContentsMargins(24, 24, 24, 24);
    mainLayout->setSpacing(16);
    
    // Title
    auto* titleLabel = new QLabel(tr("Активные сессии"), this);
    titleLabel->setStyleSheet(QStringLiteral("font-size: 18px; font-weight: bold; color: #333333;"));
    mainLayout->addWidget(titleLabel);
    
    // Description
    auto* descLabel = new QLabel(
        tr("Если вы видите незнакомые устройства, завершите сессию и смените пароль"),
        this
    );
    descLabel->setWordWrap(true);
    descLabel->setStyleSheet(QStringLiteral("color: #888888; font-size: 13px;"));
    mainLayout->addWidget(descLabel);
    
    // Loading label
    loadingLabel_ = new QLabel(tr("Загрузка сессий..."), this);
    loadingLabel_->setStyleSheet(QStringLiteral("color: #888888; padding: 20px;"));
    loadingLabel_->setAlignment(Qt::AlignCenter);
    mainLayout->addWidget(loadingLabel_);
    
    // Empty label
    emptyLabel_ = new QLabel(tr("Нет активных сессий"), this);
    emptyLabel_->setStyleSheet(QStringLiteral("color: #888888; padding: 20px;"));
    emptyLabel_->setAlignment(Qt::AlignCenter);
    emptyLabel_->hide();
    mainLayout->addWidget(emptyLabel_);
    
    // Session list
    sessionList_ = new QListWidget(this);
    sessionList_->setStyleSheet(R"(
        QListWidget {
            background-color: #ffffff;
            border: 1px solid #e0e0e0;
            border-radius: 8px;
            outline: none;
        }
        QListWidget::item {
            padding: 16px;
            border-bottom: 1px solid #e0e0e0;
        }
        QListWidget::item:last-child {
            border-bottom: none;
        }
        QListWidget::item:hover {
            background-color: #f5f5f5;
        }
    )");
    sessionList_->hide();
    mainLayout->addWidget(sessionList_, 1);
    
    // Error label
    errorLabel_ = new QLabel(this);
    errorLabel_->setStyleSheet(QStringLiteral("color: #ff6b6b; padding: 10px;"));
    errorLabel_->setWordWrap(true);
    errorLabel_->hide();
    mainLayout->addWidget(errorLabel_);
    
    // Terminate all button
    terminateAllBtn_ = new QPushButton(tr("Завершить все остальные сессии"), this);
    terminateAllBtn_->setStyleSheet(R"(
        QPushButton {
            background-color: #3a3a3a;
            color: #ff6b6b;
            border: none;
            padding: 10px 16px;
            border-radius: 6px;
            font-size: 13px;
        }
        QPushButton:hover {
            background-color: #4a4a4a;
        }
        QPushButton:disabled {
            color: #666666;
        }
    )");
    terminateAllBtn_->hide();
    connect(terminateAllBtn_, &QPushButton::clicked, this, &SessionsSettingsWidget::onTerminateAllOther);
    mainLayout->addWidget(terminateAllBtn_);
    
    mainLayout->addStretch();
}

void SessionsSettingsWidget::loadSessions() {
    if (isLoading_) return;
    
    isLoading_ = true;
    loadingLabel_->show();
    sessionList_->hide();
    emptyLabel_->hide();
    errorLabel_->hide();
    terminateAllBtn_->hide();
    
    auto future = QtConcurrent::run([this]() {
        try {
            return identityClient_->getActiveSessions(session_.accessToken.value);
        } catch (...) {
            return QList<SessionInfo>();
        }
    });
    
    loadWatcher_->setFuture(future);
}

void SessionsSettingsWidget::onSessionsLoaded() {
    isLoading_ = false;
    loadingLabel_->hide();
    
    sessions_ = loadWatcher_->result();
    
    if (sessions_.isEmpty()) {
        emptyLabel_->show();
    } else {
        updateSessionList();
        sessionList_->show();
        
        // Show terminate all button if there are other sessions
        QString currentDeviceId = DeviceIdGenerator::getOrCreateDeviceId();
        int otherSessions = 0;
        for (const auto& session : sessions_) {
            if (session.deviceId != currentDeviceId) {
                otherSessions++;
            }
        }
        if (otherSessions > 0) {
            terminateAllBtn_->show();
        }
    }
}

void SessionsSettingsWidget::updateSessionList() {
    sessionList_->clear();
    
    QString currentDeviceId = DeviceIdGenerator::getOrCreateDeviceId();
    
    for (const auto& session : sessions_) {
        auto* item = new QListWidgetItem(sessionList_);
        
        bool isCurrent = session.deviceId == currentDeviceId;
        
        // Create item widget
        auto* itemWidget = new QWidget(sessionList_);
        auto* itemLayout = new QHBoxLayout(itemWidget);
        itemLayout->setContentsMargins(12, 8, 12, 8);
        itemLayout->setSpacing(12);
        
        // Icon label
        auto* iconLabel = new QLabel(itemWidget);
        iconLabel->setText(osIconName(session.operationSystem));
        iconLabel->setStyleSheet(QStringLiteral("font-size: 24px;"));
        iconLabel->setFixedSize(32, 32);
        itemLayout->addWidget(iconLabel);
        
        // Info layout
        auto* infoLayout = new QVBoxLayout();
        infoLayout->setSpacing(8);
        infoLayout->setContentsMargins(0, 4, 0, 4);
        
        // Name row
        auto* nameLayout = new QHBoxLayout();
        nameLayout->setSpacing(8);
        auto* nameLabel = new QLabel(session.displayName(), itemWidget);
        nameLabel->setStyleSheet(QStringLiteral("color: #333333; font-weight: bold; font-size: 13px;"));
        nameLabel->setMinimumHeight(20);
        nameLayout->addWidget(nameLabel);
        
        if (isCurrent) {
            auto* currentBadge = new QLabel(tr("Текущая"), itemWidget);
            currentBadge->setStyleSheet(R"(
                background-color: #0078d4;
                color: #ffffff;
                padding: 4px 8px;
                border-radius: 4px;
                font-size: 11px;
            )");
            currentBadge->setMinimumHeight(20);
            nameLayout->addWidget(currentBadge);
        }
        
        nameLayout->addStretch();
        infoLayout->addLayout(nameLayout);
        
        // App and OS
        QString appOs;
        if (!session.appName.isEmpty()) {
            appOs = session.appName;
        }
        if (!session.operationSystem.isEmpty()) {
            if (!appOs.isEmpty()) appOs += QStringLiteral(" · ");
            appOs += session.operationSystem;
        }
        if (!appOs.isEmpty()) {
            auto* appOsLabel = new QLabel(appOs, itemWidget);
            appOsLabel->setStyleSheet(QStringLiteral("color: #888888; font-size: 12px;"));
            appOsLabel->setMinimumHeight(18);
            infoLayout->addWidget(appOsLabel);
        }
        
        // Location and date
        QString locationDate;
        if (!session.location.isEmpty()) {
            locationDate = session.location + QStringLiteral(" · ");
        }
        locationDate += session.createdAt.toString(QStringLiteral("dd.MM.yyyy"));
        
        auto* locLabel = new QLabel(locationDate, itemWidget);
        locLabel->setStyleSheet(QStringLiteral("color: #666666; font-size: 11px;"));
        locLabel->setMinimumHeight(18);
        infoLayout->addWidget(locLabel);
        
        itemLayout->addLayout(infoLayout, 1);
        
        // Terminate button (not for current session)
        if (!isCurrent) {
            auto* terminateBtn = new QPushButton(tr("Завершить"), itemWidget);
            terminateBtn->setStyleSheet(R"(
                QPushButton {
                    background-color: transparent;
                    color: #ff6b6b;
                    border: 1px solid #ff6b6b;
                    padding: 6px 12px;
                    border-radius: 4px;
                    font-size: 12px;
                }
                QPushButton:hover {
                    background-color: #ff6b6b;
                    color: #ffffff;
                }
            )");
            connect(terminateBtn, &QPushButton::clicked, this, [this, session]() {
                onTerminateSession(session.deviceId);
            });
            itemLayout->addWidget(terminateBtn);
        }
        
        itemWidget->setLayout(itemLayout);
        item->setSizeHint(itemWidget->sizeHint());
        sessionList_->setItemWidget(item, itemWidget);
    }
}

void SessionsSettingsWidget::onTerminateSession(const QString& deviceId) {
    auto reply = QMessageBox::question(
        this,
        tr("Завершить сессию?"),
        tr("Вы уверены, что хотите завершить эту сессию?"),
        QMessageBox::Yes | QMessageBox::No,
        QMessageBox::No
    );
    
    if (reply != QMessageBox::Yes) return;
    
    try {
        identityClient_->removeActiveSession(deviceId, session_.accessToken.value);
        
        // Remove from list
        sessions_.removeIf([deviceId](const SessionInfo& s) {
            return s.deviceId == deviceId;
        });
        
        updateSessionList();
        
        // Update terminate all button visibility
        QString currentDeviceId = DeviceIdGenerator::getOrCreateDeviceId();
        int otherSessions = 0;
        for (const auto& session : sessions_) {
            if (session.deviceId != currentDeviceId) {
                otherSessions++;
            }
        }
        terminateAllBtn_->setVisible(otherSessions > 0);
        
    } catch (const std::exception& e) {
        errorLabel_->setText(tr("Не удалось завершить сессию: %1").arg(QString::fromStdString(e.what())));
        errorLabel_->show();
    }
}

void SessionsSettingsWidget::onTerminateAllOther() {
    auto reply = QMessageBox::question(
        this,
        tr("Завершить все сессии?"),
        tr("Вы уверены, что хотите завершить все остальные сессии?"),
        QMessageBox::Yes | QMessageBox::No,
        QMessageBox::No
    );
    
    if (reply != QMessageBox::Yes) return;
    
    QString currentDeviceId = DeviceIdGenerator::getOrCreateDeviceId();
    QStringList toRemove;
    
    for (const auto& session : sessions_) {
        if (session.deviceId != currentDeviceId) {
            toRemove.append(session.deviceId);
        }
    }
    
    int removed = 0;
    for (const auto& deviceId : toRemove) {
        try {
            identityClient_->removeActiveSession(deviceId, session_.accessToken.value);
            removed++;
        } catch (...) {
            // Continue with other sessions
        }
    }
    
    // Reload sessions
    loadSessions();
}

QString SessionsSettingsWidget::osIconName(const QString& os) const {
    QString lower = os.toLower();
    
    if (lower.contains(QStringLiteral("mac")) || lower.contains(QStringLiteral("macos"))) {
        return QStringLiteral("🖥️");  // desktop
    } else if (lower.contains(QStringLiteral("iphone")) || lower.contains(QStringLiteral("ios"))) {
        return QStringLiteral("📱");  // phone
    } else if (lower.contains(QStringLiteral("ipad"))) {
        return QStringLiteral("📱");  // tablet
    } else if (lower.contains(QStringLiteral("android"))) {
        return QStringLiteral("📱");  // phone
    } else if (lower.contains(QStringLiteral("windows"))) {
        return QStringLiteral("💻");  // pc
    } else if (lower.contains(QStringLiteral("linux"))) {
        return QStringLiteral("🖥️");  // server
    } else if (lower.contains(QStringLiteral("web"))) {
        return QStringLiteral("🌐");  // globe
    }
    return QStringLiteral("❓");
}

} // namespace BarkFluff