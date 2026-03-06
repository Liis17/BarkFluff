#pragma once

#include <QWidget>
#include <QString>
#include <QPixmap>
#include <QNetworkAccessManager>
#include <QNetworkReply>

namespace BarkFluff {

// Forward declaration
enum class CachedFileType;

/**
 * @brief Виджет аватара пользователя
 * 
 * Отображает круглый аватар с изображением или инициалами.
 * Опционально показывает индикатор онлайн-статуса.
 */
class AvatarWidget : public QWidget {
    Q_OBJECT
    Q_PROPERTY(int size READ size WRITE setSize NOTIFY sizeChanged)
    Q_PROPERTY(QString imageUrl READ imageUrl WRITE setImageUrl NOTIFY imageUrlChanged)
    Q_PROPERTY(QString initials READ initials WRITE setInitials NOTIFY initialsChanged)
    Q_PROPERTY(bool isOnline READ isOnline WRITE setIsOnline NOTIFY isOnlineChanged)
    Q_PROPERTY(bool showOnlineIndicator READ showOnlineIndicator WRITE setShowOnlineIndicator NOTIFY showOnlineIndicatorChanged)

public:
    explicit AvatarWidget(QWidget* parent = nullptr);
    explicit AvatarWidget(const QString& imageUrl, const QString& initials, int size, QWidget* parent = nullptr);
    ~AvatarWidget() override;

    // Properties
    int size() const { return size_; }
    void setSize(int size);

    QString imageUrl() const { return imageUrl_; }
    void setImageUrl(const QString& url);

    QString initials() const { return initials_; }
    void setInitials(const QString& initials);

    bool isOnline() const { return isOnline_; }
    void setIsOnline(bool online);

    bool showOnlineIndicator() const { return showOnlineIndicator_; }
    void setShowOnlineIndicator(bool show);
    
    /**
     * @brief Использовать FileCacheService для загрузки изображений
     * 
     * Когда true, загрузка происходит через FileCacheService вместо прямого HTTP запроса.
     * Это позволяет загружать изображения по ID файла, а не только по полному URL.
     */
    bool useCacheService() const { return useCacheService_; }
    void setUseCacheService(bool use);

    QSize sizeHint() const override;
    QSize minimumSizeHint() const override;

signals:
    void sizeChanged(int size);
    void imageUrlChanged(const QString& url);
    void initialsChanged(const QString& initials);
    void isOnlineChanged(bool online);
    void showOnlineIndicatorChanged(bool show);
    void clicked();

protected:
    void paintEvent(QPaintEvent* event) override;
    void mousePressEvent(QMouseEvent* event) override;

private slots:
    void onImageDownloaded(QNetworkReply* reply);

private:
    void loadAvatar();
    QColor initialsColor() const;
    int indicatorSize() const;
    int indicatorBorderWidth() const;

    int size_ = 40;
    QString imageUrl_;
    QString initials_;
    bool isOnline_ = false;
    bool showOnlineIndicator_ = false;
    bool useCacheService_ = false;

    QPixmap avatarPixmap_;
    bool isLoading_ = false;
    QNetworkAccessManager* networkManager_ = nullptr;
};

} // namespace BarkFluff