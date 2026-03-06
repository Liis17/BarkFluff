/**
 * @file SharedMediaSection.h
 * @brief Секция общих медиа в профиле с переключателем Медиа/Файлы
 */

#pragma once

#include <QWidget>
#include <QPushButton>
#include <QStackedWidget>
#include <QLabel>
#include "Models/SharedMediaItem.h"
#include "UI/Widgets/SharedMediaGridView.h"
#include "UI/Widgets/SharedDocumentsListView.h"

namespace BarkFluff {

/**
 * @brief Секция "Вложения" с табами Медиа/Файлы
 */
class SharedMediaSection : public QWidget {
    Q_OBJECT

public:
    explicit SharedMediaSection(QWidget* parent = nullptr);
    
    void setChatId(const QString& chatId);
    void setLoading(bool loading);
    void clear();
    
    // Методы для установки данных извне
    void setMediaItems(const QList<SharedMediaItem>& items, bool hasMore);
    void setDocumentItems(const QList<SharedMediaItem>& items, bool hasMore);
    void appendMediaItems(const QList<SharedMediaItem>& items, bool hasMore);
    void appendDocumentItems(const QList<SharedMediaItem>& items, bool hasMore);
    
    QString chatId() const { return chatId_; }
    QList<SharedMediaItem> mediaItems() const { return mediaItems_; }
    QList<SharedMediaItem> documentItems() const { return documentItems_; }
    
    QSize sizeHint() const override;

signals:
    void mediaItemClicked(const SharedMediaItem& item, int index);
    void documentItemClicked(const SharedMediaItem& item, int index);
    
    // Сигналы для запроса загрузки данных
    void loadMediaRequested(const QString& chatId, int offset, int size);
    void loadDocumentsRequested(const QString& chatId, int offset, int size);

private slots:
    void onMediaTabClicked();
    void onDocumentsTabClicked();
    void onLoadMoreMedia();
    void onLoadMoreDocuments();

private:
    void setupUI();
    void updateTabButtons();
    void loadMedia();
    void loadDocuments();

    QString chatId_;
    SharedMediaFilter currentFilter_ = SharedMediaFilter::Media;
    
    // UI Components - Header
    QLabel* titleLabel_ = nullptr;
    QPushButton* mediaTabButton_ = nullptr;
    QPushButton* documentsTabButton_ = nullptr;
    
    // UI Components - Content
    QStackedWidget* stackedWidget_ = nullptr;
    SharedMediaGridView* mediaGridView_ = nullptr;
    SharedDocumentsListView* documentsListView_ = nullptr;
    
    // Pagination state
    int mediaOffset_ = 0;
    int documentsOffset_ = 0;
    bool hasMoreMedia_ = false;
    bool hasMoreDocuments_ = false;
    bool isLoadingMedia_ = false;
    bool isLoadingDocuments_ = false;
    
    QList<SharedMediaItem> mediaItems_;
    QList<SharedMediaItem> documentItems_;
    
    static constexpr int kPageSize = 50;
};

} // namespace BarkFluff