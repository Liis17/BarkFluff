/**
 * @file GeneralSettingsWidget.h
 * @brief Виджет общих настроек
 */

#pragma once

#include <QWidget>

class QCheckBox;

namespace BarkFluff {

/**
 * @brief Виджет общих настроек приложения
 * 
 * Содержит:
 * - Настройки уведомлений
 */
class GeneralSettingsWidget : public QWidget {
    Q_OBJECT

public:
    explicit GeneralSettingsWidget(QWidget* parent = nullptr);

private slots:
    void onEnabledChanged(int state);
    void onPreviewChanged(int state);
    void onAvatarChanged(int state);

private:
    void setupUI();
    void loadSettings();
    void saveSettings();
    
    QCheckBox* enabledCheck_ = nullptr;
    QCheckBox* previewCheck_ = nullptr;
    QCheckBox* avatarCheck_ = nullptr;
};

} // namespace BarkFluff