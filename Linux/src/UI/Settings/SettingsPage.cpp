/**
 * @file SettingsPage.cpp
 * @brief Реализация главной страницы настроек
 */

#include "SettingsPage.h"
#include "Connection/IdentityClient.h"
#include "GeneralSettingsWidget.h"
#include "SessionsSettingsWidget.h"
#include "SecuritySettingsWidget.h"
#include "StorageSettingsWidget.h"
#include "AboutWidget.h"

#include <QHBoxLayout>
#include <QVBoxLayout>
#include <QPushButton>
#include <QListWidgetItem>
#include <QMessageBox>

namespace BarkFluff {

SettingsPage::SettingsPage(
    const ServerConfiguration& config,
    const UserSession& session,
    QWidget* parent
)
    : QWidget(parent)
    , config_(config)
    , session_(session)
{
    // Create identity client for settings operations
    identityClient_ = std::make_unique<IdentityClient>(config_.identity);
    
    setupUI();
}

SettingsPage::~SettingsPage() = default;

void SettingsPage::setupUI() {
    auto* mainLayout = new QHBoxLayout(this);
    mainLayout->setContentsMargins(0, 0, 0, 0);
    mainLayout->setSpacing(0);
    
    // Left panel - category list
    auto* leftPanel = new QWidget(this);
    leftPanel->setFixedWidth(220);
    leftPanel->setStyleSheet(R"(
        QWidget {
            background-color: #f5f5f5;
            border-right: 1px solid #e0e0e0;
        }
    )");
    
    auto* leftLayout = new QVBoxLayout(leftPanel);
    leftLayout->setContentsMargins(0, 0, 0, 0);
    leftLayout->setSpacing(0);
    
    // Header with back button
    auto* headerLayout = new QHBoxLayout();
    headerLayout->setContentsMargins(12, 12, 12, 12);
    
    backButton_ = new QPushButton(QStringLiteral("←"), this);
    backButton_->setFixedSize(32, 32);
    backButton_->setStyleSheet(R"(
        QPushButton {
            background-color: transparent;
            border: none;
            color: #333333;
            font-size: 18px;
        }
        QPushButton:hover {
            background-color: #e0e0e0;
            border-radius: 4px;
        }
    )");
    connect(backButton_, &QPushButton::clicked, this, &SettingsPage::backRequested);
    
    titleLabel_ = new QLabel(tr("Настройки"), this);
    titleLabel_->setStyleSheet(QStringLiteral("color: #333333; font-size: 16px; font-weight: bold;"));
    
    headerLayout->addWidget(backButton_);
    headerLayout->addWidget(titleLabel_);
    headerLayout->addStretch();
    
    leftLayout->addLayout(headerLayout);
    
    // Category list
    categoryList_ = new QListWidget(this);
    categoryList_->setStyleSheet(R"(
        QListWidget {
            background-color: transparent;
            border: none;
            outline: none;
        }
        QListWidget::item {
            color: #333333;
            padding: 12px 16px;
            border: none;
        }
        QListWidget::item:selected {
            background-color: #2196F3;
            color: #ffffff;
        }
        QListWidget::item:hover:!selected {
            background-color: #e0e0e0;
        }
    )");
    
    // Add categories
    auto categories = SettingsCategoryHelper::allCategories();
    for (auto category : categories) {
        auto* item = new QListWidgetItem(
            SettingsCategoryHelper::title(category),
            categoryList_
        );
        item->setData(Qt::UserRole, static_cast<int>(category));
    }
    
    connect(categoryList_, &QListWidget::currentRowChanged,
            this, &SettingsPage::onCategoryChanged);
    
    leftLayout->addWidget(categoryList_);
    
    mainLayout->addWidget(leftPanel);
    
    // Right panel - content stack
    contentStack_ = new QStackedWidget(this);
    contentStack_->setStyleSheet(R"(
        QStackedWidget {
            background-color: #ffffff;
        }
    )");
    
    // Create category widgets (lazy loading not needed for settings)
    generalWidget_ = new GeneralSettingsWidget(contentStack_);
    contentStack_->addWidget(generalWidget_);
    
    sessionsWidget_ = new SessionsSettingsWidget(identityClient_.get(), session_, contentStack_);
    contentStack_->addWidget(sessionsWidget_);
    
    securityWidget_ = new SecuritySettingsWidget(identityClient_.get(), session_, contentStack_);
    contentStack_->addWidget(securityWidget_);
    
    storageWidget_ = new StorageSettingsWidget(contentStack_);
    contentStack_->addWidget(storageWidget_);
    
    aboutWidget_ = new AboutWidget(config_, contentStack_);
    contentStack_->addWidget(aboutWidget_);
    
    mainLayout->addWidget(contentStack_, 1);
    
    // Select first category
    categoryList_->setCurrentRow(0);
}

void SettingsPage::onCategoryChanged(int row) {
    if (row < 0) return;
    
    auto* item = categoryList_->item(row);
    if (!item) return;
    
    auto category = static_cast<SettingsCategory>(item->data(Qt::UserRole).toInt());
    showCategory(category);
}

void SettingsPage::showCategory(SettingsCategory category) {
    switch (category) {
        case SettingsCategory::General:
            contentStack_->setCurrentWidget(generalWidget_);
            break;
        case SettingsCategory::Sessions:
            sessionsWidget_->loadSessions();
            contentStack_->setCurrentWidget(sessionsWidget_);
            break;
        case SettingsCategory::Security:
            securityWidget_->loadOTPStatus();
            contentStack_->setCurrentWidget(securityWidget_);
            break;
        case SettingsCategory::Storage:
            storageWidget_->updateStorageInfo();
            contentStack_->setCurrentWidget(storageWidget_);
            break;
        case SettingsCategory::About:
            contentStack_->setCurrentWidget(aboutWidget_);
            break;
    }
}

} // namespace BarkFluff