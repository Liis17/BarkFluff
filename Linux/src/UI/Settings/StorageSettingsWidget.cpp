/**
 * @file StorageSettingsWidget.cpp
 * @brief Реализация виджета управления хранилищем
 */

#include "StorageSettingsWidget.h"
#include "Services/FileCacheService.h"

#include <QVBoxLayout>
#include <QMessageBox>
#include <QTimer>

namespace BarkFluff {

StorageSettingsWidget::StorageSettingsWidget(QWidget* parent)
    : QWidget(parent)
{
    setupUI();
}

void StorageSettingsWidget::setupUI() {
    auto* mainLayout = new QVBoxLayout(this);
    mainLayout->setContentsMargins(24, 24, 24, 24);
    mainLayout->setSpacing(16);
    
    // Title
    auto* titleLabel = new QLabel(tr("Хранилище"), this);
    titleLabel->setStyleSheet(QStringLiteral("font-size: 18px; font-weight: bold; color: #333333;"));
    mainLayout->addWidget(titleLabel);
    
    // Cache section
    auto* cacheFrame = new QWidget(this);
    cacheFrame->setStyleSheet(R"(
        QWidget {
            background-color: #f5f5f5;
            border-radius: 8px;
        }
    )");
    auto* cacheLayout = new QVBoxLayout(cacheFrame);
    cacheLayout->setContentsMargins(16, 16, 16, 16);
    cacheLayout->setSpacing(8);
    
    auto* cacheLabel = new QLabel(tr("Кеш файлов"), cacheFrame);
    cacheLabel->setStyleSheet(QStringLiteral("color: #333333; font-weight: bold;"));
    cacheLayout->addWidget(cacheLabel);
    
    auto* cacheDesc = new QLabel(tr("Изображения и файлы из чатов для быстрой загрузки"), cacheFrame);
    cacheDesc->setStyleSheet(QStringLiteral("color: #888888;"));
    cacheDesc->setWordWrap(true);
    cacheLayout->addWidget(cacheDesc);
    
    cacheSizeLabel_ = new QLabel(tr("Размер кеша: вычисление..."), cacheFrame);
    cacheSizeLabel_->setStyleSheet(QStringLiteral("color: #888888;"));
    cacheLayout->addWidget(cacheSizeLabel_);
    
    clearCacheBtn_ = new QPushButton(tr("Очистить кеш"), cacheFrame);
    clearCacheBtn_->setStyleSheet(R"(
        QPushButton {
            background-color: #dc3545;
            color: #ffffff;
            border: 1px solid #bd2130;
            padding: 8px 16px;
            border-radius: 4px;
        }
        QPushButton:hover {
            background-color: #c82333;
            border-color: #a71d2a;
        }
    )");
    connect(clearCacheBtn_, &QPushButton::clicked, this, &StorageSettingsWidget::onClearCacheClicked);
    cacheLayout->addWidget(clearCacheBtn_);
    
    mainLayout->addWidget(cacheFrame);
    
    // Status label
    statusLabel_ = new QLabel(this);
    statusLabel_->setStyleSheet(QStringLiteral("color: #4caf50; padding: 10px;"));
    statusLabel_->hide();
    mainLayout->addWidget(statusLabel_);
    
    mainLayout->addStretch();
}

void StorageSettingsWidget::updateStorageInfo() {
    qint64 cacheSize = FileCacheService::cacheSize();
    cacheSizeLabel_->setText(tr("Размер кеша: %1").arg(formatSize(cacheSize)));
}

void StorageSettingsWidget::onClearCacheClicked() {
    auto reply = QMessageBox::question(
        this,
        tr("Очистить кеш?"),
        tr("Вы уверены, что хотите очистить кеш? Это не удалит файлы с сервера."),
        QMessageBox::Yes | QMessageBox::No,
        QMessageBox::No
    );
    
    if (reply != QMessageBox::Yes) return;
    
    FileCacheService::clearAllCache();
    updateStorageInfo();
    
    statusLabel_->setText(tr("Кеш успешно очищен"));
    statusLabel_->show();
    
    // Hide after 3 seconds
    QTimer::singleShot(3000, this, [this]() {
        statusLabel_->hide();
    });
}

QString StorageSettingsWidget::formatSize(qint64 bytes) const {
    if (bytes < 1024) {
        return QString::number(bytes) + QStringLiteral(" ") + tr("Б");
    } else if (bytes < 1024 * 1024) {
        return QString::number(bytes / 1024.0, 'f', 1) + QStringLiteral(" ") + tr("КБ");
    } else if (bytes < 1024 * 1024 * 1024) {
        return QString::number(bytes / (1024.0 * 1024.0), 'f', 1) + QStringLiteral(" ") + tr("МБ");
    } else {
        return QString::number(bytes / (1024.0 * 1024.0 * 1024.0), 'f', 2) + QStringLiteral(" ") + tr("ГБ");
    }
}

} // namespace BarkFluff