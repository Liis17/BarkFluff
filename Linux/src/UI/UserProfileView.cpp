/**
 * @file UserProfileView.cpp
 * @brief Реализация страницы просмотра профиля пользователя
 */

#include "UserProfileView.h"
#include "UI/Widgets/BadgeChipWidget.h"
#include "UI/Widgets/FlowLayout.h"
#include "UI/Widgets/AttachmentWidgets.h"
#include "Services/FileCacheService.h"
#include "Connection/FilesClient.h"
#include <QVBoxLayout>
#include <QHBoxLayout>
#include <QScrollArea>
#include <QLabel>
#include <QPushButton>
#include <QFuture>
#include <QtConcurrent>
#include <QLocale>
#include <QDebug>
#include <QDesktopServices>
#include <QUrl>
#include <QStandardPaths>
#include <thread>

namespace BarkFluff {

UserProfileView::UserProfileView(
    const ServiceInfo& usersConfig,
    const ServiceInfo& onlinerConfig,
    const ServiceInfo& messagesConfig,
    const ServiceInfo& filesConfig,
    const QString& accessToken,
    qint64 currentUserId,
    const Chat& chat,
    QWidget* parent
)
    : QWidget(parent)
    , accessToken_(accessToken)
    , currentUserId_(currentUserId)
    , chat_(chat)
    , usersClient_(std::make_unique<UsersClient>(usersConfig))
    , onlinerClient_(new OnlinerClient(onlinerConfig, this))
    , messagesClient_(std::make_unique<MessagesClient>(messagesConfig))
    , filesConfig_(filesConfig)
{
    setupUI();
    
    // Определяем свой/чужой профиль
    if (!chat_.isGroupChat && !chat_.members.isEmpty()) {
        auto otherMember = chat_.members.first();
        isOwnProfile_ = (otherMember.userId == currentUserId_);
    }
    
    loadProfile();
}

UserProfileView::~UserProfileView() {
    if (profileWatcher_) {
        profileWatcher_->cancel();
        profileWatcher_->waitForFinished();
    }
    if (badgesWatcher_) {
        badgesWatcher_->cancel();
        badgesWatcher_->waitForFinished();
    }
}

void UserProfileView::setupUI() {
    auto* mainLayout = new QVBoxLayout(this);
    mainLayout->setContentsMargins(0, 0, 0, 0);
    mainLayout->setSpacing(0);
    
    // === Header ===
    headerWidget_ = new QWidget();
    headerWidget_->setFixedHeight(48);
    headerWidget_->setStyleSheet("background-color: palette(window);");
    
    auto* headerLayout = new QHBoxLayout(headerWidget_);
    headerLayout->setContentsMargins(8, 8, 8, 8);
    
    backButton_ = new QPushButton("←");
    backButton_->setFixedSize(32, 32);
    backButton_->setStyleSheet(R"(
        QPushButton {
            border: none;
            border-radius: 16px;
            font-size: 18px;
            background: transparent;
        }
        QPushButton:hover {
            background-color: rgba(128, 128, 128, 50);
        }
    )");
    connect(backButton_, &QPushButton::clicked, this, &UserProfileView::onBackClicked);
    headerLayout->addWidget(backButton_);
    
    auto* title = new QLabel("Профиль");
    title->setStyleSheet("font-size: 16px; font-weight: bold;");
    headerLayout->addWidget(title);
    headerLayout->addStretch();
    
    mainLayout->addWidget(headerWidget_);
    
    // === Scroll Area ===
    scrollArea_ = new QScrollArea();
    scrollArea_->setWidgetResizable(true);
    scrollArea_->setHorizontalScrollBarPolicy(Qt::ScrollBarAlwaysOff);
    scrollArea_->setStyleSheet("QScrollArea { border: none; background: transparent; }");
    
    contentWidget_ = new QWidget();
    contentWidget_->setStyleSheet("background: transparent;");
    
    auto* contentLayout = new QVBoxLayout(contentWidget_);
    contentLayout->setContentsMargins(16, 16, 16, 16);
    contentLayout->setSpacing(16);
    
    // === Avatar Section ===
    auto* avatarSection = new QVBoxLayout();
    avatarSection->setSpacing(8);
    avatarSection->setAlignment(Qt::AlignCenter);
    
    avatarWidget_ = new AvatarWidget(QString(), QString(), 96, this);
    avatarSection->addWidget(avatarWidget_, 0, Qt::AlignCenter);
    
    nameLabel_ = new QLabel();
    nameLabel_->setStyleSheet("font-size: 20px; font-weight: bold;");
    nameLabel_->setAlignment(Qt::AlignCenter);
    avatarSection->addWidget(nameLabel_);
    
    usernameLabel_ = new QLabel();
    usernameLabel_->setStyleSheet("font-size: 14px; color: gray;");
    usernameLabel_->setAlignment(Qt::AlignCenter);
    avatarSection->addWidget(usernameLabel_);
    
    onlineStatusWidget_ = new OnlineStatusWidget(OnlineStatusWidget::Size::Large);
    onlineStatusWidget_->setShowText(true);
    avatarSection->addWidget(onlineStatusWidget_, 0, Qt::AlignCenter);
    
    contentLayout->addLayout(avatarSection);
    
    // === Divider ===
    auto* divider1 = new QFrame();
    divider1->setFrameShape(QFrame::HLine);
    divider1->setStyleSheet("background-color: rgba(128, 128, 128, 50);");
    divider1->setFixedHeight(1);
    contentLayout->addWidget(divider1);
    
    // === Info Section ===
    auto* infoSection = new QVBoxLayout();
    infoSection->setSpacing(12);
    
    // Био
    bioLabel_ = new QLabel();
    bioLabel_->setWordWrap(true);
    bioLabel_->setTextInteractionFlags(Qt::TextSelectableByMouse);
    bioLabel_->setStyleSheet("font-size: 14px;");
    infoSection->addWidget(bioLabel_);
    
    // Бейджи
    badgesContainer_ = new QWidget();
    badgesContainer_->setStyleSheet("background: transparent;");
    auto* badgesLayout = new FlowLayout(badgesContainer_, 8, 8, 8);
    badgesLayout->setContentsMargins(0, 0, 0, 0);
    infoSection->addWidget(badgesContainer_);
    
    // ID пользователя
    userIdLabel_ = new QLabel();
    userIdLabel_->setStyleSheet("font-size: 13px; color: gray;");
    userIdLabel_->setTextInteractionFlags(Qt::TextSelectableByMouse);
    infoSection->addWidget(userIdLabel_);
    
    // Дата регистрации
    registrationLabel_ = new QLabel();
    registrationLabel_->setStyleSheet("font-size: 13px; color: gray;");
    infoSection->addWidget(registrationLabel_);
    
    // Лимит хранилища
    storageLabel_ = new QLabel();
    storageLabel_->setStyleSheet("font-size: 13px; color: gray;");
    storageLabel_->hide();
    infoSection->addWidget(storageLabel_);
    
    contentLayout->addLayout(infoSection);
    
    // === Divider ===
    auto* divider2 = new QFrame();
    divider2->setFrameShape(QFrame::HLine);
    divider2->setStyleSheet("background-color: rgba(128, 128, 128, 50);");
    divider2->setFixedHeight(1);
    contentLayout->addWidget(divider2);
    
    // === Action Button ===
    actionButton_ = new QPushButton();
    actionButton_->setFixedHeight(40);
    actionButton_->setStyleSheet(R"(
        QPushButton {
            background-color: #0078d4;
            color: white;
            border: none;
            border-radius: 8px;
            font-size: 14px;
            font-weight: bold;
        }
        QPushButton:hover {
            background-color: #106ebe;
        }
        QPushButton:pressed {
            background-color: #005a9e;
        }
    )");
    connect(actionButton_, &QPushButton::clicked, this, &UserProfileView::onActionClicked);
    contentLayout->addWidget(actionButton_);
    
    // === Shared Media Section ===
    auto* divider3 = new QFrame();
    divider3->setFrameShape(QFrame::HLine);
    divider3->setStyleSheet("background-color: rgba(128, 128, 128, 50);");
    divider3->setFixedHeight(1);
    contentLayout->addWidget(divider3);
    
    sharedMediaSection_ = new SharedMediaSection();
    contentLayout->addWidget(sharedMediaSection_);
    
    // Connect shared media signals
    connect(sharedMediaSection_, &SharedMediaSection::loadMediaRequested,
            this, &UserProfileView::onLoadMediaRequested);
    connect(sharedMediaSection_, &SharedMediaSection::loadDocumentsRequested,
            this, &UserProfileView::onLoadDocumentsRequested);
    connect(sharedMediaSection_, &SharedMediaSection::mediaItemClicked,
            this, &UserProfileView::onMediaItemClicked);
    connect(sharedMediaSection_, &SharedMediaSection::documentItemClicked,
            this, &UserProfileView::onDocumentItemClicked);
    
    contentLayout->addStretch();
    
    scrollArea_->setWidget(contentWidget_);
    mainLayout->addWidget(scrollArea_);
    
    // === Loading Widget ===
    loadingWidget_ = new QWidget(this);
    loadingWidget_->setStyleSheet("background-color: rgba(255, 255, 255, 200);");
    auto* loadingLayout = new QVBoxLayout(loadingWidget_);
    loadingLayout->setAlignment(Qt::AlignCenter);
    auto* loadingLabel = new QLabel("Загрузка профиля...");
    loadingLabel->setAlignment(Qt::AlignCenter);
    loadingLayout->addWidget(loadingLabel);
    loadingWidget_->hide();
    
    // === Error Label ===
    errorLabel_ = new QLabel(this);
    errorLabel_->setStyleSheet("color: red; padding: 16px;");
    errorLabel_->setWordWrap(true);
    errorLabel_->setAlignment(Qt::AlignCenter);
    errorLabel_->hide();
    mainLayout->addWidget(errorLabel_);
}

void UserProfileView::loadProfile() {
    if (isLoading_) return;
    
    isLoading_ = true;
    showLoading(true);
    showError("");
    
    // Определяем ID пользователя для загрузки
    qint64 targetUserId = 0;
    
    if (chat_.isGroupChat) {
        // Для группового чата - показываем информацию о группе
        // Пока загружаем информацию о первом участнике (временно)
        if (!chat_.members.isEmpty()) {
            targetUserId = chat_.members.first().userId;
        }
    } else {
        // Для DM - загружаем информацию о собеседнике
        if (!chat_.members.isEmpty()) {
            targetUserId = chat_.members.first().userId;
        }
    }
    
    if (targetUserId == 0) {
        showError("Не удалось определить пользователя");
        isLoading_ = false;
        showLoading(false);
        return;
    }
    
    // Загружаем профиль асинхронно
    profileWatcher_ = new QFutureWatcher<User>(this);
    connect(profileWatcher_, &QFutureWatcher<User>::finished, this, &UserProfileView::onProfileLoaded);
    
    QFuture<User> future = QtConcurrent::run([this, targetUserId]() {
        return usersClient_->getUser(targetUserId, accessToken_);
    });
    
    profileWatcher_->setFuture(future);
}

void UserProfileView::loadBadges() {
    if (user_.id == 0) return;
    
    badgesWatcher_ = new QFutureWatcher<QList<UserBadge>>(this);
    connect(badgesWatcher_, &QFutureWatcher<QList<UserBadge>>::finished, this, &UserProfileView::onBadgesLoaded);
    
    QFuture<QList<UserBadge>> future = QtConcurrent::run([this]() {
        return usersClient_->getUserBadges(user_.id, accessToken_, std::nullopt);
    });
    
    badgesWatcher_->setFuture(future);
}

void UserProfileView::onProfileLoaded() {
    if (!profileWatcher_) return;
    
    try {
        user_ = profileWatcher_->result();
        loadBadges();
        updateUI();
    } catch (const std::exception& e) {
        showError(QString("Ошибка загрузки профиля: %1").arg(e.what()));
    }
    
    delete profileWatcher_;
    profileWatcher_ = nullptr;
    isLoading_ = false;
    showLoading(false);
}

void UserProfileView::onBadgesLoaded() {
    if (!badgesWatcher_) return;
    
    try {
        user_.badges = badgesWatcher_->result();
        
        // Обновляем бейджи в UI
        // Очищаем старые
        auto* badgesLayout = dynamic_cast<FlowLayout*>(badgesContainer_->layout());
        if (badgesLayout) {
            QLayoutItem* item;
            while ((item = badgesLayout->takeAt(0)) != nullptr) {
                if (item->widget()) {
                    item->widget()->deleteLater();
                }
                delete item;
            }
        }
        
        // Добавляем новые
        for (const auto& badge : user_.badges) {
            auto* chip = new BadgeChipWidget(badge, badgesContainer_);
            badgesLayout->addWidget(chip);
        }
        
        badgesContainer_->setVisible(!user_.badges.isEmpty());
    } catch (const std::exception& e) {
        qWarning() << "Error loading badges:" << e.what();
    }
    
    delete badgesWatcher_;
    badgesWatcher_ = nullptr;
}

void UserProfileView::updateUI() {
    // Аватар
    if (avatarWidget_) {
        avatarWidget_->setSize(96);
        avatarWidget_->setImageUrl(user_.profilePicturePreview);
        avatarWidget_->setInitials(user_.avatarInitials());
    }
    
    // Имя
    nameLabel_->setText(user_.displayName());
    
    // Username
    if (!user_.username.isEmpty()) {
        usernameLabel_->setText("@" + user_.username);
        usernameLabel_->show();
    } else {
        usernameLabel_->hide();
    }
    
    // Био
    if (!user_.bio.isEmpty()) {
        bioLabel_->setText(user_.bio);
        bioLabel_->show();
    } else {
        bioLabel_->hide();
    }
    
    // ID
    userIdLabel_->setText(QString("ID: %1").arg(user_.id));
    
    // Дата регистрации
    registrationLabel_->setText(QString("Дата регистрации: %1").arg(formatRegistrationDate()));
    
    // Лимит хранилища
    if (user_.storageLimitGb > 0) {
        storageLabel_->setText(QString("Хранилище: %1 ГБ").arg(user_.storageLimitGb));
        storageLabel_->show();
    }
    
    // Кнопка действия
    if (isOwnProfile_) {
        actionButton_->setText("Редактировать профиль");
    } else {
        actionButton_->setText("Отправить сообщение");
    }
    
    // Онлайн-статус (только для DM)
    if (!chat_.isGroupChat && onlinerClient_) {
        // Получаем статус асинхронно
        QtConcurrent::run([this]() {
            try {
                auto statuses = onlinerClient_->getOnlineStatus({user_.id}, accessToken_);
                if (!statuses.isEmpty()) {
                    QMetaObject::invokeMethod(this, [this, status = statuses.first()]() {
                        onlineStatusWidget_->setStatus(status);
                    });
                }
            } catch (const std::exception& e) {
                qWarning() << "Failed to get online status:" << e.what();
            }
        });
    } else {
        onlineStatusWidget_->hide();
    }
    
    // Загружаем общие медиа чата
    if (sharedMediaSection_ && !chat_.id.isEmpty()) {
        sharedMediaSection_->setChatId(chat_.id);
    }
}

void UserProfileView::showLoading(bool show) {
    if (loadingWidget_) {
        loadingWidget_->setVisible(show);
        if (show) {
            loadingWidget_->setGeometry(rect());
        }
    }
}

void UserProfileView::showError(const QString& message) {
    if (errorLabel_) {
        errorLabel_->setText(message);
        errorLabel_->setVisible(!message.isEmpty());
    }
}

QString UserProfileView::formatRegistrationDate() const {
    if (!user_.registrationDate.isValid()) {
        return "неизвестно";
    }
    
    QLocale locale(QLocale::Russian);
    return locale.toString(user_.registrationDate.date(), "d MMMM yyyy");
}

void UserProfileView::onBackClicked() {
    emit backRequested();
}

void UserProfileView::onActionClicked() {
    if (isOwnProfile_) {
        emit editProfileRequested();
    } else {
        emit openChatRequested(chat_.id.toLongLong());
    }
}

void UserProfileView::onLoadMediaRequested(const QString& chatId, int offset, int size) {
    QString accessToken = accessToken_;
    MessagesClient* client = messagesClient_.get();
    
    std::thread([this, client, chatId, offset, size, accessToken]() {
        try {
            auto result = client->listChatAttachments(
                chatId, offset, size, SharedMediaFilter::Media, accessToken);
            
            QList<SharedMediaItem> items = result.items;
            bool hasMore = result.hasMore;
            
            QMetaObject::invokeMethod(this, [this, items, hasMore, offset]() {
                if (offset == 0) {
                    sharedMediaSection_->setMediaItems(items, hasMore);
                } else {
                    sharedMediaSection_->appendMediaItems(items, hasMore);
                }
            });
        } catch (const std::exception& e) {
            qWarning() << "Failed to load shared media:" << e.what();
            QMetaObject::invokeMethod(this, [this]() {
                sharedMediaSection_->setMediaItems({}, false);
            });
        }
    }).detach();
}

void UserProfileView::onLoadDocumentsRequested(const QString& chatId, int offset, int size) {
    QString accessToken = accessToken_;
    MessagesClient* client = messagesClient_.get();
    
    std::thread([this, client, chatId, offset, size, accessToken]() {
        try {
            auto result = client->listChatAttachments(
                chatId, offset, size, SharedMediaFilter::Documents, accessToken);
            
            QList<SharedMediaItem> items = result.items;
            bool hasMore = result.hasMore;
            
            QMetaObject::invokeMethod(this, [this, items, hasMore, offset]() {
                if (offset == 0) {
                    sharedMediaSection_->setDocumentItems(items, hasMore);
                } else {
                    sharedMediaSection_->appendDocumentItems(items, hasMore);
                }
            });
        } catch (const std::exception& e) {
            qWarning() << "Failed to load shared documents:" << e.what();
            QMetaObject::invokeMethod(this, [this]() {
                sharedMediaSection_->setDocumentItems({}, false);
            });
        }
    }).detach();
}

void UserProfileView::onMediaItemClicked(const SharedMediaItem& item, int index) {
    // Получаем все медиа элементы из секции
    QList<SharedMediaItem> mediaItems = sharedMediaSection_->mediaItems();
    
    if (mediaItems.isEmpty()) {
        return;
    }
    
    // Конвертируем SharedMediaItem в MessageAttachment
    QList<MessageAttachment> attachments;
    for (const auto& media : mediaItems) {
        MessageAttachment att;
        att.id = media.id;
        att.type = media.type;
        att.fileId = media.fileId;
        att.fileName = media.fileName;
        att.attachmentSize = media.fileSize;
        att.previewUrl = media.previewUrl;
        att.previewFileId = media.previewFileId;
        attachments.append(att);
    }
    
    // Открываем MediaViewerDialog
    auto* viewer = new MediaViewerDialog(attachments, index, this);
    viewer->setAttribute(Qt::WA_DeleteOnClose);
    viewer->show();
}

void UserProfileView::onDocumentItemClicked(const SharedMediaItem& item, int index) {
    Q_UNUSED(index)
    
    if (item.fileId.isEmpty()) {
        qWarning() << "Document has no file ID";
        return;
    }
    
    QString accessToken = accessToken_;
    ServiceInfo filesConfig = filesConfig_;
    QString fileId = item.fileId;
    QString fileName = item.fileName;
    
    // Получаем URL для скачивания через FilesClient (синхронно, но результат обрабатываем в главном потоке)
    QtConcurrent::run([filesConfig, accessToken, fileId, fileName]() {
        try {
            FilesClient filesClient(filesConfig);
            QString downloadUrl = filesClient.getDownloadUrl(fileId, accessToken);
            
            qDebug() << "Got download URL for document:" << fileId << "url:" << downloadUrl;
            
            // Возвращаемся в главный поток Qt для скачивания через FileCacheService
            QMetaObject::invokeMethod(FileCacheService::instance(), [fileId, downloadUrl, fileName]() {
                auto* cache = FileCacheService::instance();
                
                cache->getCachedFilePathAsync(fileId, CachedFileType::Document, downloadUrl,
                    [fileName](const QString& localPath) {
                        if (!localPath.isEmpty() && QFile::exists(localPath)) {
                            // Копируем файл с правильным именем в папку загрузок
                            QString downloadsDir = QStandardPaths::writableLocation(QStandardPaths::DownloadLocation);
                            QString destPath = downloadsDir + "/" + fileName;
                            
                            // Если файл уже существует, добавляем номер
                            int counter = 1;
                            QString baseName = fileName;
                            QString extension;
                            int dotPos = fileName.lastIndexOf('.');
                            if (dotPos > 0) {
                                baseName = fileName.left(dotPos);
                                extension = fileName.mid(dotPos);
                            }
                            
                            while (QFile::exists(destPath)) {
                                destPath = downloadsDir + "/" + baseName + QString(" (%1)").arg(counter) + extension;
                                counter++;
                            }
                            
                            if (QFile::copy(localPath, destPath)) {
                                qDebug() << "Document saved to:" << destPath;
                                QDesktopServices::openUrl(QUrl::fromLocalFile(destPath));
                            } else {
                                qWarning() << "Failed to copy document to downloads, opening cached file";
                                QDesktopServices::openUrl(QUrl::fromLocalFile(localPath));
                            }
                        } else {
                            qWarning() << "Failed to get document file:" << fileName;
                        }
                    });
            }, Qt::QueuedConnection);
        } catch (const std::exception& e) {
            qWarning() << "Failed to get download URL for document:" << fileName << "-" << e.what();
        }
    });
}

} // namespace BarkFluff
