/**
 * @file ScrollToBottomButton.cpp
 * @brief Реализация кнопки прокрутки вниз
 */

#include "ScrollToBottomButton.h"
#include <QPainter>
#include <QPainterPath>
#include <QGraphicsOpacityEffect>

namespace BarkFluff {

ScrollToBottomButton::ScrollToBottomButton(QWidget* parent)
    : QPushButton(parent)
{
    setupUI();
}

void ScrollToBottomButton::setupUI() {
    setFixedSize(buttonSize_, buttonSize_);
    setCursor(Qt::PointingHandCursor);
    
    // Круглая кнопка
    setStyleSheet(
        "QPushButton {"
        "  background-color: white;"
        "  border: 1px solid #e0e0e0;"
        "  border-radius: 20px;"
        "}"
        "QPushButton:hover {"
        "  background-color: #f5f5f5;"
        "}"
        "QPushButton:pressed {"
        "  background-color: #e0e0e0;"
        "}"
    );
    
    // Badge для непрочитанных
    badgeLabel_ = new QLabel(this);
    badgeLabel_->setAlignment(Qt::AlignCenter);
    badgeLabel_->setFixedSize(badgeSize_, badgeSize_);
    badgeLabel_->setStyleSheet(
        "QLabel {"
        "  background-color: #2196F3;"
        "  color: white;"
        "  border-radius: 10px;"
        "  font-size: 11px;"
        "  font-weight: bold;"
        "}"
    );
    badgeLabel_->hide();
    
    // Позиция badge в правом верхнем углу
    badgeLabel_->move(buttonSize_ - badgeSize_ + 4, -4);
    
    // Анимации
    showAnimation_ = new QPropertyAnimation(this, "opacity", this);
    showAnimation_->setDuration(200);
    showAnimation_->setStartValue(0.0);
    showAnimation_->setEndValue(1.0);
    
    hideAnimation_ = new QPropertyAnimation(this, "opacity", this);
    hideAnimation_->setDuration(200);
    hideAnimation_->setStartValue(1.0);
    hideAnimation_->setEndValue(0.0);
    connect(hideAnimation_, &QPropertyAnimation::finished, this, &QPushButton::hide);
    
    // Изначально скрыта
    QPushButton::hide();
}

void ScrollToBottomButton::setUnreadCount(int count) {
    unreadCount_ = count;
    updateBadge();
}

void ScrollToBottomButton::updateBadge() {
    if (unreadCount_ > 0) {
        QString text = unreadCount_ > 99 ? "99+" : QString::number(unreadCount_);
        badgeLabel_->setText(text);
        badgeLabel_->show();
    } else {
        badgeLabel_->hide();
    }
}

void ScrollToBottomButton::showAnimated() {
    if (isVisible()) return;
    
    // Прерываем анимацию скрытия если идёт
    hideAnimation_->stop();
    
    show();
    showAnimation_->start();
}

void ScrollToBottomButton::hideAnimated() {
    if (!isVisible()) return;
    
    // Прерываем анимацию показа если идёт
    showAnimation_->stop();
    
    hideAnimation_->start();
}

bool ScrollToBottomButton::isEffectivelyVisible() const {
    return isVisible() && opacity_ > 0.5;
}

void ScrollToBottomButton::setOpacity(double value) {
    opacity_ = value;
    
    // Применяем opacity через graphics effect
    auto* effect = graphicsEffect();
    if (!effect) {
        effect = new QGraphicsOpacityEffect(this);
        setGraphicsEffect(effect);
    }
    
    if (auto* opacityEffect = qobject_cast<QGraphicsOpacityEffect*>(effect)) {
        opacityEffect->setOpacity(opacity_);
    }
    
    update();
}

void ScrollToBottomButton::paintEvent(QPaintEvent* event) {
    QPushButton::paintEvent(event);
    
    // Рисуем стрелку вниз
    QPainter painter(this);
    painter.setRenderHint(QPainter::Antialiasing, true);
    
    // Цвет стрелки
    QPen pen("#666666");
    pen.setWidth(2);
    pen.setCapStyle(Qt::RoundCap);
    pen.setJoinStyle(Qt::RoundJoin);
    painter.setPen(pen);
    
    // Центр кнопки
    int cx = buttonSize_ / 2;
    int cy = buttonSize_ / 2;
    
    // Стрелка вниз (V)
    QPainterPath path;
    path.moveTo(cx - 8, cy - 3);
    path.lineTo(cx, cy + 5);
    path.lineTo(cx + 8, cy - 3);
    
    painter.drawPath(path);
}

} // namespace BarkFluff