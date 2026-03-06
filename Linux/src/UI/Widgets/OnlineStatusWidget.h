/**
 * @file OnlineStatusWidget.h
 * @brief Виджет отображения статуса онлайн пользователя
 */

#pragma once

#include <QWidget>
#include <QLabel>
#include "Connection/OnlinerClient.h"

namespace BarkFluff {

/**
 * @brief Виджет-индикатор статуса онлайн
 * 
 * Показывает цветной кружок (зелёный/серый) с опциональным текстом.
 * Используется в списке чатов и заголовке разговора.
 */
class OnlineStatusWidget : public QWidget {
    Q_OBJECT
    
public:
    /**
     * @brief Размер индикатора
     */
    enum class Size {
        Small,   ///< 8px (для списка чатов)
        Medium,  ///< 12px (для заголовка)
        Large    ///< 16px (для профиля)
    };
    
    explicit OnlineStatusWidget(QWidget* parent = nullptr);
    explicit OnlineStatusWidget(Size size, QWidget* parent = nullptr);
    
    /**
     * @brief Установить статус
     */
    void setStatus(const UserOnlineStatusInfo& status);
    
    /**
     * @brief Установить статус напрямую
     */
    void setStatus(OnlineStatus status, const QDateTime& lastSeen = QDateTime());
    
    /**
     * @brief Получить текущий статус
     */
    OnlineStatus status() const { return status_; }
    
    /**
     * @brief Показывать ли текст статуса
     */
    void setShowText(bool show);
    
    /**
     * @brief Установить размер индикатора
     */
    void setSize(Size size);
    
    /**
     * @brief Размер виджета
     */
    QSize sizeHint() const override;
    
    /**
     * @brief Минимальный размер виджета
     */
    QSize minimumSizeHint() const override;
    
protected:
    void paintEvent(QPaintEvent* event) override;
    
private:
    void setupUI();
    void updateTooltip();
    
    OnlineStatus status_ = OnlineStatus::Unknown;
    QDateTime lastSeen_;
    Size size_ = Size::Small;
    bool showText_ = false;
    
    QLabel* textLabel_ = nullptr;
    
    // Размеры индикатора
    static constexpr int smallIndicatorSize_ = 8;
    static constexpr int mediumIndicatorSize_ = 12;
    static constexpr int largeIndicatorSize_ = 16;
};

} // namespace BarkFluff