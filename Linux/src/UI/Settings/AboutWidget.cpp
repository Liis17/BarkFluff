/**
 * @file AboutWidget.cpp
 * @brief Реализация виджета информации о сервере
 */

#include "AboutWidget.h"

#include <QVBoxLayout>
#include <QLabel>

namespace BarkFluff {

AboutWidget::AboutWidget(const ServerConfiguration& config, QWidget* parent)
    : QWidget(parent)
    , config_(config)
{
    setupUI();
}

void AboutWidget::setupUI() {
    auto* mainLayout = new QVBoxLayout(this);
    mainLayout->setContentsMargins(24, 24, 24, 24);
    mainLayout->setSpacing(16);
    
    // Title
    auto* titleLabel = new QLabel(tr("О сервере"), this);
    titleLabel->setStyleSheet(QStringLiteral("font-size: 18px; font-weight: bold; color: #333333;"));
    mainLayout->addWidget(titleLabel);
    
    // Server info frame
    auto* infoFrame = new QWidget(this);
    infoFrame->setStyleSheet(R"(
        QWidget {
            background-color: #f5f5f5;
            border-radius: 8px;
        }
    )");
    auto* infoLayout = new QVBoxLayout(infoFrame);
    infoLayout->setContentsMargins(16, 16, 16, 16);
    infoLayout->setSpacing(12);
    
    // Server name
    auto* nameLabel = new QLabel(tr("Сервер:"), infoFrame);
    nameLabel->setStyleSheet(QStringLiteral("color: #888888; font-size: 12px;"));
    infoLayout->addWidget(nameLabel);
    
    auto* nameValue = new QLabel(config_.name, infoFrame);
    nameValue->setStyleSheet(QStringLiteral("color: #333333; font-size: 16px; font-weight: bold;"));
    nameValue->setWordWrap(true);
    infoLayout->addWidget(nameValue);
    
    // Server host
    auto* hostLabel = new QLabel(tr("Адрес:"), infoFrame);
    hostLabel->setStyleSheet(QStringLiteral("color: #888888; font-size: 12px;"));
    infoLayout->addWidget(hostLabel);
    
    QString hostInfo;
    if (config_.identity.tlsEnabled) {
        hostInfo = QStringLiteral("https://");
    } else {
        hostInfo = QStringLiteral("http://");
    }
    hostInfo += config_.identity.endpoint.host + QStringLiteral(":") + QString::number(config_.identity.endpoint.port);
    
    auto* hostValue = new QLabel(hostInfo, infoFrame);
    hostValue->setStyleSheet(QStringLiteral("color: #333333; font-size: 14px;"));
    hostValue->setTextInteractionFlags(Qt::TextSelectableByMouse);
    infoLayout->addWidget(hostValue);
    
    // Server description
    if (!config_.description.isEmpty()) {
        auto* descLabel = new QLabel(tr("Описание:"), infoFrame);
        descLabel->setStyleSheet(QStringLiteral("color: #888888; font-size: 12px;"));
        infoLayout->addWidget(descLabel);
        
        auto* descValue = new QLabel(config_.description, infoFrame);
        descValue->setStyleSheet(QStringLiteral("color: #666666; font-size: 13px;"));
        descValue->setWordWrap(true);
        infoLayout->addWidget(descValue);
    }
    
    mainLayout->addWidget(infoFrame);
    
    // App info frame
    auto* appFrame = new QWidget(this);
    appFrame->setStyleSheet(R"(
        QWidget {
            background-color: #f5f5f5;
            border-radius: 8px;
        }
    )");
    auto* appLayout = new QVBoxLayout(appFrame);
    appLayout->setContentsMargins(16, 16, 16, 16);
    appLayout->setSpacing(8);
    
    auto* appTitle = new QLabel(tr("О приложении"), appFrame);
    appTitle->setStyleSheet(QStringLiteral("color: #333333; font-weight: bold;"));
    appLayout->addWidget(appTitle);
    
    auto* versionLabel = new QLabel(tr("Версия: 1.0.0 (Qt)"), appFrame);
    versionLabel->setStyleSheet(QStringLiteral("color: #888888;"));
    appLayout->addWidget(versionLabel);
    
    auto* qtLabel = new QLabel(tr("Платформа: Qt %1").arg(qVersion()), appFrame);
    qtLabel->setStyleSheet(QStringLiteral("color: #888888;"));
    appLayout->addWidget(qtLabel);
    
    mainLayout->addWidget(appFrame);
    
    mainLayout->addStretch();
}

} // namespace BarkFluff