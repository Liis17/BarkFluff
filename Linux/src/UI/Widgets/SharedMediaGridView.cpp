/**
 * @file SharedMediaGridView.cpp
 * @brief Реализация сетки превью общих медиа файлов
 */

#include "SharedMediaGridView.h"
#include "Connection/FilesClient.h"
#include "Services/FileCacheService.h"

#include <QPainter>
#include <QPaintEvent>
#include <QNetworkAccessManager>
#include <QNetworkReply>
#include <QNetworkRequest>
#include <QScrollBar>
#include <QStyleOption>
#include <QApplication>
#include <QStyle>

namespace BarkFluff {

// ============================================================================
// SharedMediaGridView
// ============================================================================

SharedMediaGridView::SharedMediaGridView(QWidget* parent)
    : QWidget(parent)
{
    setupUI();
}

void SharedMediaGridView::setupUI() {
    auto* mainLayout = new QVBoxLayout(this);
    mainLayout->setContentsMargins(0, 0, 0, 0);
    mainLayout->setSpacing(0);
    
    // Scroll area
    scrollArea_ = new QScrollArea(this);
    scrollArea_->setWidgetResizable(true);
    scrollArea_->setHorizontalScrollBarPolicy(Qt::ScrollBarAlwaysOff);
    scrollArea_->setVerticalScrollBarPolicy(Qt::ScrollBarAsNeeded);
    scrollArea_->setFrameShape(QFrame::NoFrame);
    
    // Grid container
    gridContainer_ = new QWidget();
    gridLayout_ = new QGridLayout(gridContainer_);
    gridLayout_->setContentsMargins(0, 8, 0, 0);
    gridLayout_->setSpacing(kSpacing);
    
    scrollArea_->setWidget(gridContainer_);
    mainLayout->addWidget(scrollArea_);
    
    // Loading widget
    loadingWidget_ = new QWidget();
    auto* loadingLayout = new QHBoxLayout(loadingWidget_);
    auto* loadingLabel = new QLabel(tr("Загрузка..."));
    loadingLayout->addStretch();
    loadingLayout->addWidget(loadingLabel);
    loadingLayout->addStretch();
    loadingWidget_->hide();
    
    mainLayout->addWidget(loadingWidget_);
    
    // Connect scroll for pagination
    connect(scrollArea_->verticalScrollBar(), &QScrollBar::valueChanged, this, [this](int value) {
        if (value >= scrollArea_->verticalScrollBar()->maximum() - 50) {
            emit loadMoreRequested();
        }
    });
}

void SharedMediaGridView::setItems(const QList<SharedMediaItem>& items) {
    items_ = items;
    
    // Clear existing thumbnails
    for (auto* thumb : thumbnails_) {
        gridLayout_->removeWidget(thumb);
        thumb->deleteLater();
    }
    thumbnails_.clear();
    
    // Create new thumbnails
    int index = 0;
    for (const auto& item : items_) {
        auto* thumb = new SharedMediaThumbnail(item, index, this);
        connect(thumb, &SharedMediaThumbnail::clicked, this, &SharedMediaGridView::onThumbnailClicked);
        thumbnails_.append(thumb);
        index++;
    }
    
    updateLayout();
}

void SharedMediaGridView::setLoading(bool loading) {
    loadingWidget_->setVisible(loading);
}

void SharedMediaGridView::clear() {
    for (auto* thumb : thumbnails_) {
        gridLayout_->removeWidget(thumb);
        thumb->deleteLater();
    }
    thumbnails_.clear();
    items_.clear();
}

void SharedMediaGridView::updateLayout() {
    int columns = calculateColumns();
    int thumbSize = calculateThumbnailSize();
    
    // Remove all widgets from layout
    for (auto* thumb : thumbnails_) {
        gridLayout_->removeWidget(thumb);
    }
    
    // Add widgets in grid
    for (int i = 0; i < thumbnails_.size(); ++i) {
        int row = i / columns;
        int col = i % columns;
        thumbnails_[i]->setFixedSize(thumbSize, thumbSize);
        gridLayout_->addWidget(thumbnails_[i], row, col);
    }
    
    // Set row and column stretch
    for (int c = 0; c < columns; ++c) {
        gridLayout_->setColumnStretch(c, 1);
    }
}

int SharedMediaGridView::calculateColumns() const {
    int width = scrollArea_->viewport()->width();
    return qMax(3, width / kMinThumbnailSize);
}

int SharedMediaGridView::calculateThumbnailSize() const {
    int columns = calculateColumns();
    int width = scrollArea_->viewport()->width();
    int size = (width - (columns - 1) * kSpacing) / columns;
    return qBound(kMinThumbnailSize, size, kMaxThumbnailSize);
}

QSize SharedMediaGridView::sizeHint() const {
    return QSize(280, 200);
}

QSize SharedMediaGridView::minimumSizeHint() const {
    return QSize(200, 150);
}

void SharedMediaGridView::resizeEvent(QResizeEvent* event) {
    QWidget::resizeEvent(event);
    updateLayout();
}

void SharedMediaGridView::onThumbnailClicked() {
    auto* thumb = qobject_cast<SharedMediaThumbnail*>(sender());
    if (thumb) {
        emit itemClicked(thumb->item(), thumb->index());
    }
}

// ============================================================================
// SharedMediaThumbnail
// ============================================================================

SharedMediaThumbnail::SharedMediaThumbnail(const SharedMediaItem& item, int index, QWidget* parent)
    : QWidget(parent)
    , item_(item)
    , index_(index)
{
    setCursor(Qt::PointingHandCursor);
    setMouseTracking(true);
    
    loadPreview();
}

QSize SharedMediaThumbnail::sizeHint() const {
    return QSize(100, 100);
}

void SharedMediaThumbnail::paintEvent(QPaintEvent* event) {
    Q_UNUSED(event)
    
    QPainter painter(this);
    painter.setRenderHint(QPainter::Antialiasing);
    
    QRect rect = this->rect();
    
    // Background
    QColor bgColor = palette().color(QPalette::Window);
    painter.fillRect(rect, bgColor);
    
    // Pixmap or placeholder
    if (!pixmap_.isNull()) {
        // Scale pixmap to fill the rect while maintaining aspect ratio
        QPixmap scaled = pixmap_.scaled(rect.size(), Qt::KeepAspectRatioByExpanding, Qt::SmoothTransformation);
        
        // Center crop
        int x = (scaled.width() - rect.width()) / 2;
        int y = (scaled.height() - rect.height()) / 2;
        QRect targetRect(0, 0, rect.width(), rect.height());
        QRect sourceRect(x, y, rect.width(), rect.height());
        
        painter.drawPixmap(targetRect, scaled, sourceRect);
    } else {
        // Placeholder
        painter.fillRect(rect, bgColor.darker(105));
        
        QString icon = getPlaceholderIcon();
        QStyleOption opt;
        opt.initFrom(this);
        
        // Draw placeholder icon
        QIcon placeholderIcon = QApplication::style()->standardIcon(QStyle::SP_FileIcon);
        if (item_.type == MessageAttachmentType::Image) {
            placeholderIcon = QApplication::style()->standardIcon(QStyle::SP_FileIcon);
        } else if (item_.type == MessageAttachmentType::Video) {
            placeholderIcon = QApplication::style()->standardIcon(QStyle::SP_MediaPlay);
        }
        
        int iconSize = rect.width() / 3;
        QPixmap iconPixmap = placeholderIcon.pixmap(iconSize, iconSize);
        int iconX = (rect.width() - iconSize) / 2;
        int iconY = (rect.height() - iconSize) / 2;
        painter.drawPixmap(iconX, iconY, iconPixmap);
    }
    
    // Hover effect
    if (isHovering_) {
        painter.fillRect(rect, QColor(0, 0, 0, 30));
    }
    
    // Press effect
    if (isPressed_) {
        painter.fillRect(rect, QColor(0, 0, 0, 50));
    }
    
    // Video overlay - play icon
    if (item_.type == MessageAttachmentType::Video) {
        painter.fillRect(rect, QColor(0, 0, 0, 60));
        
        QIcon playIcon = QApplication::style()->standardIcon(QStyle::SP_MediaPlay);
        int iconSize = rect.width() / 3;
        QPixmap playPixmap = playIcon.pixmap(iconSize, iconSize);
        int iconX = (rect.width() - iconSize) / 2;
        int iconY = (rect.height() - iconSize) / 2;
        painter.drawPixmap(iconX, iconY, playPixmap);
    }
    
    // GIF label
    if (item_.type == MessageAttachmentType::Gif) {
        QString gifLabel = "GIF";
        QFont font = painter.font();
        font.setBold(true);
        font.setPointSize(8);
        painter.setFont(font);
        
        QFontMetrics fm(font);
        int textWidth = fm.horizontalAdvance(gifLabel) + 8;
        int textHeight = fm.height() + 4;
        
        QRect labelRect(4, rect.height() - textHeight - 4, textWidth, textHeight);
        painter.fillRect(labelRect, QColor(0, 0, 0, 150));
        painter.setPen(Qt::white);
        painter.drawText(labelRect, Qt::AlignCenter, gifLabel);
    }
}

void SharedMediaThumbnail::enterEvent(QEnterEvent* event) {
    Q_UNUSED(event)
    isHovering_ = true;
    update();
}

void SharedMediaThumbnail::leaveEvent(QEvent* event) {
    Q_UNUSED(event)
    isHovering_ = false;
    isPressed_ = false;
    update();
}

void SharedMediaThumbnail::mousePressEvent(QMouseEvent* event) {
    Q_UNUSED(event)
    isPressed_ = true;
    update();
}

void SharedMediaThumbnail::mouseReleaseEvent(QMouseEvent* event) {
    Q_UNUSED(event)
    if (isPressed_) {
        emit clicked();
    }
    isPressed_ = false;
    update();
}

void SharedMediaThumbnail::loadPreview() {
    // If we have a preview URL, load it
    if (!item_.previewUrl.isEmpty()) {
        auto* manager = new QNetworkAccessManager(this);
        QNetworkRequest request(QUrl(item_.previewUrl));
        request.setAttribute(QNetworkRequest::RedirectPolicyAttribute, QNetworkRequest::NoLessSafeRedirectPolicy);
        
        QNetworkReply* reply = manager->get(request);
        connect(reply, &QNetworkReply::finished, this, [this, reply, manager]() {
            manager->deleteLater();
            
            if (reply->error() == QNetworkReply::NoError) {
                QByteArray data = reply->readAll();
                pixmap_.loadFromData(data);
                update();
            }
            reply->deleteLater();
        });
    }
    // If we have a preview file ID, we could load from FilesClient
    // For now, just show placeholder
}

QString SharedMediaThumbnail::getPlaceholderIcon() const {
    switch (item_.type) {
    case MessageAttachmentType::Image:
        return "image";
    case MessageAttachmentType::Video:
        return "video";
    case MessageAttachmentType::Gif:
        return "gif";
    default:
        return "file";
    }
}

} // namespace BarkFluff