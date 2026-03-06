/**
 * @file GeneralSettingsWidget.cpp
 * @brief Реализация виджета общих настроек
 */

#include "GeneralSettingsWidget.h"
#include "Storage/AppSettings.h"
#include "Services/NotificationService.h"

#include <QVBoxLayout>
#include <QCheckBox>
#include <QLabel>
#include <QGroupBox>

namespace BarkFluff {

GeneralSettingsWidget::GeneralSettingsWidget(QWidget* parent)
    : QWidget(parent)
{
    setupUI();
    loadSettings();
}

void GeneralSettingsWidget::setupUI() {
    auto* mainLayout = new QVBoxLayout(this);
    mainLayout->setContentsMargins(24, 24, 24, 24);
    mainLayout->setSpacing(16);
    
    // Title
    auto* titleLabel = new QLabel(tr("Общие"), this);
    titleLabel->setStyleSheet(QStringLiteral("font-size: 18px; font-weight: bold; color: #333333;"));
    mainLayout->addWidget(titleLabel);
    
    // Notifications group
    auto* groupBox = new QGroupBox(tr("Уведомления"), this);
    groupBox->setStyleSheet(QStringLiteral(
        "QGroupBox { "
        "    font-weight: bold; "
        "    border: 1px solid #ddd; "
        "    border-radius: 6px; "
        "    margin-top: 12px; "
        "    padding-top: 8px; "
        "} "
        "QGroupBox::title { "
        "    subcontrol-origin: margin; "
        "    left: 12px; "
        "    padding: 0 6px; "
        "}"
    ));
    
    auto* groupLayout = new QVBoxLayout(groupBox);
    groupLayout->setSpacing(12);
    groupLayout->setContentsMargins(16, 20, 16, 16);
    
    enabledCheck_ = new QCheckBox(tr("Показывать уведомления о сообщениях"), groupBox);
    previewCheck_ = new QCheckBox(tr("Показывать текст сообщения в уведомлении"), groupBox);
    avatarCheck_ = new QCheckBox(tr("Показывать аватар отправителя"), groupBox);
    
    enabledCheck_->setStyleSheet(QStringLiteral("QCheckBox { spacing: 8px; }"));
    previewCheck_->setStyleSheet(QStringLiteral("QCheckBox { spacing: 8px; }"));
    avatarCheck_->setStyleSheet(QStringLiteral("QCheckBox { spacing: 8px; }"));
    
    groupLayout->addWidget(enabledCheck_);
    groupLayout->addWidget(previewCheck_);
    groupLayout->addWidget(avatarCheck_);
    
    // Note about sound
    auto* soundNote = new QLabel(
        tr("Звук уведомления используется системный"),
        groupBox
    );
    soundNote->setStyleSheet(QStringLiteral("color: #888888; font-style: italic; font-size: 12px;"));
    soundNote->setWordWrap(true);
    groupLayout->addWidget(soundNote);
    
    mainLayout->addWidget(groupBox);
    mainLayout->addStretch();
    
    // Connections
    connect(enabledCheck_, &QCheckBox::stateChanged, this, &GeneralSettingsWidget::onEnabledChanged);
    connect(previewCheck_, &QCheckBox::stateChanged, this, &GeneralSettingsWidget::onPreviewChanged);
    connect(avatarCheck_, &QCheckBox::stateChanged, this, &GeneralSettingsWidget::onAvatarChanged);
    
    // Dependency: disable sub-options when notifications are off
    connect(enabledCheck_, &QCheckBox::toggled, previewCheck_, &QCheckBox::setEnabled);
    connect(enabledCheck_, &QCheckBox::toggled, avatarCheck_, &QCheckBox::setEnabled);
}

void GeneralSettingsWidget::loadSettings() {
    auto settings = AppSettings::instance().getNotificationSettings();
    
    enabledCheck_->setChecked(settings.enabled);
    previewCheck_->setChecked(settings.showPreview);
    avatarCheck_->setChecked(settings.showAvatar);
    
    previewCheck_->setEnabled(settings.enabled);
    avatarCheck_->setEnabled(settings.enabled);
}

void GeneralSettingsWidget::saveSettings() {
    NotificationSettings settings;
    settings.enabled = enabledCheck_->isChecked();
    settings.showPreview = previewCheck_->isChecked();
    settings.showAvatar = avatarCheck_->isChecked();
    
    AppSettings::instance().setNotificationSettings(settings);
    NotificationService::instance().setSettings(settings);
}

void GeneralSettingsWidget::onEnabledChanged(int state) {
    Q_UNUSED(state);
    saveSettings();
}

void GeneralSettingsWidget::onPreviewChanged(int state) {
    Q_UNUSED(state);
    saveSettings();
}

void GeneralSettingsWidget::onAvatarChanged(int state) {
    Q_UNUSED(state);
    saveSettings();
}

} // namespace BarkFluff