/**
 * @file OnlineStatusWidget.cpp
 * @brief Реализация виджета статуса онлайн
 */

#include "OnlineStatusWidget.h"
#include <QHBoxLayout>
#include <QPainter>
#include <QDebug>

namespace BarkFluff {

OnlineStatusWidget::OnlineStatusWidget(QWidget* parent)
    : QWidget(parent)
{
    setupUI();
}

OnlineStatusWidget::OnlineStatusWidget(Size size, QWidget* parent)
    : QWidget(parent)
    , size_(size)
{
    setupUI();
}

void OnlineStatusWidget::setupUI() {
    auto* layout = new QHBoxLayout(this);
    layout->setContentsMargins(0, 0, 0, 0);
    layout->setSpacing(6);
    
    // Spacer для индикатора (рисуется в paintEvent)
    int indicatorSize = smallIndicatorSize_;
    switch (size_) {
        case Size::Small: indicatorSize = smallIndicatorSize_; break;
        case Size::Medium: indicatorSize = mediumIndicatorSize_; break;
        case Size::Large: indicatorSize = largeIndicatorSize_; break;
    }
    
    auto* indicatorSpacer = new QWidget(this);
    indicatorSpacer->setFixedWidth(indicatorSize);
    layout->addWidget(indicatorSpacer);
    
    textLabel_ = new QLabel(this);
    textLabel_->setStyleSheet("color: #666; font-size: 12px; background-color: transparent;");
    textLabel_->setSizePolicy(QSizePolicy::Maximum, QSizePolicy::Maximum);
    textLabel_->setWordWrap(false);
    textLabel_->setText(tr("Статус неизвестен"));  // Дефолтный текст
    layout->addWidget(textLabel_);
    
    // По умолчанию показываем текст
    showText_ = true;
}

void OnlineStatusWidget::setStatus(const UserOnlineStatusInfo& status) {
    status_ = status.status;
    lastSeen_ = status.lastSeen;
    updateTooltip();
    
    // Обновляем текст если showText включён
    if (showText_ && textLabel_) {
        textLabel_->setText(status.statusText());
    }
    
    update();
}

void OnlineStatusWidget::setStatus(OnlineStatus status, const QDateTime& lastSeen) {
    status_ = status;
    lastSeen_ = lastSeen;
    updateTooltip();
    
    // Обновляем текст если showText включён
    if (showText_ && textLabel_) {
        UserOnlineStatusInfo info;
        info.status = status_;
        info.lastSeen = lastSeen_;
        textLabel_->setText(info.statusText());
    }
    
    update();
}

void OnlineStatusWidget::setShowText(bool show) {
    showText_ = show;
    qDebug() << "OnlineStatusWidget::setShowText - show:" << show 
             << "textLabel_:" << (void*)textLabel_ 
             << "isVisible:" << isVisible();
    
    if (show) {
        UserOnlineStatusInfo info;
        info.status = status_;
        info.lastSeen = lastSeen_;
        QString text = info.statusText();
        qDebug() << "OnlineStatusWidget::setShowText - setting text:" << text;
        
        textLabel_->setText(text);
        
        // Принудительно показываем label и убеждаемся что он не скрыт
        textLabel_->setVisible(true);
        textLabel_->setHidden(false);
        
        // Убеждаемся что layout обновился
        layout()->activate();
        
        // Убеждаемся что label виден и имеет правильный размер
        textLabel_->adjustSize();
        adjustSize();
        
        qDebug() << "OnlineStatusWidget::setShowText - AFTER:"
                 << "textLabel visible:" << textLabel_->isVisible()
                 << "textLabel hidden:" << textLabel_->isHidden()
                 << "textLabel size:" << textLabel_->size()
                 << "textLabel pos:" << textLabel_->pos()
                 << "this size:" << size() 
                 << "this visible:" << isVisible()
                 << "this hidden:" << isHidden()
                 << "geometry:" << geometry();
    } else {
        textLabel_->hide();
    }
    updateGeometry();
}

void OnlineStatusWidget::setSize(Size size) {
    size_ = size;
    updateGeometry();
    update();
}

QSize OnlineStatusWidget::sizeHint() const {
    int indicatorSize = 0;
    switch (size_) {
        case Size::Small:
            indicatorSize = smallIndicatorSize_;
            break;
        case Size::Medium:
            indicatorSize = mediumIndicatorSize_;
            break;
        case Size::Large:
            indicatorSize = largeIndicatorSize_;
            break;
    }
    
    if (showText_ && textLabel_) {
        return QSize(indicatorSize + 4 + textLabel_->sizeHint().width(), 
                     qMax(indicatorSize, textLabel_->sizeHint().height()));
    }
    
    return QSize(indicatorSize, indicatorSize);
}

QSize OnlineStatusWidget::minimumSizeHint() const {
    return sizeHint();
}

void OnlineStatusWidget::paintEvent(QPaintEvent* event) {
    Q_UNUSED(event)
    
    QPainter painter(this);
    painter.setRenderHint(QPainter::Antialiasing, true);
    
    // Determine indicator size
    int indicatorSize = 0;
    switch (size_) {
        case Size::Small:
            indicatorSize = smallIndicatorSize_;
            break;
        case Size::Medium:
            indicatorSize = mediumIndicatorSize_;
            break;
        case Size::Large:
            indicatorSize = largeIndicatorSize_;
            break;
    }
    
    // Determine color
    QColor color;
    switch (status_) {
        case OnlineStatus::Online:
            color = QColor("#4CAF50");  // Green
            break;
        case OnlineStatus::Offline:
            color = QColor("#9E9E9E");  // Grey
            break;
        case OnlineStatus::Unknown:
        default:
            color = QColor("#BDBDBD");  // Light grey for unknown
            break;
    }
    
    // Draw circle
    painter.setBrush(color);
    painter.setPen(Qt::NoPen);
    
    // Position (centered vertically, left-aligned)
    int y = (height() - indicatorSize) / 2;
    painter.drawEllipse(0, y, indicatorSize, indicatorSize);
    
    // Draw white border for better visibility on dark avatars
    if (status_ == OnlineStatus::Online) {
        painter.setBrush(Qt::NoBrush);
        painter.setPen(QPen(Qt::white, 1.5));
        painter.drawEllipse(0, y, indicatorSize, indicatorSize);
    }
}

void OnlineStatusWidget::updateTooltip() {
    UserOnlineStatusInfo info;
    info.status = status_;
    info.lastSeen = lastSeen_;
    
    QString tooltip = info.statusText();
    if (!tooltip.isEmpty()) {
        setToolTip(tooltip);
    } else {
        setToolTip(QString());
    }
}

} // namespace BarkFluff