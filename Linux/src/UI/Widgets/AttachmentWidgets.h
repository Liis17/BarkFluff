/**
 * @file AttachmentWidgets.h
 * @brief Виджеты для отображения вложений в сообщениях и превью перед отправкой
 */

#pragma once

#include <QWidget>
#include <QLabel>
#include <QVBoxLayout>
#include <QHBoxLayout>
#include <QPushButton>
#include <QScrollArea>
#include <QDialog>
#include <QProgressBar>
#include <QPropertyAnimation>
#include "../../Models/MessageContent.h"
#include "../../Models/SelectedAttachment.h"
#include "../../Services/FileCacheService.h"

class QMediaPlayer;
class QVideoWidget;
class QVideoSink;

namespace BarkFluff {

/**
 * @brief Виджет для отображения изображения
 */
class ImageAttachmentWidget : public QWidget {
    Q_OBJECT
    
public:
    explicit ImageAttachmentWidget(const MessageAttachment& attachment,
                                   const QSize& maxSize = QSize(300, 300),
                                   QWidget* parent = nullptr);
    
    QSize sizeHint() const override;
    
signals:
    void clicked();
    
protected:
    void mousePressEvent(QMouseEvent* event) override;
    
private slots:
    void onFileCached(const QString& fileId, const QString& localPath, CachedFileType type);
    
private:
    void loadImage();
    
    MessageAttachment attachment_;
    QSize maxSize_;
    QLabel* imageLabel_;
    QLabel* loadingLabel_;
    bool imageLoaded_ = false;
};

/**
 * @brief Виджет для отображения видео с превью
 */
class VideoAttachmentWidget : public QWidget {
    Q_OBJECT
    
public:
    explicit VideoAttachmentWidget(const MessageAttachment& attachment,
                                   const QSize& maxSize = QSize(300, 200),
                                   QWidget* parent = nullptr);
    
    QSize sizeHint() const override;
    
signals:
    void clicked();
    
protected:
    void mousePressEvent(QMouseEvent* event) override;
    
private:
    void loadThumbnail();
    
    MessageAttachment attachment_;
    QSize maxSize_;
    QLabel* thumbnailLabel_;
    QWidget* playButtonOverlay_;
    QLabel* durationLabel_;
};

/**
 * @brief Виджет для отображения GIF
 */
class GifAttachmentWidget : public QWidget {
    Q_OBJECT
    
public:
    explicit GifAttachmentWidget(const MessageAttachment& attachment,
                                 const QSize& maxSize = QSize(300, 200),
                                 QWidget* parent = nullptr);
    
    QSize sizeHint() const override;
    
signals:
    void clicked();
    
protected:
    void mousePressEvent(QMouseEvent* event) override;
    
private:
    MessageAttachment attachment_;
    QSize maxSize_;
    QLabel* gifLabel_;
    QLabel* gifBadge_;
};

/**
 * @brief Виджет для отображения документа
 */
class DocumentAttachmentWidget : public QWidget {
    Q_OBJECT
    
public:
    explicit DocumentAttachmentWidget(const MessageAttachment& attachment,
                                      QWidget* parent = nullptr);
    
signals:
    void downloadClicked();
    void openClicked();
    
private:
    void setupUi();
    QString fileIconName() const;
    
    MessageAttachment attachment_;
    QLabel* iconLabel_;
    QLabel* nameLabel_;
    QLabel* sizeLabel_;
    QPushButton* downloadButton_;
};

/**
 * @brief Сетка для отображения нескольких вложений
 * 
 * Автоматически выбирает layout в зависимости от количества вложений:
 * - 1 вложение: на всю ширину
 * - 2 вложения: 2 колонки
 * - 3 вложения: 1 большое + 2 маленьких
 * - 4+ вложений: сетка 2x2 с индикатором "+N"
 */
class AttachmentGridView : public QWidget {
    Q_OBJECT
    
public:
    explicit AttachmentGridView(const QList<MessageAttachment>& attachments,
                                QWidget* parent = nullptr);
    
signals:
    void attachmentClicked(const MessageAttachment& attachment);
    
private:
    void setupLayout();
    QWidget* createAttachmentWidget(const MessageAttachment& attachment,
                                    const QSize& size);
    QWidget* createOverlayCount(int remaining);
    
    QList<MessageAttachment> attachments_;
    static constexpr int MaxDisplayed = 4;
    static constexpr int MaxWidth = 300;
    static constexpr int MaxHeight = 300;
    static constexpr int Spacing = 2;
};

/**
 * @brief Полноэкранный просмотрщик медиа
 */
class MediaViewerDialog : public QDialog {
    Q_OBJECT
    
public:
    explicit MediaViewerDialog(const QList<MessageAttachment>& attachments,
                               int initialIndex = 0,
                               QWidget* parent = nullptr);
    
private slots:
    void showPrevious();
    void showNext();
    void downloadCurrent();
    
private:
    void setupUi();
    void showAttachment(int index);
    void updateNavigation();
    void updateInfo();
    
    QList<MessageAttachment> attachments_;
    int currentIndex_;
    
    QLabel* imageViewer_;
    QWidget* videoPlayerWidget_;
    QMediaPlayer* mediaPlayer_;
    QVideoWidget* videoWidget_;
    
    QPushButton* prevButton_;
    QPushButton* nextButton_;
    QPushButton* downloadButton_;
    QPushButton* closeButton_;
    QLabel* infoLabel_;
    QLabel* pageLabel_;
};

/**
 * @brief Диалог видеоплеера
 */
class VideoPlayerDialog : public QDialog {
    Q_OBJECT
    
public:
    explicit VideoPlayerDialog(const QString& videoUrl, QWidget* parent = nullptr);
    ~VideoPlayerDialog();
    
private:
    void setupUi();
    
    QString videoUrl_;
    QMediaPlayer* mediaPlayer_;
    QVideoWidget* videoWidget_;
};

// ============================================================================
// Превью вложений перед отправкой
// ============================================================================

/**
 * @brief Виджет превью одного выбранного вложения
 * 
 * Показывает миниатюру, имя файла и кнопку удаления
 */
class SelectedAttachmentPreview : public QWidget {
    Q_OBJECT
    
public:
    explicit SelectedAttachmentPreview(const SelectedAttachment& attachment,
                                       QWidget* parent = nullptr);
    
    QUuid attachmentId() const { return attachment_.id; }
    
    void setUploadProgress(double progress);  // 0.0 - 1.0
    void setUploading(bool uploading);
    
signals:
    void removeRequested(const QUuid& id);
    
private:
    void setupUi();
    void loadThumbnail();
    QPixmap createThumbnail(const QPixmap& pixmap) const;
    
    SelectedAttachment attachment_;
    QLabel* thumbnailLabel_;
    QLabel* nameLabel_;
    QLabel* sizeLabel_;
    QPushButton* removeButton_;
    QProgressBar* progressBar_;
    bool isUploading_ = false;
    
    static constexpr int ThumbnailSize = 60;
};

/**
 * @brief Горизонтальная полоса с превью выбранных вложений
 * 
 * Показывает превью всех выбранных файлов перед отправкой.
 * Поддерживает скролл если много вложений.
 * 
 * Использование:
 * @code
 * auto* strip = new AttachmentPreviewStrip(this);
 * strip->addAttachment(SelectedAttachment::fromUrl(url));
 * connect(strip, &AttachmentPreviewStrip::attachmentsChanged, ...);
 * @endcode
 */
class AttachmentPreviewStrip : public QWidget {
    Q_OBJECT
    
public:
    explicit AttachmentPreviewStrip(QWidget* parent = nullptr);
    
    /// Добавить вложение
    void addAttachment(const SelectedAttachment& attachment);
    
    /// Добавить несколько вложений
    void addAttachments(const QList<SelectedAttachment>& attachments);
    
    /// Удалить вложение по ID
    void removeAttachment(const QUuid& id);
    
    /// Очистить все вложения
    void clearAttachments();
    
    /// Получить все вложения
    QList<SelectedAttachment> attachments() const;
    
    /// Количество вложений
    int count() const { return previews_.size(); }
    
    /// Пусто?
    bool isEmpty() const { return previews_.isEmpty(); }
    
    /// Показать/скрыть полосу (с анимацией)
    void setVisibleAnimated(bool visible);
    
public slots:
    /// Установить прогресс загрузки для конкретного вложения
    void setUploadProgress(const QUuid& id, double progress);
    
signals:
    void attachmentsChanged(const QList<SelectedAttachment>& attachments);
    void attachmentRemoved(const QUuid& id);
    
private slots:
    void onRemoveRequested(const QUuid& id);
    
private:
    void setupUi();
    void updateVisibility();
    
    QScrollArea* scrollArea_;
    QWidget* container_;
    QHBoxLayout* layout_;
    QMap<QUuid, SelectedAttachmentPreview*> previews_;
    QList<SelectedAttachment> attachments_;
    
    QPropertyAnimation* showAnimation_;
    QPropertyAnimation* hideAnimation_;
};

} // namespace BarkFluff
