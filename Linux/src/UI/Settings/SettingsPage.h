/**
 * @file SettingsPage.h
 * @brief Главная страница настроек приложения
 */

#pragma once

#include <QWidget>
#include <QStackedWidget>
#include <QListWidget>
#include <QLabel>
#include <QPushButton>
#include <memory>

#include "SettingsCategory.h"
#include "Models/ServerConfiguration.h"
#include "Models/UserSession.h"

namespace BarkFluff {

class IdentityClient;
class GeneralSettingsWidget;
class SessionsSettingsWidget;
class SecuritySettingsWidget;
class StorageSettingsWidget;
class AboutWidget;

/**
 * @brief Главная страница настроек с навигацией по категориям
 */
class SettingsPage : public QWidget {
    Q_OBJECT

public:
    explicit SettingsPage(
        const ServerConfiguration& config,
        const UserSession& session,
        QWidget* parent = nullptr
    );
    ~SettingsPage() override;

signals:
    void backRequested();
    void logoutRequested();

private slots:
    void onCategoryChanged(int row);

private:
    void setupUI();
    void showCategory(SettingsCategory category);

    ServerConfiguration config_;
    UserSession session_;
    std::unique_ptr<IdentityClient> identityClient_;

    QListWidget* categoryList_ = nullptr;
    QStackedWidget* contentStack_ = nullptr;
    QLabel* titleLabel_ = nullptr;
    QPushButton* backButton_ = nullptr;

    // Category widgets
    GeneralSettingsWidget* generalWidget_ = nullptr;
    SessionsSettingsWidget* sessionsWidget_ = nullptr;
    SecuritySettingsWidget* securityWidget_ = nullptr;
    StorageSettingsWidget* storageWidget_ = nullptr;
    AboutWidget* aboutWidget_ = nullptr;
};

} // namespace BarkFluff