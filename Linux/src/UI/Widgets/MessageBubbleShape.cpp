/**
 * @file MessageBubbleShape.cpp
 * @brief Реализация формы пузырька сообщения (скруглено с 3 сторон)
 */

#include "MessageBubbleShape.h"
#include <QPaintEvent>
#include <QRegion>

namespace BarkFluff {

// ============================================================================
// MessageBubbleWidget
// ============================================================================

MessageBubbleWidget::MessageBubbleWidget(
    BubbleTailSide side,
    bool showTail,
    QWidget* parent
)
    : QWidget(parent)
    , side_(side)
    , showTail_(showTail)
    , backgroundColor_("#f0f0f0")
    , borderColor_(Qt::transparent)
{
    setAttribute(Qt::WA_TranslucentBackground, true);
}

void MessageBubbleWidget::setBackgroundColor(const QColor& color) {
    backgroundColor_ = color;
    update();
}

void MessageBubbleWidget::setShowTail(bool show) {
    showTail_ = show;
    update();
}

QPainterPath MessageBubbleWidget::bubblePath() const {
    return createBubblePath(rect(), side_, showTail_, cornerRadius_);
}

int MessageBubbleWidget::tailOffset() const {
    return 0;  // Упрощённая форма без хвостика
}

QPainterPath MessageBubbleWidget::createBubblePath(
    const QRectF& rect,
    BubbleTailSide side,
    bool showTail,
    qreal cornerRadius
) {
    qreal w = rect.width();
    qreal h = rect.height();
    qreal r = cornerRadius;
    
    QPainterPath path;
    
    if (side == BubbleTailSide::Right) {
        // Исходящее — прямой угол справа-снизу
        // Скруглены: верхний-левый, верхний-правый, нижний-левый
        
        path.moveTo(r, 0);                          // верхний-левый (после скругления)
        path.lineTo(w - r, 0);                      // верхняя линия
        path.arcTo(w - 2*r, 0, 2*r, 2*r, 90, -90);  // верхний-правый угол
        path.lineTo(w, h);                          // правая линия (до нижнего-правого - ПРЯМОЙ УГОЛ)
        path.lineTo(r, h);                          // нижняя линия
        path.arcTo(0, h - 2*r, 2*r, 2*r, 270, -90); // нижний-левый угол
        path.lineTo(0, r);                          // левая линия
        path.arcTo(0, 0, 2*r, 2*r, 180, -90);       // верхний-левый угол
        
    } else {
        // Входящее — прямой угол слева-снизу
        // Скруглены: верхний-левый, верхний-правый, нижний-правый
        
        path.moveTo(r, 0);                          // верхний-левый (после скругления)
        path.lineTo(w - r, 0);                      // верхняя линия
        path.arcTo(w - 2*r, 0, 2*r, 2*r, 90, -90);  // верхний-правый угол
        path.lineTo(w, h - r);                      // правая линия
        path.arcTo(w - 2*r, h - 2*r, 2*r, 2*r, 0, -90); // нижний-правый угол
        path.lineTo(0, h);                          // нижняя линия (до нижнего-левого - ПРЯМОЙ УГОЛ)
        path.lineTo(0, r);                          // левая линия
        path.arcTo(0, 0, 2*r, 2*r, 180, -90);       // верхний-левый угол
    }
    
    path.closeSubpath();
    return path;
}

QPainterPath MessageBubbleWidget::createBubblePathWithCorners(
    const QRectF& rect,
    const BubbleCorners& corners,
    qreal cornerRadius
) {
    qreal w = rect.width();
    qreal h = rect.height();
    qreal r = cornerRadius;      // Полное скругление (~18px)
    qreal rs = cornerRadius * 0.15;  // Маленькое скругление (~3px)
    
    // Вспомогательная лямбда для получения радиуса по стилю
    auto getRadius = [](CornerStyle style, qreal roundRadius, qreal smallRadius) -> qreal {
        switch (style) {
            case CornerStyle::Round: return roundRadius;
            case CornerStyle::Small: return smallRadius;
            case CornerStyle::Sharp: return 0;
        }
        return 0;
    };
    
    qreal rTL = getRadius(corners.topLeft, r, rs);
    qreal rTR = getRadius(corners.topRight, r, rs);
    qreal rBR = getRadius(corners.bottomRight, r, rs);
    qreal rBL = getRadius(corners.bottomLeft, r, rs);
    
    QPainterPath path;
    
    // Начинаем с верхнего-левого угла
    path.moveTo(rTL, 0);
    
    // Верхняя линия и верхний-правый угол
    path.lineTo(w - rTR, 0);
    if (rTR > 0) {
        path.arcTo(w - 2*rTR, 0, 2*rTR, 2*rTR, 90, -90);
    }
    
    // Правая линия и нижний-правый угол
    path.lineTo(w, h - rBR);
    if (rBR > 0) {
        path.arcTo(w - 2*rBR, h - 2*rBR, 2*rBR, 2*rBR, 0, -90);
    }
    
    // Нижняя линия и нижний-левый угол
    path.lineTo(rBL, h);
    if (rBL > 0) {
        path.arcTo(0, h - 2*rBL, 2*rBL, 2*rBL, 270, -90);
    }
    
    // Левая линия и верхний-левый угол (замыкание)
    path.lineTo(0, rTL);
    if (rTL > 0) {
        path.arcTo(0, 0, 2*rTL, 2*rTL, 180, -90);
    }
    
    path.closeSubpath();
    return path;
}

void MessageBubbleWidget::paintEvent(QPaintEvent* event) {
    Q_UNUSED(event);
    
    QPainter painter(this);
    painter.setRenderHint(QPainter::Antialiasing, true);
    painter.setRenderHint(QPainter::SmoothPixmapTransform, true);
    
    // Рисуем форму пузырька
    QPainterPath path = bubblePath();
    
    // Заливка
    painter.fillPath(path, backgroundColor_);
    
    // Обводка (если задана)
    if (borderColor_.alpha() > 0) {
        QPen pen(borderColor_, 1);
        painter.setPen(pen);
        painter.drawPath(path);
    }
}

// ============================================================================
// BubbleContainer
// ============================================================================

BubbleContainer::BubbleContainer(
    BubbleTailSide side,
    bool showTail,
    QWidget* parent
)
    : QWidget(parent)
    , side_(side)
    , showTail_(showTail)
    , backgroundColor_("#f0f0f0")
{
    setAttribute(Qt::WA_TranslucentBackground, true);
}

void BubbleContainer::setShowTail(bool show) {
    if (showTail_ != show) {
        showTail_ = show;
        updateMask();
    }
}

void BubbleContainer::setBackgroundColor(const QColor& color) {
    backgroundColor_ = color;
    setStyleSheet(QString("background-color: %1;").arg(color.name()));
}

void BubbleContainer::resizeEvent(QResizeEvent* event) {
    QWidget::resizeEvent(event);
    updateMask();
}

void BubbleContainer::updateMask() {
    QPainterPath path = MessageBubbleWidget::createBubblePath(
        rect(),
        side_,
        showTail_,
        18.0
    );
    
    QRegion region = QRegion(path.toFillPolygon().toPolygon());
    setMask(region);
}

} // namespace BarkFluff