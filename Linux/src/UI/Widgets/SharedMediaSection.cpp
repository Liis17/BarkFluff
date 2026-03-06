/**
 * @file SharedMediaSection.cpp
 * @brief Реализация секции общих медиа
 */

#include "SharedMediaSection.h"
#include "Connection/MessagesClient.h"

#include <QVBoxLayout>
#include <QHBoxLayout>
#include <QButtonGroup>

namespace BarkFluff {

SharedMediaSection::SharedMediaSection(QWidget* parent)
    : QWidget(parent)
{
    setupUI();
}

void SharedMediaSection::setupUI() {
    auto* mainLayout = new QVBoxLayout(this);
    mainLayout->setContentsMargins(0, 0, 0, 0);
    mainLayout->setSpacing(8);
    
    // Header with title
    auto* headerLayout = new QHBoxLayout();
    headerLayout->setContentsMargins(0, 0, 0, 0);
    headerLayout->setSpacing(8);
    
    titleLabel_ = new QLabel(tr("Вложения"));
    titleLabel_->setStyleSheet("font-weight: bold; font-size: 14px;");
    headerLayout->addWidget(titleLabel_);
    
    headerLayout->addStretch();
    mainLayout->addLayout(headerLayout);
    
    // Tab bar (segmented control)
    auto* tabBarLayout = new QHBoxLayout();
    tabBarLayout->setContentsMargins(0, 0, 0, 0);
    tabBarLayout->setSpacing(0);
    
    mediaTabButton_ = new QPushButton(tr("Медиа"));
    mediaTabButton_->setCheckable(true);
    mediaTabButton_->setChecked(true);
    mediaTabButton_->setFlat(true);
    mediaTabButton_->setCursor(Qt::PointingHandCursor);
    
    documentsTabButton_ = new QPushButton(tr("Файлы"));
    documentsTabButton_->setCheckable(true);
    documentsTabButton_->setFlat(true);
    documentsTabButton_->setCursor(Qt::PointingHandCursor);
    
    // Group buttons for exclusive selection
    auto* buttonGroup = new QButtonGroup(this);
    buttonGroup->addButton(mediaTabButton_, 0);
    buttonGroup->addButton(documentsTabButton_, 1);
    buttonGroup->setExclusive(true);
    
    tabBarLayout->addWidget(mediaTabButton_);
    tabBarLayout->addWidget(documentsTabButton_);
    tabBarLayout->addStretch();
    
    mainLayout->addLayout(tabBarLayout);
    
    // Stacked widget for content
    stackedWidget_ = new QStackedWidget();
    
    mediaGridView_ = new SharedMediaGridView();
    documentsListView_ = new SharedDocumentsListView();
    
    stackedWidget_->addWidget(mediaGridView_);
    stackedWidget_->addWidget(documentsListView_);
    
    mainLayout->addWidget(stackedWidget_);
    
    // Connect signals
    connect(mediaTabButton_, &QPushButton::clicked, this, &SharedMediaSection::onMediaTabClicked);
    connect(documentsTabButton_, &QPushButton::clicked, this, &SharedMediaSection::onDocumentsTabClicked);
    connect(mediaGridView_, &SharedMediaGridView::itemClicked, this, &SharedMediaSection::mediaItemClicked);
    connect(mediaGridView_, &SharedMediaGridView::loadMoreRequested, this, &SharedMediaSection::onLoadMoreMedia);
    connect(documentsListView_, &SharedDocumentsListView::itemClicked, this, &SharedMediaSection::documentItemClicked);
    connect(documentsListView_, &SharedDocumentsListView::loadMoreRequested, this, &SharedMediaSection::onLoadMoreDocuments);
    
    updateTabButtons();
}

void SharedMediaSection::setChatId(const QString& chatId) {
    chatId_ = chatId;
    clear();
    loadMedia();
}

void SharedMediaSection::setLoading(bool loading) {
    if (currentFilter_ == SharedMediaFilter::Media) {
        mediaGridView_->setLoading(loading);
    } else {
        documentsListView_->setLoading(loading);
    }
}

void SharedMediaSection::clear() {
    mediaItems_.clear();
    documentItems_.clear();
    mediaOffset_ = 0;
    documentsOffset_ = 0;
    hasMoreMedia_ = false;
    hasMoreDocuments_ = false;
    mediaGridView_->clear();
    documentsListView_->clear();
}

QSize SharedMediaSection::sizeHint() const {
    return QSize(280, 300);
}

void SharedMediaSection::onMediaTabClicked() {
    if (currentFilter_ != SharedMediaFilter::Media) {
        currentFilter_ = SharedMediaFilter::Media;
        stackedWidget_->setCurrentWidget(mediaGridView_);
        updateTabButtons();
        
        if (mediaItems_.isEmpty() && !chatId_.isEmpty()) {
            loadMedia();
        }
    }
}

void SharedMediaSection::onDocumentsTabClicked() {
    if (currentFilter_ != SharedMediaFilter::Documents) {
        currentFilter_ = SharedMediaFilter::Documents;
        stackedWidget_->setCurrentWidget(documentsListView_);
        updateTabButtons();
        
        if (documentItems_.isEmpty() && !chatId_.isEmpty()) {
            loadDocuments();
        }
    }
}

void SharedMediaSection::onLoadMoreMedia() {
    if (hasMoreMedia_ && !isLoadingMedia_) {
        loadMedia();
    }
}

void SharedMediaSection::onLoadMoreDocuments() {
    if (hasMoreDocuments_ && !isLoadingDocuments_) {
        loadDocuments();
    }
}

void SharedMediaSection::updateTabButtons() {
    QString activeStyle = "QPushButton { background-color: palette(highlight); color: palette(highlightedText); border-radius: 4px; padding: 6px 12px; }";
    QString inactiveStyle = "QPushButton { background-color: transparent; color: palette(text); border-radius: 4px; padding: 6px 12px; } QPushButton:hover { background-color: rgba(128, 128, 128, 50); }";
    
    if (currentFilter_ == SharedMediaFilter::Media) {
        mediaTabButton_->setStyleSheet(activeStyle);
        documentsTabButton_->setStyleSheet(inactiveStyle);
    } else {
        mediaTabButton_->setStyleSheet(inactiveStyle);
        documentsTabButton_->setStyleSheet(activeStyle);
    }
}

void SharedMediaSection::loadMedia() {
    if (chatId_.isEmpty() || isLoadingMedia_) {
        return;
    }
    
    isLoadingMedia_ = true;
    mediaGridView_->setLoading(true);
    
    // Emit signal to request data loading
    emit loadMediaRequested(chatId_, mediaOffset_, kPageSize);
}

void SharedMediaSection::loadDocuments() {
    if (chatId_.isEmpty() || isLoadingDocuments_) {
        return;
    }
    
    isLoadingDocuments_ = true;
    documentsListView_->setLoading(true);
    
    // Emit signal to request data loading
    emit loadDocumentsRequested(chatId_, documentsOffset_, kPageSize);
}

void SharedMediaSection::setMediaItems(const QList<SharedMediaItem>& items, bool hasMore) {
    mediaItems_ = items;
    hasMoreMedia_ = hasMore;
    mediaOffset_ = items.size();
    mediaGridView_->setItems(items);
    isLoadingMedia_ = false;
    mediaGridView_->setLoading(false);
}

void SharedMediaSection::setDocumentItems(const QList<SharedMediaItem>& items, bool hasMore) {
    documentItems_ = items;
    hasMoreDocuments_ = hasMore;
    documentsOffset_ = items.size();
    documentsListView_->setItems(items);
    isLoadingDocuments_ = false;
    documentsListView_->setLoading(false);
}

void SharedMediaSection::appendMediaItems(const QList<SharedMediaItem>& items, bool hasMore) {
    mediaItems_.append(items);
    hasMoreMedia_ = hasMore;
    mediaOffset_ = mediaItems_.size();
    mediaGridView_->setItems(mediaItems_);
    isLoadingMedia_ = false;
    mediaGridView_->setLoading(false);
}

void SharedMediaSection::appendDocumentItems(const QList<SharedMediaItem>& items, bool hasMore) {
    documentItems_.append(items);
    hasMoreDocuments_ = hasMore;
    documentsOffset_ = documentItems_.size();
    documentsListView_->setItems(documentItems_);
    isLoadingDocuments_ = false;
    documentsListView_->setLoading(false);
}

} // namespace BarkFluff