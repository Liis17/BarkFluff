#include "AvatarWidget.h"
#include "Services/FileCacheService.h"
#include <QPainter>
#include <QPainterPath>
#include <QMouseEvent>
#include <QDebug>

namespace BarkFluff {

AvatarWidget::AvatarWidget(QWidget* parent)
    : QWidget(parent)
    , networkManager_(new QNetworkAccessManager(this))
{
    setFixedSize(size_, size_);
    setCursor(Qt::PointingHandCursor);
}

AvatarWidget::AvatarWidget(const QString& imageUrl, const QString& initials, int size, QWidget* parent)
    : QWidget(parent)
    , size_(size)
    , imageUrl_(imageUrl)
    , initials_(initials)
    , networkManager_(new QNetworkAccessManager(this))
{
    setFixedSize(size_, size_);
    setCursor(Qt::PointingHandCursor);
    loadAvatar();
}

AvatarWidget::~AvatarWidget() = default;

void AvatarWidget::setSize(int size) {
    if (size_ != size && size > 0) {
        size_ = size;
        setFixedSize(size_, size_);
        update();
        emit sizeChanged(size);
    }
}

void AvatarWidget::setImageUrl(const QString& url) {
    if (imageUrl_ != url) {
        imageUrl_ = url;
        avatarPixmap_ = QPixmap();
        loadAvatar();
        emit imageUrlChanged(url);
    }
}

void AvatarWidget::setInitials(const QString& initials) {
    if (initials_ != initials) {
        initials_ = initials;
        update();
        emit initialsChanged(initials);
    }
}

void AvatarWidget::setIsOnline(bool online) {
    if (isOnline_ != online) {
        isOnline_ = online;
        update();
        emit isOnlineChanged(online);
    }
}

void AvatarWidget::setShowOnlineIndicator(bool show) {
    if (showOnlineIndicator_ != show) {
        showOnlineIndicator_ = show;
        update();
        emit showOnlineIndicatorChanged(show);
    }
}

void AvatarWidget::setUseCacheService(bool use) {
    qDebug() << "AvatarWidget::setUseCacheService - setting to:" << use << "was:" << useCacheService_;
    if (useCacheService_ != use) {
        useCacheService_ = use;
    }
}

QSize AvatarWidget::sizeHint() const {
    return QSize(size_, size_);
}

QSize AvatarWidget::minimumSizeHint() const {
    return QSize(size_, size_);
}

void AvatarWidget::paintEvent(QPaintEvent* event) {
    Q_UNUSED(event)
    
    QPainter painter(this);
    painter.setRenderHint(QPainter::Antialiasing);
    painter.setRenderHint(QPainter::SmoothPixmapTransform);
    
    QRectF rect(0, 0, size_, size_);
    
    // Draw avatar (image or placeholder)
    if (!avatarPixmap_.isNull()) {
        // Draw clipped image
        QPainterPath clipPath;
        clipPath.addEllipse(rect);
        painter.setClipPath(clipPath);
        
        QPixmap scaled = avatarPixmap_.scaled(size_, size_, Qt::KeepAspectRatioByExpanding, Qt::SmoothTransformation);
        painter.drawPixmap(rect, scaled, QRectF(scaled.rect()));
        
        painter.setClipping(false);
    } else {
        // Draw placeholder with initials
        QColor bgColor = initialsColor();
        painter.setBrush(bgColor);
        painter.setPen(Qt::NoPen);
        painter.drawEllipse(rect);
        
        // Draw initials
        QColor textColor = bgColor.lighter(140);
        painter.setPen(textColor);
        
        QFont font = painter.font();
        font.setPixelSize(static_cast<int>(size_ * 0.4));
        font.setWeight(QFont::Medium);
        painter.setFont(font);
        
        painter.drawText(rect, Qt::AlignCenter, initials_);
    }
    
    // Draw online indicator
    if (showOnlineIndicator_ && isOnline_) {
        qreal indSize = indicatorSize();
        qreal borderWidth = indicatorBorderWidth();
        
        QPointF center(size_ - indSize / 2 - 1, size_ - indSize / 2 - 1);
        
        // Background (white border)
        painter.setBrush(palette().window().color());
        painter.setPen(Qt::NoPen);
        painter.drawEllipse(center, indSize / 2 + borderWidth, indSize / 2 + borderWidth);
        
        // Green dot
        painter.setBrush(QColor(76, 175, 80));  // Green
        painter.drawEllipse(center, indSize / 2, indSize / 2);
    }
}

void AvatarWidget::mousePressEvent(QMouseEvent* event) {
    if (event->button() == Qt::LeftButton) {
        emit clicked();
    }
    QWidget::mousePressEvent(event);
}

void AvatarWidget::loadAvatar() {
    qDebug() << "AvatarWidget::loadAvatar - imageUrl:" << imageUrl_ << "isLoading:" << isLoading_ << "useCache:" << useCacheService_;
    
    if (imageUrl_.isEmpty() || isLoading_) {
        return;
    }
    
    isLoading_ = true;
    
    // Если включён FileCacheService - используем его (асинхронно)
    if (useCacheService_) {
        qDebug() << "AvatarWidget::loadAvatar - using FileCacheService for:" << imageUrl_;
        auto* cacheService = FileCacheService::instance();
        
        cacheService->getCachedFilePathAsync(
            imageUrl_,
            CachedFileType::Avatar,
            imageUrl_,  // URL может быть полным или просто ID
            [this](const QString& localPath) {
                isLoading_ = false;
                
                if (localPath.isEmpty()) {
                    qWarning() << "AvatarWidget: Failed to get cached avatar path";
                    return;
                }
                
                QPixmap pixmap(localPath);
                if (!pixmap.isNull()) {
                    avatarPixmap_ = pixmap;
                    QMetaObject::invokeMethod(this, "update", Qt::QueuedConnection);
                } else {
                    qWarning() << "AvatarWidget: Failed to load pixmap from:" << localPath;
                }
            }
        );
    } else {
        // Стандартная загрузка через HTTP
        QNetworkRequest request{QUrl(imageUrl_)};
        request.setAttribute(QNetworkRequest::RedirectPolicyAttribute, QNetworkRequest::NoLessSafeRedirectPolicy);
        
        QNetworkReply* reply = networkManager_->get(request);
        connect(reply, &QNetworkReply::finished, this, [this, reply]() {
            onImageDownloaded(reply);
        });
    }
}

void AvatarWidget::onImageDownloaded(QNetworkReply* reply) {
    isLoading_ = false;
    reply->deleteLater();
    
    if (reply->error() == QNetworkReply::NoError) {
        QByteArray data = reply->readAll();
        avatarPixmap_.loadFromData(data);
        update();
    } else {
        qWarning() << "AvatarWidget: Failed to load avatar:" << reply->errorString();
    }
}

QColor AvatarWidget::initialsColor() const {
    // Generate color based on initials hash
    static const QList<QColor> colors = {
        QColor(66, 133, 244),   // Blue
        QColor(52, 168, 83),    // Green
        QColor(251, 188, 5),    // Yellow
        QColor(234, 67, 53),    // Red
        QColor(103, 58, 183),   // Purple
        QColor(0, 172, 193),    // Cyan
        QColor(255, 112, 67),   // Orange
        QColor(156, 39, 176),   // Magenta
        QColor(0, 150, 136),    // Teal
        QColor(121, 85, 72),    // Brown
    };
    
    int hash = 0;
    for (const QChar& c : initials_) {
        hash += c.unicode();
    }
    return colors[qAbs(hash) % colors.size()];
}

int AvatarWidget::indicatorSize() const {
    // Proportional to avatar size, min 8px
    return qMax(8, static_cast<int>(size_ * 0.25));
}

int AvatarWidget::indicatorBorderWidth() const {
    return size_ > 40 ? 2 : 1;
}

} // namespace BarkFluff