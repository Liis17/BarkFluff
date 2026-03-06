/**
 * @file ScrollToBottomButton.h
 * @brief Кнопка прокрутки вниз с индикатором непрочитанных сообщений
 */

#pragma once

#include <QPushButton>
#include <QLabel>
#include <QPropertyAnimation>

namespace BarkFluff {

/**
 * @brief Кнопка прокрутки вниз с индикатором непрочитанных
 * 
 * Показывается когда пользователь прокрутил вверх.
 * Отображает количество непрочитанных сообщений.
 * Анимированно появляется/исчезает.
 */
class ScrollToBottomButton : public QPushButton {
    Q_OBJECT
    Q_PROPERTY(double opacity READ opacity WRITE setOpacity)
    
public:
    explicit ScrollToBottomButton(QWidget* parent = nullptr);
    
    /**
     * @brief Установить количество непрочитанных
     */
    void setUnreadCount(int count);
    
    /**
     * @brief Получить количество непрочитанных
     */
    int unreadCount() const { return unreadCount_; }
    
    /**
     * @brief Показать кнопку с анимацией
     */
    void showAnimated();
    
    /**
     * @brief Скрыть кнопку с анимацией
     */
    void hideAnimated();
    
    /**
     * @brief Видима ли кнопка (с учётом анимации)
     */
    bool isEffectivelyVisible() const;
    
    /**
     * @brief Получить текущую прозрачность
     */
    double opacity() const { return opacity_; }
    
    /**
     * @brief Установить прозрачность
     */
    void setOpacity(double value);
    
signals:
    /**
     * @brief Клик по кнопке
     */
    void clicked();
    
protected:
    void paintEvent(QPaintEvent* event) override;
    
private:
    void setupUI();
    void updateBadge();
    
    int unreadCount_ = 0;
    double opacity_ = 1.0;
    QLabel* badgeLabel_ = nullptr;
    QPropertyAnimation* showAnimation_ = nullptr;
    QPropertyAnimation* hideAnimation_ = nullptr;
    
    // Размеры
    static constexpr int buttonSize_ = 40;
    static constexpr int badgeSize_ = 20;
};

} // namespace BarkFluff