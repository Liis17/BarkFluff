/**
 * @file BadgeChipWidget.cpp
 * @brief Реализация виджета чипа баджа
 */

#include "BadgeChipWidget.h"
#include <QHBoxLayout>
#include <QNetworkAccessManager>
#include <QNetworkReply>
#include <QPixmap>
#include <QDebug>

namespace BarkFluff {

BadgeChipWidget::BadgeChipWidget(const UserBadge& badge, QWidget* parent)
    : QWidget(parent)
    , badge_(badge)
{
    setupUI();
    loadIcon();
}

QSize BadgeChipWidget::sizeHint() const {
    return minimumSizeHint();
}

QSize BadgeChipWidget::minimumSizeHint() const {
    // Минимальный размер: иконка 18px + отступы + текст
    QFontMetrics fm(font());
    int textWidth = fm.horizontalAdvance(badge_.badge.name);
    return QSize(36 + textWidth, 30);
}

void BadgeChipWidget::setupUI() {
    auto* layout = new QHBoxLayout(this);
    layout->setContentsMargins(10, 6, 10, 6);
    layout->setSpacing(6);
    
    // Иконка баджа
    iconLabel_ = new QLabel();
    iconLabel_->setFixedSize(18, 18);
    iconLabel_->setScaledContents(true);
    layout->addWidget(iconLabel_);
    
    // Название баджа
    nameLabel_ = new QLabel(badge_.badge.name);
    nameLabel_->setFont(QFont(font().family(), 10));
    layout->addWidget(nameLabel_);
    
    // Описание баджа (справа от названия, если есть)
    if (!badge_.badge.description.isEmpty()) {
        descriptionLabel_ = new QLabel("— " + badge_.badge.description);
        descriptionLabel_->setFont(QFont(font().family(), 9));
        descriptionLabel_->setStyleSheet("color: #888;");
        layout->addWidget(descriptionLabel_);
    }
    
    // Стиль чипа - скруглённый фон
    setStyleSheet(R"(
        BadgeChipWidget {
            background-color: rgba(128, 128, 128, 50);
            border-radius: 12px;
        }
        BadgeChipWidget:hover {
            background-color: rgba(128, 128, 128, 80);
        }
    )");
    
    // Tooltip с полным описанием (для длинных описаний)
    QString tooltip = badge_.badge.name;
    if (!badge_.badge.description.isEmpty()) {
        tooltip = QString("%1\n\n%2").arg(badge_.badge.name, badge_.badge.description);
    }
    setToolTip(tooltip);
    
    setCursor(Qt::PointingHandCursor);
}

void BadgeChipWidget::loadIcon() {
    if (badge_.badge.imageUrl.isEmpty()) {
        // Дефолтная иконка - звёздочка
        iconLabel_->setText("⭐");
        iconLabel_->setAlignment(Qt::AlignCenter);
        iconLabel_->setStyleSheet("font-size: 14px; background: transparent;");
        return;
    }
    
    // Загрузка иконки по URL
    static QNetworkAccessManager* networkManager = new QNetworkAccessManager(this);
    
    QUrl url(badge_.badge.imageUrl);
    if (!url.isValid()) {
        iconLabel_->setText("⭐");
        iconLabel_->setAlignment(Qt::AlignCenter);
        return;
    }
    
    QNetworkReply* reply = networkManager->get(QNetworkRequest(url));
    connect(reply, &QNetworkReply::finished, this, [this, reply]() {
        reply->deleteLater();
        
        if (reply->error() != QNetworkReply::NoError) {
            iconLabel_->setText("⭐");
            iconLabel_->setAlignment(Qt::AlignCenter);
            return;
        }
        
        QByteArray data = reply->readAll();
        QPixmap pixmap;
        if (pixmap.loadFromData(data)) {
            iconLabel_->setPixmap(pixmap.scaled(18, 18, Qt::KeepAspectRatio, Qt::SmoothTransformation));
        } else {
            iconLabel_->setText("⭐");
            iconLabel_->setAlignment(Qt::AlignCenter);
        }
    });
}

} // namespace BarkFluff