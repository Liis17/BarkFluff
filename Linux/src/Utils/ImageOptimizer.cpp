/**
 * @file ImageOptimizer.cpp
 * @brief Реализация оптимизации изображений
 */

#include "ImageOptimizer.h"
#include <QBuffer>
#include <QFileInfo>
#include <QDebug>

namespace BarkFluff {

OptimizedImage ImageOptimizer::optimize(
    const QImage& image,
    const ImageOptimizationSettings& settings
) {
    OptimizedImage result;
    result.originalWidth = image.width();
    result.originalHeight = image.height();
    result.originalSize = 0;  // Неизвестен для QImage
    
    if (image.isNull()) {
        qWarning() << "ImageOptimizer::optimize - null image";
        return result;
    }
    
    // Масштабируем если нужно
    QImage scaled = scaleIfNeeded(image, settings.maxWidth, settings.maxHeight);
    result.resultWidth = scaled.width();
    result.resultHeight = scaled.height();
    
    // Выбираем формат
    QString format = chooseFormat(scaled, settings);
    result.format = format;
    
    // Сжимаем
    QByteArray compressedData;
    QBuffer buffer(&compressedData);
    buffer.open(QIODevice::WriteOnly);
    
    if (format == "JPEG") {
        scaled.save(&buffer, "JPEG", settings.quality);
    } else if (format == "PNG") {
        scaled.save(&buffer, "PNG", settings.quality);
    } else if (format == "WEBP") {
        scaled.save(&buffer, "WEBP", settings.quality);
    } else {
        scaled.save(&buffer, format.toUtf8(), settings.quality);
    }
    
    buffer.close();
    
    result.data = compressedData;
    result.resultSize = compressedData.size();
    
    if (result.originalSize > 0) {
        result.compressionRatio = static_cast<double>(result.resultSize) / result.originalSize;
    }
    
    qDebug() << "ImageOptimizer::optimize -" << result.originalWidth << "x" << result.originalHeight
             << "->" << result.resultWidth << "x" << result.resultHeight
             << "format:" << format
             << "size:" << result.resultSize << "bytes";
    
    return result;
}

OptimizedImage ImageOptimizer::optimizeFromData(
    const QByteArray& data,
    const ImageOptimizationSettings& settings
) {
    OptimizedImage result;
    result.originalSize = data.size();
    
    QImage image;
    if (!image.loadFromData(data)) {
        qWarning() << "ImageOptimizer::optimizeFromData - failed to load image";
        return result;
    }
    
    result = optimize(image, settings);
    result.originalSize = data.size();
    result.compressionRatio = static_cast<double>(result.resultSize) / result.originalSize;
    
    return result;
}

OptimizedImage ImageOptimizer::optimizeFromFile(
    const QString& filePath,
    const ImageOptimizationSettings& settings
) {
    OptimizedImage result;
    
    QFileInfo info(filePath);
    result.originalSize = info.size();
    
    QImage image;
    if (!image.load(filePath)) {
        qWarning() << "ImageOptimizer::optimizeFromFile - failed to load:" << filePath;
        return result;
    }
    
    result = optimize(image, settings);
    result.originalSize = info.size();
    result.compressionRatio = static_cast<double>(result.resultSize) / result.originalSize;
    
    return result;
}

bool ImageOptimizer::needsOptimization(
    const QByteArray& data,
    const ImageOptimizationSettings& settings
) {
    // Проверяем размер
    if (data.size() > settings.maxSizeBytes) {
        return true;
    }
    
    // Загружаем для проверки размеров
    QImage image;
    if (!image.loadFromData(data)) {
        return false;
    }
    
    // Проверяем размеры
    if (image.width() > settings.maxWidth || image.height() > settings.maxHeight) {
        return true;
    }
    
    return false;
}

bool ImageOptimizer::isImageFile(const QString& filePath) {
    static const QStringList imageExtensions = {
        "jpg", "jpeg", "png", "gif", "bmp", "webp", "tiff", "tif"
    };
    
    QString ext = QFileInfo(filePath).suffix().toLower();
    return imageExtensions.contains(ext);
}

bool ImageOptimizer::isAnimated(const QString& filePath) {
    QString ext = QFileInfo(filePath).suffix().toLower();
    return ext == "gif";
}

QString ImageOptimizer::mimeType(const QString& format) {
    static const QHash<QString, QString> mimeTypes = {
        {"JPEG", "image/jpeg"},
        {"JPG", "image/jpeg"},
        {"PNG", "image/png"},
        {"GIF", "image/gif"},
        {"WEBP", "image/webp"},
        {"BMP", "image/bmp"},
        {"TIFF", "image/tiff"},
    };
    
    return mimeTypes.value(format.toUpper(), "application/octet-stream");
}

QImage ImageOptimizer::scaleIfNeeded(
    const QImage& image,
    int maxWidth,
    int maxHeight
) {
    if (image.width() <= maxWidth && image.height() <= maxHeight) {
        return image;
    }
    
    // Масштабируем с сохранением пропорций
    return image.scaled(
        maxWidth, maxHeight,
        Qt::KeepAspectRatio,
        Qt::SmoothTransformation
    );
}

QString ImageOptimizer::chooseFormat(
    const QImage& image,
    const ImageOptimizationSettings& settings
) {
    // Если есть прозрачность и нужно сохранить - PNG
    if (settings.preserveTransparency && image.hasAlphaChannel()) {
        // Проверяем, есть ли реально прозрачные пиксели
        for (int y = 0; y < image.height(); ++y) {
            for (int x = 0; x < image.width(); ++x) {
                if (qAlpha(image.pixel(x, y)) < 255) {
                    return "PNG";
                }
            }
        }
    }
    
    // Для фотографий - JPEG
    if (settings.convertToJpeg) {
        return "JPEG";
    }
    
    // По умолчанию PNG
    return "PNG";
}

QByteArray ImageOptimizer::compressToTargetSize(
    const QImage& image,
    const QString& format,
    qint64 targetSize,
    int initialQuality
) {
    QByteArray result;
    
    // Бинарный поиск качества для достижения целевого размера
    int minQuality = 10;
    int maxQuality = 100;
    int quality = initialQuality;
    
    while (minQuality <= maxQuality) {
        QBuffer buffer(&result);
        buffer.open(QIODevice::WriteOnly);
        image.save(&buffer, format.toUtf8(), quality);
        buffer.close();
        
        if (result.size() > targetSize) {
            maxQuality = quality - 1;
        } else if (result.size() < targetSize * 0.9) {
            minQuality = quality + 1;
        } else {
            break;  // Достаточно близко к целевому
        }
        
        quality = (minQuality + maxQuality) / 2;
    }
    
    return result;
}

} // namespace BarkFluff