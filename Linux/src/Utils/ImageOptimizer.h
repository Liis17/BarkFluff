/**
 * @file ImageOptimizer.h
 * @brief Оптимизация изображений перед отправкой
 */

#pragma once

#include <QImage>
#include <QByteArray>
#include <QString>

namespace BarkFluff {

/**
 * @brief Результат оптимизации изображения
 */
struct OptimizedImage {
    QByteArray data;           ///< Оптимизированные данные
    QString format;            ///< Формат ("JPEG", "PNG", "WEBP")
    int originalWidth = 0;     ///< Исходная ширина
    int originalHeight = 0;    ///< Исходная высота
    int resultWidth = 0;       ///< Результирующая ширина
    int resultHeight = 0;      ///< Результирующая высота
    qint64 originalSize = 0;   ///< Исходный размер в байтах
    qint64 resultSize = 0;     ///< Результирующий размер в байтах
    double compressionRatio = 0.0; ///< Коэффициент сжатия
};

/**
 * @brief Настройки оптимизации изображений
 */
struct ImageOptimizationSettings {
    int maxWidth = 1920;           ///< Максимальная ширина
    int maxHeight = 1920;          ///< Максимальная высота
    int quality = 85;              ///< Качество JPEG (1-100)
    bool convertToJpeg = true;     ///< Конвертировать в JPEG для фото
    bool preserveTransparency = true; ///< Сохранять прозрачность (PNG)
    qint64 maxSizeBytes = 10 * 1024 * 1024; ///< Максимальный размер (10MB)
    
    static ImageOptimizationSettings defaultSettings() {
        return {};
    }
    
    static ImageOptimizationSettings highQuality() {
        return {
            .maxWidth = 2560,
            .maxHeight = 2560,
            .quality = 95,
            .convertToJpeg = true,
            .preserveTransparency = true,
            .maxSizeBytes = 20 * 1024 * 1024
        };
    }
    
    static ImageOptimizationSettings compressed() {
        return {
            .maxWidth = 1280,
            .maxHeight = 1280,
            .quality = 70,
            .convertToJpeg = true,
            .preserveTransparency = false,
            .maxSizeBytes = 2 * 1024 * 1024
        };
    }
};

/**
 * @brief Класс для оптимизации изображений перед отправкой
 * 
 * Умеет:
 * - Масштабировать до максимальных размеров
 * - Сжимать с настраиваемым качеством
 * - Конвертировать в подходящий формат
 * - Сохранять прозрачность для PNG/GIF
 */
class ImageOptimizer {
public:
    /**
     * @brief Оптимизировать изображение
     * @param image Исходное изображение
     * @param settings Настройки оптимизации
     * @return Оптимизированное изображение
     */
    static OptimizedImage optimize(
        const QImage& image,
        const ImageOptimizationSettings& settings = ImageOptimizationSettings::defaultSettings()
    );
    
    /**
     * @brief Оптимизировать из данных
     * @param data Данные изображения
     * @param settings Настройки оптимизации
     * @return Оптимизированное изображение
     */
    static OptimizedImage optimizeFromData(
        const QByteArray& data,
        const ImageOptimizationSettings& settings = ImageOptimizationSettings::defaultSettings()
    );
    
    /**
     * @brief Оптимизировать из файла
     * @param filePath Путь к файлу
     * @param settings Настройки оптимизации
     * @return Оптимизированное изображение
     */
    static OptimizedImage optimizeFromFile(
        const QString& filePath,
        const ImageOptimizationSettings& settings = ImageOptimizationSettings::defaultSettings()
    );
    
    /**
     * @brief Определить, нужна ли оптимизация
     * @param data Данные изображения
     * @param settings Настройки
     * @return true если оптимизация нужна
     */
    static bool needsOptimization(
        const QByteArray& data,
        const ImageOptimizationSettings& settings = ImageOptimizationSettings::defaultSettings()
    );
    
    /**
     * @brief Проверить, является ли файл изображением
     * @param filePath Путь к файлу
     * @return true если это изображение
     */
    static bool isImageFile(const QString& filePath);
    
    /**
     * @brief Проверить, является ли файл анимированным (GIF, APNG)
     * @param filePath Путь к файлу
     * @return true если это анимация
     */
    static bool isAnimated(const QString& filePath);
    
    /**
     * @brief Получить MIME-тип по формату
     */
    static QString mimeType(const QString& format);

private:
    /**
     * @brief Масштабировать изображение если нужно
     */
    static QImage scaleIfNeeded(
        const QImage& image,
        int maxWidth,
        int maxHeight
    );
    
    /**
     * @brief Выбрать оптимальный формат
     */
    static QString chooseFormat(
        const QImage& image,
        const ImageOptimizationSettings& settings
    );
    
    /**
     * @brief Сжать с целевым размером
     */
    static QByteArray compressToTargetSize(
        const QImage& image,
        const QString& format,
        qint64 targetSize,
        int initialQuality
    );
};

} // namespace BarkFluff