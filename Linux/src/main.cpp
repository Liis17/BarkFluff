#include <QApplication>
#include <QFontDatabase>
#include <QDebug>
#include "UI/MainWindow.h"

/**
 * @brief Загружает шрифт эмодзи и устанавливает его как fallback
 * @return true если шрифт успешно загружен
 */
bool loadEmojiFont() {
    // Загружаем шрифт Noto Color Emoji из ресурсов
    int fontId = QFontDatabase::addApplicationFont(":/fonts/Fonts/NotoColorEmoji.ttf");
    
    if (fontId == -1) {
        qWarning() << "Failed to load Noto Color Emoji font from resources";
        
        // Пробуем загрузить из системы
        QStringList systemEmojiFonts = {
            "Noto Color Emoji",
            "Segoe UI Emoji",
            "Apple Color Emoji",
            "Twitter Color Emoji",
            "JoyPixels"
        };
        
        QFontDatabase db;
        QStringList families = db.families();
        for (const QString& fontName : systemEmojiFonts) {
            if (families.contains(fontName)) {
                qDebug() << "Found system emoji font:" << fontName;
                return true;
            }
        }
        
        qWarning() << "No emoji font found in system";
        return false;
    }
    
    QStringList families = QFontDatabase::applicationFontFamilies(fontId);
    qDebug() << "Loaded emoji font families:" << families;
    
    if (!families.isEmpty()) {
        QString fontFamily = families.first();
        
        // Получаем текущий шрифт приложения
        QFont defaultFont = QApplication::font();
        
        // В Qt 6 можно использовать setFamilies для fallback шрифтов
        // Создаём шрифт с emoji как fallback
        QFont emojiFont(defaultFont.family());
        emojiFont.setFamilies({defaultFont.family(), fontFamily});
        emojiFont.setPointSize(defaultFont.pointSize());
        
        QApplication::setFont(emojiFont);
        
        qDebug() << "Emoji font set as fallback:" << fontFamily;
    }
    
    return true;
}

int main(int argc, char *argv[]) {
    QApplication app(argc, argv);
    
    app.setApplicationName("BarkFluff");
    app.setApplicationVersion("1.0.0");
    app.setOrganizationName("BarkFluff");
    
    // Загружаем шрифт эмодзи до создания UI
    loadEmojiFont();
    
    BarkFluff::MainWindow window;
    window.show();
    
    return app.exec();
}