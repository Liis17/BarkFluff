/**
 * @file SharedDocumentsListView.h
 * @brief Список общих документов
 */

#pragma once

#include <QWidget>
#include <QListWidget>
#include <QLabel>
#include <QList>
#include "Models/SharedMediaItem.h"

namespace BarkFluff {

/**
 * @brief Список документов с иконкой, именем, размером, датой
 */
class SharedDocumentsListView : public QWidget {
    Q_OBJECT

public:
    explicit SharedDocumentsListView(QWidget* parent = nullptr);
    
    void setItems(const QList<SharedMediaItem>& items);
    void setLoading(bool loading);
    void clear();
    
    QSize sizeHint() const override;

signals:
    void itemClicked(const SharedMediaItem& item, int index);
    void loadMoreRequested();

private slots:
    void onItemClicked(QListWidgetItem* listWidgetItem);
    void onScrollValueChanged(int value);

private:
    void setupUI();
    QString getFileExtensionIcon(const QString& fileName) const;

    QListWidget* listWidget_ = nullptr;
    QWidget* loadingWidget_ = nullptr;
    
    QList<SharedMediaItem> items_;
};

} // namespace BarkFluff