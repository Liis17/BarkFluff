/**
 * @file SharedMediaGridView.h
 * @brief Сетка превью общих медиа файлов
 */

#pragma once

#include <QWidget>
#include <QScrollArea>
#include <QGridLayout>
#include <QLabel>
#include <QList>
#include "Models/SharedMediaItem.h"

namespace BarkFluff {

class SharedMediaThumbnail;

/**
 * @brief Сетка медиа файлов (изображения, видео, GIF)
 */
class SharedMediaGridView : public QWidget {
    Q_OBJECT

public:
    explicit SharedMediaGridView(QWidget* parent = nullptr);
    
    void setItems(const QList<SharedMediaItem>& items);
    void setLoading(bool loading);
    void clear();
    
    QSize sizeHint() const override;
    QSize minimumSizeHint() const override;

signals:
    void itemClicked(const SharedMediaItem& item, int index);
    void loadMoreRequested();

protected:
    void resizeEvent(QResizeEvent* event) override;

private slots:
    void onThumbnailClicked();

private:
    void setupUI();
    void updateLayout();
    int calculateColumns() const;
    int calculateThumbnailSize() const;

    QScrollArea* scrollArea_ = nullptr;
    QWidget* gridContainer_ = nullptr;
    QGridLayout* gridLayout_ = nullptr;
    QWidget* loadingWidget_ = nullptr;
    
    QList<SharedMediaItem> items_;
    QList<SharedMediaThumbnail*> thumbnails_;
    
    static constexpr int kSpacing = 2;
    static constexpr int kMinThumbnailSize = 80;
    static constexpr int kMaxThumbnailSize = 150;
};

/**
 * @brief Одна ячейка-превью в сетке
 */
class SharedMediaThumbnail : public QWidget {
    Q_OBJECT

public:
    explicit SharedMediaThumbnail(const SharedMediaItem& item, int index, QWidget* parent = nullptr);
    
    QSize sizeHint() const override;
    
    const SharedMediaItem& item() const { return item_; }
    int index() const { return index_; }

signals:
    void clicked();

protected:
    void paintEvent(QPaintEvent* event) override;
    void enterEvent(QEnterEvent* event) override;
    void leaveEvent(QEvent* event) override;
    void mousePressEvent(QMouseEvent* event) override;
    void mouseReleaseEvent(QMouseEvent* event) override;

private:
    void loadPreview();
    QString getPlaceholderIcon() const;

    SharedMediaItem item_;
    int index_;
    QPixmap pixmap_;
    bool isLoading_ = false;
    bool isHovering_ = false;
    bool isPressed_ = false;
};

} // namespace BarkFluff