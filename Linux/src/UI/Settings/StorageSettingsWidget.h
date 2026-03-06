/**
 * @file StorageSettingsWidget.h
 * @brief Виджет управления хранилищем и кешем
 */

#pragma once

#include <QWidget>
#include <QLabel>
#include <QPushButton>

namespace BarkFluff {

/**
 * @brief Виджет для управления хранилищем и очистки кеша
 */
class StorageSettingsWidget : public QWidget {
    Q_OBJECT

public:
    explicit StorageSettingsWidget(QWidget* parent = nullptr);

    void updateStorageInfo();

private slots:
    void onClearCacheClicked();

private:
    void setupUI();
    QString formatSize(qint64 bytes) const;

    QLabel* cacheSizeLabel_ = nullptr;
    QPushButton* clearCacheBtn_ = nullptr;
    QLabel* statusLabel_ = nullptr;
};

} // namespace BarkFluff