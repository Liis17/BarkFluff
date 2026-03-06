/**
 * @file SharedDocumentsListView.cpp
 * @brief Реализация списка общих документов
 */

#include "SharedDocumentsListView.h"

#include <QVBoxLayout>
#include <QHBoxLayout>
#include <QListWidgetItem>
#include <QFileIconProvider>
#include <QScrollBar>
#include <QStyleOption>
#include <QApplication>
#include <QStyle>
#include <QFileInfo>

namespace BarkFluff {

SharedDocumentsListView::SharedDocumentsListView(QWidget* parent)
    : QWidget(parent)
{
    setupUI();
}

void SharedDocumentsListView::setupUI() {
    auto* mainLayout = new QVBoxLayout(this);
    mainLayout->setContentsMargins(0, 0, 0, 0);
    mainLayout->setSpacing(0);
    
    // List widget
    listWidget_ = new QListWidget(this);
    listWidget_->setSelectionMode(QAbstractItemView::SingleSelection);
    listWidget_->setVerticalScrollMode(QAbstractItemView::ScrollPerPixel);
    listWidget_->setHorizontalScrollBarPolicy(Qt::ScrollBarAlwaysOff);
    listWidget_->setFrameShape(QFrame::NoFrame);
    listWidget_->setSpacing(2);
    
    mainLayout->addWidget(listWidget_);
    
    // Loading widget
    loadingWidget_ = new QWidget();
    auto* loadingLayout = new QHBoxLayout(loadingWidget_);
    auto* loadingLabel = new QLabel(tr("Загрузка..."));
    loadingLayout->addStretch();
    loadingLayout->addWidget(loadingLabel);
    loadingLayout->addStretch();
    loadingWidget_->hide();
    
    mainLayout->addWidget(loadingWidget_);
    
    // Connect signals
    connect(listWidget_, &QListWidget::itemClicked, this, &SharedDocumentsListView::onItemClicked);
    connect(listWidget_->verticalScrollBar(), &QScrollBar::valueChanged, this, &SharedDocumentsListView::onScrollValueChanged);
}

void SharedDocumentsListView::setItems(const QList<SharedMediaItem>& items) {
    items_ = items;
    listWidget_->clear();
    
    QFileIconProvider iconProvider;
    
    for (int i = 0; i < items_.size(); ++i) {
        const auto& item = items_[i];
        
        // Create list widget item
        auto* listItem = new QListWidgetItem(listWidget_);
        listItem->setData(Qt::UserRole, i);
        
        // Get icon based on file extension
        QIcon icon = iconProvider.icon(QFileInfo(item.fileName));
        if (icon.isNull()) {
            icon = QApplication::style()->standardIcon(QStyle::SP_FileIcon);
        }
        listItem->setIcon(icon);
        
        // Format display text
        QString displayText = QString("%1\n%2 • %3")
            .arg(item.fileName)
            .arg(item.formattedSize())
            .arg(item.sentAt.toString("dd.MM.yyyy"));
        
        listItem->setText(displayText);
        listItem->setToolTip(item.fileName);
        
        listWidget_->addItem(listItem);
    }
}

void SharedDocumentsListView::setLoading(bool loading) {
    loadingWidget_->setVisible(loading);
}

void SharedDocumentsListView::clear() {
    listWidget_->clear();
    items_.clear();
}

QSize SharedDocumentsListView::sizeHint() const {
    return QSize(280, 200);
}

void SharedDocumentsListView::onItemClicked(QListWidgetItem* listItem) {
    int index = listItem->data(Qt::UserRole).toInt();
    if (index >= 0 && index < items_.size()) {
        emit itemClicked(items_[index], index);
    }
}

void SharedDocumentsListView::onScrollValueChanged(int value) {
    if (value >= listWidget_->verticalScrollBar()->maximum() - 50) {
        emit loadMoreRequested();
    }
}

QString SharedDocumentsListView::getFileExtensionIcon(const QString& fileName) const {
    Q_UNUSED(fileName)
    // Return icon name based on extension
    // For now, return generic file icon
    return "file";
}

} // namespace BarkFluff