/**
 * @file BadgeChipWidget.h
 * @brief Виджет чипа баджа пользователя
 */

#pragma once

#include <QWidget>
#include <QLabel>
#include "Models/User.h"

namespace BarkFluff {

/**
 * @brief Виджет чипа баджа — иконка + название
 * 
 * Скруглённый чип с иконкой баджа и названием.
 * Tooltip показывает описание баджа.
 */
class BadgeChipWidget : public QWidget {
    Q_OBJECT
    
public:
    explicit BadgeChipWidget(const UserBadge& badge, QWidget* parent = nullptr);
    
    QSize sizeHint() const override;
    QSize minimumSizeHint() const override;
    
private:
    void setupUI();
    void loadIcon();
    
    UserBadge badge_;
    QLabel* iconLabel_ = nullptr;
    QLabel* nameLabel_ = nullptr;
    QLabel* descriptionLabel_ = nullptr;  // Описание справа от названия
};

} // namespace BarkFluff