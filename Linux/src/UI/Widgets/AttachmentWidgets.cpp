/**
 * @file AttachmentWidgets.cpp
 * @brief Реализация виджетов для отображения вложений
 */

#include "AttachmentWidgets.h"
#include <QMouseEvent>
#include <QPixmap>
#include <QMovie>
#include <QStyle>
#include <QApplication>
#include <QScreen>
#include <QFileDialog>
#include <QDesktopServices>
#include <QUrl>
#include <QMimeDatabase>
#include <QPainter>
#include <QMediaPlayer>
#include <QVideoWidget>
#include <QAudioOutput>
#include <QStyleOption>
#include <QMessageBox>
#include <QProgressDialog>

namespace BarkFluff {

// ============================================================================
// ImageAttachmentWidget
// ============================================================================

ImageAttachmentWidget::ImageAttachmentWidget(const MessageAttachment& attachment,
                                             const QSize& maxSize,
                                             QWidget* parent)
    : QWidget(parent)
    , attachment_(attachment)
    , maxSize_(maxSize)
{
    auto* layout = new QVBoxLayout(this);
    layout->setContentsMargins(0, 0, 0, 0);
    
    imageLabel_ = new QLabel(this);
    imageLabel_->setAlignment(Qt::AlignCenter);
    imageLabel_->setScaledContents(false);
    imageLabel_->setStyleSheet("background-color: #f0f0f0; border-radius: 8px;");
    imageLabel_->setFixedSize(maxSize_);
    
    loadingLabel_ = new QLabel(tr("Загрузка..."), this);
    loadingLabel_->setAlignment(Qt::AlignCenter);
    loadingLabel_->setStyleSheet("color: #888;");
    
    layout->addWidget(imageLabel_);
    
    // Connect to file cache service
    auto* cacheService = FileCacheService::instance();
    connect(cacheService, &FileCacheService::fileCached,
            this, &ImageAttachmentWidget::onFileCached);
    
    // Load image
    loadImage();
    
    setCursor(Qt::PointingHandCursor);
}

QSize ImageAttachmentWidget::sizeHint() const {
    return maxSize_;
}

void ImageAttachmentWidget::mousePressEvent(QMouseEvent* event) {
    if (event->button() == Qt::LeftButton) {
        emit clicked();
    }
    QWidget::mousePressEvent(event);
}

void ImageAttachmentWidget::loadImage() {
    // Priority: previewFileID > fileID (как в WPF)
    QString effectiveFileId = !attachment_.previewFileId.isEmpty() 
        ? attachment_.previewFileId 
        : attachment_.fileId;
    
    auto* cacheService = FileCacheService::instance();
    QString localPath = cacheService->getCachedFilePath(
        effectiveFileId, 
        CachedFileType::Image, 
        attachment_.previewUrl
    );
    
    if (!localPath.isEmpty() && QFile::exists(localPath)) {
        QPixmap pixmap(localPath);
        if (!pixmap.isNull()) {
            QPixmap scaled = pixmap.scaled(maxSize_, Qt::KeepAspectRatio, Qt::SmoothTransformation);
            imageLabel_->setPixmap(scaled);
            imageLabel_->setFixedSize(scaled.size());
            imageLoaded_ = true;
            loadingLabel_->hide();
            return;
        }
    }
    
    // Show placeholder
    imageLabel_->setText("🖼");
    imageLabel_->setStyleSheet(
        "background-color: #e0e0e0; "
        "border-radius: 8px; "
        "font-size: 48px; "
        "color: #888;"
    );
}

void ImageAttachmentWidget::onFileCached(const QString& fileId, 
                                          const QString& localPath, 
                                          CachedFileType type) {
    QString effectiveFileId = !attachment_.previewFileId.isEmpty() 
        ? attachment_.previewFileId 
        : attachment_.fileId;
    
    if (fileId == effectiveFileId && !imageLoaded_) {
        QPixmap pixmap(localPath);
        if (!pixmap.isNull()) {
            QPixmap scaled = pixmap.scaled(maxSize_, Qt::KeepAspectRatio, Qt::SmoothTransformation);
            imageLabel_->setPixmap(scaled);
            imageLabel_->setFixedSize(scaled.size());
            imageLoaded_ = true;
        }
    }
}

// ============================================================================
// VideoAttachmentWidget
// ============================================================================

VideoAttachmentWidget::VideoAttachmentWidget(const MessageAttachment& attachment,
                                             const QSize& maxSize,
                                             QWidget* parent)
    : QWidget(parent)
    , attachment_(attachment)
    , maxSize_(maxSize)
{
    auto* layout = new QVBoxLayout(this);
    layout->setContentsMargins(0, 0, 0, 0);
    
    thumbnailLabel_ = new QLabel(this);
    thumbnailLabel_->setAlignment(Qt::AlignCenter);
    thumbnailLabel_->setStyleSheet("background-color: #2a2a2a; border-radius: 8px;");
    thumbnailLabel_->setFixedSize(maxSize_);
    
    // Play button overlay
    playButtonOverlay_ = new QWidget(this);
    playButtonOverlay_->setFixedSize(60, 60);
    playButtonOverlay_->setStyleSheet(
        "background-color: rgba(0, 0, 0, 0.5); "
        "border-radius: 30px;"
    );
    
    auto* overlayLayout = new QVBoxLayout(playButtonOverlay_);
    auto* playIcon = new QLabel("▶", playButtonOverlay_);
    playIcon->setAlignment(Qt::AlignCenter);
    playIcon->setStyleSheet("color: white; font-size: 24px;");
    playButtonOverlay_->setLayout(overlayLayout);
    overlayLayout->addWidget(playIcon);
    
    // Duration label
    durationLabel_ = new QLabel(this);
    durationLabel_->setStyleSheet(
        "background-color: rgba(0, 0, 0, 0.6); "
        "color: white; "
        "padding: 2px 6px; "
        "border-radius: 4px; "
        "font-size: 11px;"
    );
    durationLabel_->hide();
    
    layout->addWidget(thumbnailLabel_);
    
    loadThumbnail();
    setCursor(Qt::PointingHandCursor);
}

QSize VideoAttachmentWidget::sizeHint() const {
    return maxSize_;
}

void VideoAttachmentWidget::mousePressEvent(QMouseEvent* event) {
    if (event->button() == Qt::LeftButton) {
        emit clicked();
    }
    QWidget::mousePressEvent(event);
}

void VideoAttachmentWidget::loadThumbnail() {
    // Try to load preview URL
    if (!attachment_.previewUrl.isEmpty()) {
        // TODO: Load thumbnail from previewUrl
    }
    
    // Show placeholder with play icon
    thumbnailLabel_->setText("🎬");
    thumbnailLabel_->setStyleSheet(
        "background-color: #1a1a1a; "
        "border-radius: 8px; "
        "font-size: 48px; "
        "color: #888;"
    );
}

// ============================================================================
// GifAttachmentWidget
// ============================================================================

GifAttachmentWidget::GifAttachmentWidget(const MessageAttachment& attachment,
                                         const QSize& maxSize,
                                         QWidget* parent)
    : QWidget(parent)
    , attachment_(attachment)
    , maxSize_(maxSize)
{
    auto* layout = new QVBoxLayout(this);
    layout->setContentsMargins(0, 0, 0, 0);
    
    gifLabel_ = new QLabel(this);
    gifLabel_->setAlignment(Qt::AlignCenter);
    gifLabel_->setStyleSheet("background-color: #f0f0f0; border-radius: 8px;");
    gifLabel_->setFixedSize(maxSize_);
    
    // GIF badge
    gifBadge_ = new QLabel("GIF", this);
    gifBadge_->setStyleSheet(
        "background-color: rgba(0, 0, 0, 0.6); "
        "color: white; "
        "padding: 2px 6px; "
        "border-radius: 4px; "
        "font-size: 10px; "
        "font-weight: bold;"
    );
    gifBadge_->move(8, 8);
    gifBadge_->raise();
    
    layout->addWidget(gifLabel_);
    
    // Load GIF
    if (!attachment_.previewUrl.isEmpty()) {
        QMovie* movie = new QMovie(QUrl(attachment_.previewUrl).toString(), QByteArray(), this);
        if (movie->isValid()) {
            gifLabel_->setMovie(movie);
            movie->start();
        } else {
            gifLabel_->setText("GIF");
            gifLabel_->setStyleSheet(
                "background-color: #e0e0e0; "
                "border-radius: 8px; "
                "font-size: 24px; "
                "color: #888;"
            );
        }
    }
    
    setCursor(Qt::PointingHandCursor);
}

QSize GifAttachmentWidget::sizeHint() const {
    return maxSize_;
}

void GifAttachmentWidget::mousePressEvent(QMouseEvent* event) {
    if (event->button() == Qt::LeftButton) {
        emit clicked();
    }
    QWidget::mousePressEvent(event);
}

// ============================================================================
// DocumentAttachmentWidget
// ============================================================================

DocumentAttachmentWidget::DocumentAttachmentWidget(const MessageAttachment& attachment,
                                                   QWidget* parent)
    : QWidget(parent)
    , attachment_(attachment)
{
    setupUi();
}

void DocumentAttachmentWidget::setupUi() {
    auto* layout = new QHBoxLayout(this);
    layout->setContentsMargins(12, 8, 12, 8);
    layout->setSpacing(12);
    
    // Icon
    iconLabel_ = new QLabel(this);
    iconLabel_->setFixedSize(44, 44);
    iconLabel_->setAlignment(Qt::AlignCenter);
    iconLabel_->setText(fileIconName());
    iconLabel_->setStyleSheet(
        "background-color: #e8e8e8; "
        "border-radius: 8px; "
        "font-size: 24px;"
    );
    
    // File info
    auto* infoLayout = new QVBoxLayout();
    infoLayout->setSpacing(2);
    
    nameLabel_ = new QLabel(attachment_.fileName, this);
    nameLabel_->setStyleSheet("font-weight: 500;");
    nameLabel_->setWordWrap(true);
    nameLabel_->setMaximumWidth(200);
    
    sizeLabel_ = new QLabel(attachment_.formattedSize(), this);
    sizeLabel_->setStyleSheet("color: #888; font-size: 11px;");
    
    infoLayout->addWidget(nameLabel_);
    infoLayout->addWidget(sizeLabel_);
    
    // Download button
    downloadButton_ = new QPushButton(this);
    downloadButton_->setIcon(QApplication::style()->standardIcon(QStyle::SP_ArrowDown));
    downloadButton_->setFixedSize(32, 32);
    downloadButton_->setFlat(true);
    connect(downloadButton_, &QPushButton::clicked, this, &DocumentAttachmentWidget::downloadClicked);
    
    layout->addWidget(iconLabel_);
    layout->addLayout(infoLayout, 1);
    layout->addWidget(downloadButton_);
    
    // Container styling
    setStyleSheet(
        "DocumentAttachmentWidget {"
        "  background-color: #f5f5f5; "
        "  border-radius: 12px; "
        "  border: 1px solid #e0e0e0;"
        "}"
    );
    setMinimumHeight(60);
}

QString DocumentAttachmentWidget::fileIconName() const {
    QString ext = QFileInfo(attachment_.fileName).suffix().toLower();
    
    static QMap<QString, QString> iconMap = {
        {"pdf", "📕"},
        {"doc", "📘"}, {"docx", "📘"},
        {"xls", "📗"}, {"xlsx", "📗"},
        {"ppt", "📙"}, {"pptx", "📙"},
        {"zip", "📦"}, {"rar", "📦"}, {"7z", "📦"},
        {"mp3", "🎵"}, {"wav", "🎵"}, {"flac", "🎵"},
        {"txt", "📄"},
    };
    
    return iconMap.value(ext, "📄");
}

// ============================================================================
// AttachmentGridView
// ============================================================================

AttachmentGridView::AttachmentGridView(const QList<MessageAttachment>& attachments,
                                       QWidget* parent)
    : QWidget(parent)
    , attachments_(attachments)
{
    setupLayout();
}

void AttachmentGridView::setupLayout() {
    if (attachments_.isEmpty()) {
        return;
    }
    
    delete layout();
    
    int count = qMin(attachments_.size(), MaxDisplayed);
    
    switch (count) {
    case 1: {
        auto* layout = new QVBoxLayout(this);
        layout->setContentsMargins(0, 0, 0, 0);
        layout->setSpacing(Spacing);
        
        auto* widget = createAttachmentWidget(attachments_[0], QSize(MaxWidth, MaxHeight));
        layout->addWidget(widget);
        break;
    }
    case 2: {
        auto* layout = new QHBoxLayout(this);
        layout->setContentsMargins(0, 0, 0, 0);
        layout->setSpacing(Spacing);
        
        QSize size((MaxWidth - Spacing) / 2, MaxHeight / 2);
        for (int i = 0; i < 2; ++i) {
            auto* widget = createAttachmentWidget(attachments_[i], size);
            layout->addWidget(widget);
        }
        break;
    }
    case 3: {
        auto* layout = new QHBoxLayout(this);
        layout->setContentsMargins(0, 0, 0, 0);
        layout->setSpacing(Spacing);
        
        // Large on left
        auto* leftWidget = createAttachmentWidget(
            attachments_[0], 
            QSize((MaxWidth - Spacing) * 0.6, MaxHeight)
        );
        layout->addWidget(leftWidget);
        
        // Two small on right
        auto* rightLayout = new QVBoxLayout();
        rightLayout->setSpacing(Spacing);
        
        QSize rightSize((MaxWidth - Spacing) * 0.4, (MaxHeight - Spacing) / 2);
        for (int i = 1; i < 3; ++i) {
            auto* widget = createAttachmentWidget(attachments_[i], rightSize);
            rightLayout->addWidget(widget);
        }
        
        layout->addLayout(rightLayout);
        break;
    }
    default: {
        auto* layout = new QGridLayout(this);
        layout->setContentsMargins(0, 0, 0, 0);
        layout->setSpacing(Spacing);
        
        QSize size((MaxWidth - Spacing) / 2, MaxHeight / 2);
        
        for (int i = 0; i < count; ++i) {
            int row = i / 2;
            int col = i % 2;
            
            auto* widget = createAttachmentWidget(attachments_[i], size);
            
            if (i == 3 && attachments_.size() > MaxDisplayed) {
                // Add overlay for remaining count
                int remaining = attachments_.size() - MaxDisplayed;
                auto* overlayWidget = createOverlayCount(remaining);
                
                auto* container = new QWidget(this);
                auto* containerLayout = new QVBoxLayout(container);
                containerLayout->setContentsMargins(0, 0, 0, 0);
                containerLayout->addWidget(widget);
                container->setLayout(containerLayout);
                
                // Overlay on top
                overlayWidget->setParent(container);
                overlayWidget->setGeometry(widget->geometry());
                overlayWidget->show();
                
                layout->addWidget(container, row, col);
            } else {
                layout->addWidget(widget, row, col);
            }
        }
        break;
    }
    }
}

QWidget* AttachmentGridView::createAttachmentWidget(const MessageAttachment& attachment,
                                                     const QSize& size) {
    QWidget* widget = nullptr;
    
    switch (attachment.type) {
    case MessageAttachmentType::Image:
        widget = new ImageAttachmentWidget(attachment, size, this);
        break;
    case MessageAttachmentType::Video:
        widget = new VideoAttachmentWidget(attachment, size, this);
        break;
    case MessageAttachmentType::Gif:
        widget = new GifAttachmentWidget(attachment, size, this);
        break;
    case MessageAttachmentType::Document:
        widget = new DocumentAttachmentWidget(attachment);
        break;
    default:
        widget = new QLabel("?", this);
        widget->setStyleSheet("background-color: #e0e0e0; border-radius: 8px;");
        break;
    }
    
    // Connect click signal
    if (auto* imageWidget = qobject_cast<ImageAttachmentWidget*>(widget)) {
        connect(imageWidget, &ImageAttachmentWidget::clicked, this, [this, attachment]() {
            emit attachmentClicked(attachment);
        });
    } else if (auto* videoWidget = qobject_cast<VideoAttachmentWidget*>(widget)) {
        connect(videoWidget, &VideoAttachmentWidget::clicked, this, [this, attachment]() {
            emit attachmentClicked(attachment);
        });
    } else if (auto* gifWidget = qobject_cast<GifAttachmentWidget*>(widget)) {
        connect(gifWidget, &GifAttachmentWidget::clicked, this, [this, attachment]() {
            emit attachmentClicked(attachment);
        });
    }
    
    return widget;
}

QWidget* AttachmentGridView::createOverlayCount(int remaining) {
    auto* overlay = new QWidget(this);
    overlay->setStyleSheet("background-color: rgba(0, 0, 0, 0.6); border-radius: 8px;");
    
    auto* layout = new QVBoxLayout(overlay);
    auto* label = new QLabel(QString("+%1").arg(remaining), overlay);
    label->setAlignment(Qt::AlignCenter);
    label->setStyleSheet("color: white; font-size: 24px; font-weight: bold;");
    layout->addWidget(label);
    
    overlay->setCursor(Qt::PointingHandCursor);
    
    return overlay;
}

// ============================================================================
// MediaViewerDialog
// ============================================================================

MediaViewerDialog::MediaViewerDialog(const QList<MessageAttachment>& attachments,
                                     int initialIndex,
                                     QWidget* parent)
    : QDialog(parent)
    , attachments_(attachments)
    , currentIndex_(initialIndex)
{
    setupUi();
    showAttachment(currentIndex_);
    updateNavigation();
}

void MediaViewerDialog::setupUi() {
    setWindowTitle(tr("Просмотр медиа"));
    setWindowState(Qt::WindowMaximized);
    setStyleSheet("background-color: #1a1a1a;");
    
    auto* mainLayout = new QVBoxLayout(this);
    mainLayout->setContentsMargins(0, 0, 0, 0);
    mainLayout->setSpacing(0);
    
    // Header
    auto* headerLayout = new QHBoxLayout();
    headerLayout->setContentsMargins(16, 12, 16, 12);
    
    closeButton_ = new QPushButton("✕", this);
    closeButton_->setFixedSize(32, 32);
    closeButton_->setStyleSheet(
        "QPushButton { background-color: rgba(255,255,255,0.1); border-radius: 16px; "
        "color: white; font-size: 16px; }"
        "QPushButton:hover { background-color: rgba(255,255,255,0.2); }"
    );
    connect(closeButton_, &QPushButton::clicked, this, &QDialog::close);
    
    pageLabel_ = new QLabel(this);
    pageLabel_->setStyleSheet("color: white; font-size: 14px;");
    
    downloadButton_ = new QPushButton(tr("Скачать"), this);
    downloadButton_->setStyleSheet(
        "QPushButton { background-color: rgba(255,255,255,0.1); border-radius: 4px; "
        "color: white; padding: 6px 12px; }"
        "QPushButton:hover { background-color: rgba(255,255,255,0.2); }"
    );
    connect(downloadButton_, &QPushButton::clicked, this, &MediaViewerDialog::downloadCurrent);
    
    headerLayout->addWidget(closeButton_);
    headerLayout->addStretch();
    headerLayout->addWidget(pageLabel_);
    headerLayout->addStretch();
    headerLayout->addWidget(downloadButton_);
    
    mainLayout->addLayout(headerLayout);
    
    // Content area
    auto* contentLayout = new QHBoxLayout();
    
    prevButton_ = new QPushButton("‹", this);
    prevButton_->setFixedSize(48, 48);
    prevButton_->setStyleSheet(
        "QPushButton { background-color: rgba(255,255,255,0.1); border-radius: 24px; "
        "color: white; font-size: 24px; }"
        "QPushButton:hover { background-color: rgba(255,255,255,0.2); }"
        "QPushButton:disabled { background-color: transparent; color: #666; }"
    );
    connect(prevButton_, &QPushButton::clicked, this, &MediaViewerDialog::showPrevious);
    
    nextButton_ = new QPushButton("›", this);
    nextButton_->setFixedSize(48, 48);
    nextButton_->setStyleSheet(prevButton_->styleSheet());
    connect(nextButton_, &QPushButton::clicked, this, &MediaViewerDialog::showNext);
    
    imageViewer_ = new QLabel(this);
    imageViewer_->setAlignment(Qt::AlignCenter);
    imageViewer_->setStyleSheet("background-color: transparent;");
    
    videoWidget_ = new QVideoWidget(this);
    videoWidget_->setStyleSheet("background-color: black;");
    videoWidget_->hide();
    
    mediaPlayer_ = new QMediaPlayer(this);
    mediaPlayer_->setVideoOutput(videoWidget_);
    
    contentLayout->addWidget(prevButton_);
    contentLayout->addStretch();
    contentLayout->addWidget(imageViewer_);
    contentLayout->addWidget(videoWidget_);
    contentLayout->addStretch();
    contentLayout->addWidget(nextButton_);
    
    mainLayout->addLayout(contentLayout, 1);
    
    // Footer
    infoLabel_ = new QLabel(this);
    infoLabel_->setStyleSheet("color: #888; font-size: 12px;");
    infoLabel_->setAlignment(Qt::AlignCenter);
    infoLabel_->setContentsMargins(16, 8, 16, 12);
    
    mainLayout->addWidget(infoLabel_);
}

void MediaViewerDialog::showAttachment(int index) {
    if (index < 0 || index >= attachments_.size()) return;
    
    currentIndex_ = index;
    const auto& attachment = attachments_[index];
    
    // Hide both viewers
    imageViewer_->hide();
    videoWidget_->hide();
    mediaPlayer_->stop();
    
    // Clear previous content
    imageViewer_->clear();
    imageViewer_->setText(QString());
    
    qDebug() << "MediaViewerDialog::showAttachment - index:" << index
             << "fileName:" << attachment.fileName
             << "type:" << static_cast<int>(attachment.type)
             << "fileId:" << attachment.fileId
             << "previewFileId:" << attachment.previewFileId
             << "previewUrl:" << attachment.previewUrl;
    
    if (attachment.type == MessageAttachmentType::Image || 
        attachment.type == MessageAttachmentType::Gif) {
        
        // Определяем эффективный fileId и URL
        QString effectiveFileId = !attachment.previewFileId.isEmpty() 
            ? attachment.previewFileId 
            : attachment.fileId;
        QString effectiveUrl = attachment.previewUrl;
        
        qDebug() << "  effectiveFileId:" << effectiveFileId;
        qDebug() << "  effectiveUrl:" << effectiveUrl;
        
        // Показываем плейсхолдер
        imageViewer_->setText(tr("Загрузка..."));
        imageViewer_->setStyleSheet("color: #888; font-size: 16px; background-color: transparent;");
        imageViewer_->show();
        
        if (effectiveFileId.isEmpty() && effectiveUrl.isEmpty()) {
            qWarning() << "  No fileId and no URL - cannot load image";
            imageViewer_->setText(tr("Изображение недоступно"));
            imageViewer_->setStyleSheet("color: #f44336; font-size: 16px; background-color: transparent;");
            updateNavigation();
            updateInfo();
            return;
        }
        
        auto* cacheService = FileCacheService::instance();
        
        // Всегда используем асинхронную загрузку
        cacheService->getCachedFilePathAsync(
            effectiveFileId.isEmpty() ? effectiveUrl : effectiveFileId,
            CachedFileType::Image,
            effectiveUrl,
            [this, effectiveFileId](const QString& path) {
                qDebug() << "  async load completed, path:" << path;
                
                if (path.isEmpty()) {
                    QMetaObject::invokeMethod(this, [this]() {
                        imageViewer_->setText(tr("Не удалось загрузить"));
                        imageViewer_->setStyleSheet("color: #f44336; font-size: 16px; background-color: transparent;");
                    });
                    return;
                }
                
                if (!QFile::exists(path)) {
                    qDebug() << "  file does not exist:" << path;
                    QMetaObject::invokeMethod(this, [this]() {
                        imageViewer_->setText(tr("Файл не найден"));
                        imageViewer_->setStyleSheet("color: #f44336; font-size: 16px; background-color: transparent;");
                    });
                    return;
                }
                
                QPixmap pixmap(path);
                qDebug() << "  pixmap.isNull:" << pixmap.isNull() << "size:" << pixmap.size();
                
                if (!pixmap.isNull()) {
                    QMetaObject::invokeMethod(this, [this, pixmap]() {
                        QSize screenSize = this->size() - QSize(200, 200);
                        if (screenSize.width() < 100) screenSize.setWidth(800);
                        if (screenSize.height() < 100) screenSize.setHeight(600);
                        
                        QPixmap scaled = pixmap.scaled(screenSize, Qt::KeepAspectRatio, Qt::SmoothTransformation);
                        imageViewer_->setPixmap(scaled);
                        imageViewer_->setFixedSize(scaled.size());
                        imageViewer_->setStyleSheet("background-color: transparent;");
                    });
                } else {
                    QMetaObject::invokeMethod(this, [this]() {
                        imageViewer_->setText(tr("Ошибка загрузки изображения"));
                        imageViewer_->setStyleSheet("color: #f44336; font-size: 16px; background-color: transparent;");
                    });
                }
            }
        );
        
    } else if (attachment.type == MessageAttachmentType::Video) {
        // Show video player
        QString videoUrl = attachment.previewUrl;
        
        if (!videoUrl.isEmpty()) {
            mediaPlayer_->setSource(QUrl(videoUrl));
            videoWidget_->show();
            mediaPlayer_->play();
        } else {
            imageViewer_->setText(tr("Видео недоступно"));
            imageViewer_->setStyleSheet("color: #888; font-size: 16px; background-color: transparent;");
            imageViewer_->show();
        }
    } else {
        // Document or unknown type
        imageViewer_->setText(tr("Файл: %1").arg(attachment.fileName));
        imageViewer_->setStyleSheet("color: #888; font-size: 16px; background-color: transparent;");
        imageViewer_->show();
    }
    
    updateNavigation();
    updateInfo();
}

void MediaViewerDialog::showPrevious() {
    if (currentIndex_ > 0) {
        showAttachment(currentIndex_ - 1);
    }
}

void MediaViewerDialog::showNext() {
    if (currentIndex_ < attachments_.size() - 1) {
        showAttachment(currentIndex_ + 1);
    }
}

void MediaViewerDialog::downloadCurrent() {
    if (currentIndex_ < 0 || currentIndex_ >= attachments_.size()) return;
    
    const auto& attachment = attachments_[currentIndex_];
    
    QString savePath = QFileDialog::getSaveFileName(
        this,
        tr("Сохранить файл"),
        attachment.fileName
    );
    
    if (savePath.isEmpty()) return;
    
    qDebug() << "MediaViewerDialog::downloadCurrent - saving to:" << savePath;
    
    // Определяем тип файла для кэша
    CachedFileType cacheType = CachedFileType::Image;
    if (attachment.type == MessageAttachmentType::Video) {
        cacheType = CachedFileType::Video;
    } else if (attachment.type == MessageAttachmentType::Document) {
        cacheType = CachedFileType::Document;
    }
    
    // Получаем файл из кэша
    auto* cacheService = FileCacheService::instance();
    QString cachedPath = cacheService->getCachedFilePath(
        attachment.fileId,
        cacheType,
        attachment.previewUrl
    );
    
    if (!cachedPath.isEmpty() && QFile::exists(cachedPath)) {
        // Файл уже в кэше - копируем
        if (QFile::copy(cachedPath, savePath)) {
            qDebug() << "MediaViewerDialog::downloadCurrent - file copied from cache";
            
            // Создаём QMessageBox с явным светлым стилем
            QMessageBox msgBox;
            msgBox.setIcon(QMessageBox::Information);
            msgBox.setWindowTitle(tr("Сохранение"));
            msgBox.setText(tr("Файл сохранён"));
            msgBox.setInformativeText(savePath);
            msgBox.addButton(QMessageBox::Ok);
            msgBox.setStyleSheet(
                "QMessageBox { background-color: white; }"
                "QLabel { color: black; }"
                "QPushButton { "
                "  background-color: #2196F3; "
                "  color: white; "
                "  border: none; "
                "  border-radius: 4px; "
                "  padding: 8px 16px; "
                "  min-width: 80px; "
                "}"
                "QPushButton:hover { background-color: #1976D2; }"
            );
            msgBox.exec();
        } else {
            qWarning() << "MediaViewerDialog::downloadCurrent - failed to copy file";
            
            QMessageBox msgBox;
            msgBox.setIcon(QMessageBox::Warning);
            msgBox.setWindowTitle(tr("Ошибка"));
            msgBox.setText(tr("Не удалось сохранить файл"));
            msgBox.addButton(QMessageBox::Ok);
            msgBox.setStyleSheet(
                "QMessageBox { background-color: white; }"
                "QLabel { color: black; }"
                "QPushButton { "
                "  background-color: #f44336; "
                "  color: white; "
                "  border: none; "
                "  border-radius: 4px; "
                "  padding: 8px 16px; "
                "  min-width: 80px; "
                "}"
                "QPushButton:hover { background-color: #d32f2f; }"
            );
            msgBox.exec();
        }
    } else {
        // Файла нет в кэше - нужно скачать
        qDebug() << "MediaViewerDialog::downloadCurrent - file not in cache, downloading...";
        
        // Показываем прогресс
        auto* progressDlg = new QProgressDialog(tr("Скачивание..."), tr("Отмена"), 0, 100, this);
        progressDlg->setWindowModality(Qt::WindowModal);
        progressDlg->setValue(0);
        
        // Скачиваем асинхронно
        cacheService->getCachedFilePathAsync(
            attachment.fileId,
            cacheType,
            attachment.previewUrl,
            [this, savePath, progressDlg](const QString& localPath) {
                progressDlg->close();
                progressDlg->deleteLater();
                
                if (localPath.isEmpty() || !QFile::exists(localPath)) {
                    QMetaObject::invokeMethod(this, [this]() {
                        QMessageBox::warning(this, tr("Ошибка"), tr("Не удалось скачать файл"));
                    });
                    return;
                }
                
                // Копируем в выбранное место
                if (QFile::copy(localPath, savePath)) {
                    qDebug() << "MediaViewerDialog - file downloaded and saved";
                    QMetaObject::invokeMethod(this, [this, savePath]() {
                        QMessageBox::information(this, tr("Сохранение"), 
                            tr("Файл сохранён: %1").arg(savePath));
                    });
                } else {
                    QMetaObject::invokeMethod(this, [this]() {
                        QMessageBox::warning(this, tr("Ошибка"), tr("Не удалось сохранить файл"));
                    });
                }
            }
        );
    }
}

void MediaViewerDialog::updateNavigation() {
    prevButton_->setEnabled(currentIndex_ > 0);
    nextButton_->setEnabled(currentIndex_ < attachments_.size() - 1);
    
    pageLabel_->setText(QString("%1 / %2").arg(currentIndex_ + 1).arg(attachments_.size()));
}

void MediaViewerDialog::updateInfo() {
    if (currentIndex_ < 0 || currentIndex_ >= attachments_.size()) return;
    
    const auto& attachment = attachments_[currentIndex_];
    QString info = attachment.fileName;
    if (attachment.attachmentSize > 0) {
        info += " • " + attachment.formattedSize();
    }
    infoLabel_->setText(info);
}

// ============================================================================
// VideoPlayerDialog
// ============================================================================

VideoPlayerDialog::VideoPlayerDialog(const QString& videoUrl, QWidget* parent)
    : QDialog(parent)
    , videoUrl_(videoUrl)
{
    setupUi();
    
    if (!videoUrl_.isEmpty()) {
        mediaPlayer_->setSource(QUrl(videoUrl_));
        mediaPlayer_->play();
    }
}

VideoPlayerDialog::~VideoPlayerDialog() {
    if (mediaPlayer_) {
        mediaPlayer_->stop();
    }
}

void VideoPlayerDialog::setupUi() {
    setWindowTitle(tr("Видеоплеер"));
    resize(800, 600);
    setStyleSheet("background-color: black;");
    
    auto* layout = new QVBoxLayout(this);
    layout->setContentsMargins(0, 0, 0, 0);
    
    videoWidget_ = new QVideoWidget(this);
    layout->addWidget(videoWidget_);
    
    mediaPlayer_ = new QMediaPlayer(this);
    mediaPlayer_->setVideoOutput(videoWidget_);
}

// ============================================================================
// SelectedAttachmentPreview
// ============================================================================

SelectedAttachmentPreview::SelectedAttachmentPreview(const SelectedAttachment& attachment,
                                                     QWidget* parent)
    : QWidget(parent)
    , attachment_(attachment)
{
    setupUi();
    loadThumbnail();
}

void SelectedAttachmentPreview::setupUi() {
    setFixedSize(80, 100);
    
    auto* mainLayout = new QVBoxLayout(this);
    mainLayout->setContentsMargins(4, 4, 4, 4);
    mainLayout->setSpacing(4);
    
    // Thumbnail container
    auto* thumbnailContainer = new QWidget(this);
    thumbnailContainer->setFixedSize(72, 72);
    thumbnailContainer->setStyleSheet(
        "background-color: #e0e0e0; border-radius: 8px;"
    );
    
    auto* thumbnailLayout = new QVBoxLayout(thumbnailContainer);
    thumbnailLayout->setContentsMargins(0, 0, 0, 0);
    
    thumbnailLabel_ = new QLabel(thumbnailContainer);
    thumbnailLabel_->setAlignment(Qt::AlignCenter);
    thumbnailLabel_->setFixedSize(72, 72);
    thumbnailLayout->addWidget(thumbnailLabel_);
    
    // Remove button (overlay)
    removeButton_ = new QPushButton("×", thumbnailContainer);
    removeButton_->setFixedSize(20, 20);
    removeButton_->move(54, 2);
    removeButton_->setStyleSheet(
        "QPushButton {"
        "  background-color: rgba(0, 0, 0, 0.6);"
        "  color: white;"
        "  border: none;"
        "  border-radius: 10px;"
        "  font-size: 14px;"
        "  font-weight: bold;"
        "}"
        "QPushButton:hover {"
        "  background-color: rgba(244, 67, 54, 0.9);"
        "}"
    );
    removeButton_->raise();
    connect(removeButton_, &QPushButton::clicked, this, [this]() {
        emit removeRequested(attachment_.id);
    });
    
    mainLayout->addWidget(thumbnailContainer, 0, Qt::AlignCenter);
    
    // Progress bar (hidden by default)
    progressBar_ = new QProgressBar(this);
    progressBar_->setFixedSize(72, 4);
    progressBar_->setRange(0, 100);
    progressBar_->setValue(0);
    progressBar_->setTextVisible(false);
    progressBar_->setStyleSheet(
        "QProgressBar {"
        "  background-color: #e0e0e0;"
        "  border: none;"
        "  border-radius: 2px;"
        "}"
        "QProgressBar::chunk {"
        "  background-color: #2196F3;"
        "  border-radius: 2px;"
        "}"
    );
    progressBar_->hide();
    mainLayout->addWidget(progressBar_, 0, Qt::AlignCenter);
    
    // File name (truncated)
    QString displayName = attachment_.displayName();
    if (displayName.length() > 10) {
        displayName = displayName.left(8) + "…";
    }
    nameLabel_ = new QLabel(displayName, this);
    nameLabel_->setAlignment(Qt::AlignCenter);
    nameLabel_->setStyleSheet("font-size: 10px; color: #666;");
    mainLayout->addWidget(nameLabel_);
    
    setCursor(Qt::PointingHandCursor);
}

void SelectedAttachmentPreview::loadThumbnail() {
    qDebug() << "=== SelectedAttachmentPreview::loadThumbnail ===";
    qDebug() << "  displayName:" << attachment_.displayName();
    qDebug() << "  type:" << static_cast<int>(attachment_.type);
    qDebug() << "  url:" << attachment_.url.toString();
    qDebug() << "  url.scheme:" << attachment_.url.scheme();
    qDebug() << "  url.path:" << attachment_.url.path();
    qDebug() << "  url.isLocalFile:" << attachment_.url.isLocalFile();
    
    bool thumbnailLoaded = false;
    
    // Используем новый метод для получения локального пути
    QString localPath = attachment_.toLocalPath();
    qDebug() << "  toLocalPath():" << localPath;
    qDebug() << "  fileExtension:" << attachment_.fileExtension();
    qDebug() << "  isMedia:" << attachment_.isMedia();
    qDebug() << "  isImage:" << attachment_.isImage();
    qDebug() << "  isGif:" << attachment_.isGif();
    qDebug() << "  isVideo:" << attachment_.isVideo();
    
    if (attachment_.type == SelectedAttachmentType::ImageData) {
        // Image from clipboard
        qDebug() << "  Loading from ImageData, size:" << attachment_.imageData.size();
        QPixmap pixmap;
        if (pixmap.loadFromData(attachment_.imageData)) {
            thumbnailLabel_->setPixmap(createThumbnail(pixmap));
            thumbnailLoaded = true;
            qDebug() << "  ImageData thumbnail loaded successfully";
        } else {
            qWarning() << "  Failed to load ImageData";
        }
    } else if (attachment_.type == SelectedAttachmentType::FileUrl) {
        // File from disk
        if (localPath.isEmpty()) {
            qWarning() << "  Failed to get local path from URL";
        } else {
            qDebug() << "  Checking file exists:" << QFile::exists(localPath);
            
            if (attachment_.isImage()) {
                QPixmap pixmap(localPath);
                if (!pixmap.isNull()) {
                    thumbnailLabel_->setPixmap(createThumbnail(pixmap));
                    thumbnailLoaded = true;
                    qDebug() << "  Image thumbnail loaded successfully, pixmap size:" << pixmap.size();
                } else {
                    qWarning() << "  Failed to load pixmap from:" << localPath 
                               << "file exists:" << QFile::exists(localPath);
                }
            } else if (attachment_.isGif()) {
                // GIF - показываем как изображение (первый кадр)
                QPixmap pixmap(localPath);
                if (!pixmap.isNull()) {
                    thumbnailLabel_->setPixmap(createThumbnail(pixmap));
                    thumbnailLoaded = true;
                    qDebug() << "  GIF thumbnail loaded successfully";
                } else {
                    qWarning() << "  Failed to load GIF thumbnail from:" << localPath;
                }
            } else if (attachment_.isVideo()) {
                // Video - show play icon
                thumbnailLabel_->setText("▶");
                thumbnailLabel_->setStyleSheet(
                    "font-size: 28px; color: #666; background-color: #2a2a2a; border-radius: 8px;"
                );
                thumbnailLoaded = true;
                qDebug() << "  Video thumbnail (play icon)";
            }
        }
    }
    
    // Document or fallback if thumbnail loading failed
    if (!thumbnailLoaded) {
        QString icon = "📄";
        QString ext = attachment_.fileExtension();
        
        static QMap<QString, QString> iconMap = {
            {"pdf", "📕"},
            {"doc", "📘"}, {"docx", "📘"},
            {"xls", "📗"}, {"xlsx", "📗"},
            {"zip", "📦"}, {"rar", "📦"},
            {"mp3", "🎵"}, {"wav", "🎵"},
            {"gif", "🖼️"},
            {"jpg", "🖼️"}, {"jpeg", "🖼️"}, {"png", "🖼️"},
            {"mp4", "🎬"}, {"mov", "🎬"}, {"webm", "🎬"},
        };
        
        if (iconMap.contains(ext)) {
            icon = iconMap[ext];
        }
        
        thumbnailLabel_->setText(icon);
        thumbnailLabel_->setStyleSheet(
            "font-size: 28px; background-color: #f5f5f5; border-radius: 8px;"
        );
        qDebug() << "  Using fallback icon:" << icon << "for ext:" << ext;
    }
    
    qDebug() << "=== loadThumbnail completed, thumbnailLoaded:" << thumbnailLoaded << "===";
}

QPixmap SelectedAttachmentPreview::createThumbnail(const QPixmap& pixmap) const {
    if (pixmap.isNull()) return QPixmap();
    
    QSize targetSize(ThumbnailSize, ThumbnailSize);
    QPixmap scaled = pixmap.scaled(targetSize, Qt::KeepAspectRatioByExpanding, Qt::SmoothTransformation);
    
    // Crop to center if needed
    if (scaled.width() > ThumbnailSize || scaled.height() > ThumbnailSize) {
        int x = (scaled.width() - ThumbnailSize) / 2;
        int y = (scaled.height() - ThumbnailSize) / 2;
        scaled = scaled.copy(x, y, ThumbnailSize, ThumbnailSize);
    }
    
    return scaled;
}

void SelectedAttachmentPreview::setUploadProgress(double progress) {
    if (progress < 0.0) progress = 0.0;
    if (progress > 1.0) progress = 1.0;
    
    progressBar_->setValue(static_cast<int>(progress * 100));
    
    if (progress > 0.0 && progress < 1.0) {
        progressBar_->show();
    } else {
        progressBar_->hide();
    }
}

void SelectedAttachmentPreview::setUploading(bool uploading) {
    isUploading_ = uploading;
    removeButton_->setEnabled(!uploading);
    
    if (uploading) {
        removeButton_->setStyleSheet(
            "QPushButton {"
            "  background-color: rgba(0, 0, 0, 0.3);"
            "  color: #999;"
            "  border: none;"
            "  border-radius: 10px;"
            "  font-size: 14px;"
            "}"
        );
    } else {
        removeButton_->setStyleSheet(
            "QPushButton {"
            "  background-color: rgba(0, 0, 0, 0.6);"
            "  color: white;"
            "  border: none;"
            "  border-radius: 10px;"
            "  font-size: 14px;"
            "  font-weight: bold;"
            "}"
            "QPushButton:hover {"
            "  background-color: rgba(244, 67, 54, 0.9);"
            "}"
        );
    }
}

// ============================================================================
// AttachmentPreviewStrip
// ============================================================================

AttachmentPreviewStrip::AttachmentPreviewStrip(QWidget* parent)
    : QWidget(parent)
{
    setupUi();
    
    // Setup animations
    showAnimation_ = new QPropertyAnimation(this, "maximumHeight", this);
    showAnimation_->setDuration(200);
    showAnimation_->setEasingCurve(QEasingCurve::OutQuad);
    
    hideAnimation_ = new QPropertyAnimation(this, "maximumHeight", this);
    hideAnimation_->setDuration(200);
    hideAnimation_->setEasingCurve(QEasingCurve::InQuad);
    connect(hideAnimation_, &QPropertyAnimation::finished, this, [this]() {
        QWidget::hide();
    });
    
    // Initially hidden
    QWidget::hide();
    setMaximumHeight(0);
}

void AttachmentPreviewStrip::setupUi() {
    auto* mainLayout = new QVBoxLayout(this);
    mainLayout->setContentsMargins(8, 4, 8, 4);
    mainLayout->setSpacing(4);
    
    // Scroll area for attachments
    scrollArea_ = new QScrollArea(this);
    scrollArea_->setWidgetResizable(false);
    scrollArea_->setHorizontalScrollBarPolicy(Qt::ScrollBarAsNeeded);
    scrollArea_->setVerticalScrollBarPolicy(Qt::ScrollBarAlwaysOff);
    scrollArea_->setFixedHeight(115);
    scrollArea_->setStyleSheet(
        "QScrollArea {"
        "  background-color: transparent;"
        "  border: none;"
        "}"
        "QScrollBar::horizontal {"
        "  height: 6px;"
        "  background-color: #f0f0f0;"
        "  border-radius: 3px;"
        "}"
        "QScrollBar::handle::horizontal {"
        "  background-color: #ccc;"
        "  border-radius: 3px;"
        "  min-width: 20px;"
        "}"
    );
    
    container_ = new QWidget(scrollArea_);
    layout_ = new QHBoxLayout(container_);
    layout_->setContentsMargins(0, 0, 0, 0);
    layout_->setSpacing(8);
    layout_->addStretch();
    
    scrollArea_->setWidget(container_);
    mainLayout->addWidget(scrollArea_);
    
    // Separator line
    auto* separator = new QFrame(this);
    separator->setFrameShape(QFrame::HLine);
    separator->setStyleSheet("background-color: #e0e0e0;");
    separator->setFixedHeight(1);
    mainLayout->addWidget(separator);
    
    setStyleSheet("background-color: transparent;");
}

void AttachmentPreviewStrip::addAttachment(const SelectedAttachment& attachment) {
    qDebug() << "=== AttachmentPreviewStrip::addAttachment ===";
    qDebug() << "  attachment.id:" << attachment.id;
    qDebug() << "  attachment.displayName:" << attachment.displayName();
    qDebug() << "  attachment.type:" << static_cast<int>(attachment.type);
    qDebug() << "  attachment.url:" << attachment.url.toString();
    qDebug() << "  already contains:" << previews_.contains(attachment.id);
    
    if (previews_.contains(attachment.id)) {
        qDebug() << "  -> SKIPPING (already added)";
        return;  // Already added
    }
    
    attachments_.append(attachment);
    
    qDebug() << "  Creating SelectedAttachmentPreview...";
    auto* preview = new SelectedAttachmentPreview(attachment, container_);
    qDebug() << "  Created, connecting signals...";
    
    connect(preview, &SelectedAttachmentPreview::removeRequested,
            this, &AttachmentPreviewStrip::onRemoveRequested);
    
    // Insert before stretch
    int insertIndex = layout_->count() - 1;
    qDebug() << "  Inserting at index:" << insertIndex;
    layout_->insertWidget(insertIndex, preview);
    previews_[attachment.id] = preview;
    
    // Update container width and height
    int width = previews_.size() * 88 + 16;
    container_->setMinimumWidth(width);
    container_->setFixedSize(width, 110);  // Явно задаём размер контейнера
    scrollArea_->setMinimumWidth(qMin(width + 20, 400));  // Скролл с запасом
    qDebug() << "  Container width:" << width << "container size:" << container_->size();
    
    qDebug() << "  Calling updateVisibility()...";
    updateVisibility();
    
    // Принудительно обновляем отображение
    container_->update();
    scrollArea_->update();
    update();
    QApplication::processEvents();
    
    qDebug() << "  container_ size:" << container_->size() << "scrollArea_ size:" << scrollArea_->size();
    qDebug() << "  preview widget size:" << preview->size() << "visible:" << preview->isVisible();
    
    qDebug() << "  Emitting attachmentsChanged, count:" << attachments_.size();
    emit attachmentsChanged(attachments_);
    qDebug() << "=== addAttachment DONE ===";
}

void AttachmentPreviewStrip::addAttachments(const QList<SelectedAttachment>& attachments) {
    for (const auto& attachment : attachments) {
        addAttachment(attachment);
    }
}

void AttachmentPreviewStrip::removeAttachment(const QUuid& id) {
    if (!previews_.contains(id)) return;
    
    auto* preview = previews_.take(id);
    layout_->removeWidget(preview);
    preview->deleteLater();
    
    // Remove from list
    for (int i = 0; i < attachments_.size(); ++i) {
        if (attachments_[i].id == id) {
            attachments_.removeAt(i);
            break;
        }
    }
    
    // Update container width
    int width = previews_.size() * 88 + 16;
    container_->setMinimumWidth(width);
    
    updateVisibility();
    emit attachmentRemoved(id);
    emit attachmentsChanged(attachments_);
}

#include <execinfo.h>
#include <unistd.h>

void AttachmentPreviewStrip::clearAttachments() {
    qDebug() << "AttachmentPreviewStrip::clearAttachments - called";
    
    // Print backtrace
    void* array[10];
    size_t size = backtrace(array, 10);
    char** strings = backtrace_symbols(array, size);
    qDebug() << "Backtrace:";
    for (size_t i = 0; i < size; i++) {
        qDebug() << "  " << strings[i];
    }
    free(strings);
    
    for (auto* preview : previews_) {
        layout_->removeWidget(preview);
        preview->deleteLater();
    }
    
    previews_.clear();
    attachments_.clear();
    container_->setMinimumWidth(0);
    
    updateVisibility();
    emit attachmentsChanged(attachments_);
}

QList<SelectedAttachment> AttachmentPreviewStrip::attachments() const {
    return attachments_;
}

void AttachmentPreviewStrip::setUploadProgress(const QUuid& id, double progress) {
    if (previews_.contains(id)) {
        previews_[id]->setUploadProgress(progress);
        previews_[id]->setUploading(progress < 1.0);
    }
}

void AttachmentPreviewStrip::setVisibleAnimated(bool visible) {
    if (visible == isVisible()) return;
    
    if (visible) {
        QWidget::show();
        showAnimation_->setStartValue(0);
        showAnimation_->setEndValue(130);
        showAnimation_->start();
    } else {
        hideAnimation_->setStartValue(height());
        hideAnimation_->setEndValue(0);
        hideAnimation_->start();
    }
}

void AttachmentPreviewStrip::onRemoveRequested(const QUuid& id) {
    removeAttachment(id);
}

void AttachmentPreviewStrip::updateVisibility() {
    qDebug() << "AttachmentPreviewStrip::updateVisibility - isEmpty:" << isEmpty() 
             << "previews count:" << previews_.size();
    
    if (isEmpty()) {
        // Скрываем
        QWidget::hide();
        setMaximumHeight(0);
        setMinimumHeight(0);
    } else {
        // Показываем с фиксированной высотой
        setSizePolicy(QSizePolicy::Preferred, QSizePolicy::Fixed);
        setFixedHeight(130);
        QWidget::show();
        updateGeometry();
        
        qDebug() << "  -> Strip shown, size:" << size() << "visible:" << isVisible();
    }
}

} // namespace BarkFluff
