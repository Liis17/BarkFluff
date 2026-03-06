/**
 * @file MessageBubbleShape.h
 * @brief Форма пузырька сообщения с хвостиком в стиле iMessage
 */

#pragma once

#include <QWidget>
#include <QPainterPath>
#include <QPainter>

namespace BarkFluff {

/**
 * @brief Сторона хвостика пузырька
 */
enum class BubbleTailSide {
    Left,   ///< Входящее сообщение
    Right   ///< Исходящее сообщение
};

/**
 * @brief Стиль скругления угла
 */
enum class CornerStyle {
    Sharp,  ///< Острый угол (без скругления)
    Small,  ///< Маленькое скругление (~3px)
    Round   ///< Полное скругление (~18px)
};

/**
 * @brief Настройки скругления углов пузырька
 */
struct BubbleCorners {
    CornerStyle topLeft = CornerStyle::Round;      ///< Верхний-левый угол
    CornerStyle topRight = CornerStyle::Round;     ///< Верхний-правый угол
    CornerStyle bottomLeft = CornerStyle::Round;   ///< Нижний-левый угол
    CornerStyle bottomRight = CornerStyle::Round;  ///< Нижний-правый угол
    
    BubbleCorners() = default;
    BubbleCorners(CornerStyle tl, CornerStyle tr, CornerStyle bl, CornerStyle br)
        : topLeft(tl), topRight(tr), bottomLeft(bl), bottomRight(br) {}
    
    // Вспомогательные конструкторы для совместимости
    static CornerStyle R() { return CornerStyle::Round; }
    static CornerStyle S() { return CornerStyle::Small; }
    
    // Позиция в группе сообщений (для входящих - хвостик слева)
    // Внешние углы (правые) всегда скруглены, внутренние - маленькое скругление
    static BubbleCorners incomingFirst()   { return {R(), R(), S(), R()}; } // Первое в группе
    static BubbleCorners incomingMiddle()  { return {S(), R(), S(), R()}; } // Среднее
    static BubbleCorners incomingLast()    { return {S(), R(), S(), R()}; } // Последнее (с хвостиком)
    static BubbleCorners incomingOnly()    { return {R(), R(), S(), R()}; } // Единственное (с хвостиком)
    
    // Позиция в группе сообщений (для исходящих - хвостик справа)
    // Внешние углы (левые) всегда скруглены, внутренние - маленькое скругление
    static BubbleCorners outgoingFirst()   { return {R(), R(), R(), S()}; } // Первое в группе
    static BubbleCorners outgoingMiddle()  { return {R(), S(), R(), S()}; } // Среднее
    static BubbleCorners outgoingLast()    { return {R(), S(), R(), S()}; } // Последнее (с хвостиком)
    static BubbleCorners outgoingOnly()    { return {R(), R(), R(), S()}; } // Единственное (с хвостиком)
};

/**
 * @brief Виджет-контейнер с формой пузырька сообщения
 * 
 * Рисует закруглённый прямоугольник с S-образным хвостиком
 * в стиле iMessage. Хвостик показывается только для последнего
 * сообщения в группе.
 */
class MessageBubbleWidget : public QWidget {
    Q_OBJECT
    
public:
    /**
     * @brief Конструктор
     * @param side Сторона хвостика
     * @param showTail Показывать ли хвостик
     * @param parent Родительский виджет
     */
    explicit MessageBubbleWidget(
        BubbleTailSide side,
        bool showTail = true,
        QWidget* parent = nullptr
    );
    
    /**
     * @brief Установить цвет фона
     */
    void setBackgroundColor(const QColor& color);
    
    /**
     * @brief Установить показ хвостика
     */
    void setShowTail(bool show);
    
    /**
     * @brief Получить путь формы для clipping
     */
    QPainterPath bubblePath() const;
    
    /**
     * @brief Получить отступ для хвостика
     */
    int tailOffset() const;
    
    /**
     * @brief Статический метод для получения пути формы
     * @param rect Прямоугольник
     * @param side Сторона хвостика
     * @param showTail Показывать ли хвостик
     * @param cornerRadius Радиус скругления
     * @return QPainterPath с формой пузырька
     */
    static QPainterPath createBubblePath(
        const QRectF& rect,
        BubbleTailSide side,
        bool showTail,
        qreal cornerRadius = 18.0
    );
    
    /**
     * @brief Статический метод для получения пути формы с кастомными углами
     * @param rect Прямоугольник
     * @param corners Настройки скругления углов
     * @param cornerRadius Радиус скругления
     * @return QPainterPath с формой пузырька
     */
    static QPainterPath createBubblePathWithCorners(
        const QRectF& rect,
        const BubbleCorners& corners,
        qreal cornerRadius = 18.0
    );
    
protected:
    void paintEvent(QPaintEvent* event) override;
    
private:
    BubbleTailSide side_;
    bool showTail_;
    QColor backgroundColor_;
    QColor borderColor_;
    
    static constexpr qreal cornerRadius_ = 18.0;
    static constexpr int tailWidth_ = 6;  // Ширина хвостика (выход за rect)
};

/**
 * @brief Виджет содержимого с формой пузырька
 * 
 * Контейнер для контента сообщения (текст, вложения)
 * с автоматическим применением формы пузырька.
 */
class BubbleContainer : public QWidget {
    Q_OBJECT
    
public:
    explicit BubbleContainer(
        BubbleTailSide side,
        bool showTail,
        QWidget* parent = nullptr
    );
    
    void setShowTail(bool show);
    void setBackgroundColor(const QColor& color);
    
protected:
    void resizeEvent(QResizeEvent* event) override;
    
private:
    void updateMask();
    
    BubbleTailSide side_;
    bool showTail_;
    QColor backgroundColor_;
};

} // namespace BarkFluff