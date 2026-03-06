/**
 * @file AboutWidget.h
 * @brief Виджет информации о сервере и приложении
 */

#pragma once

#include <QWidget>
#include <QLabel>
#include "Models/ServerConfiguration.h"

namespace BarkFluff {

/**
 * @brief Виджет с информацией о сервере и приложении
 */
class AboutWidget : public QWidget {
    Q_OBJECT

public:
    explicit AboutWidget(
        const ServerConfiguration& config,
        QWidget* parent = nullptr
    );

private:
    void setupUI();

    ServerConfiguration config_;
};

} // namespace BarkFluff