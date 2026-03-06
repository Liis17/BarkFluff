/**
 * @file ExpandingTextEdit.h
 * @brief Виджет текстового поля ввода с авто-расширением и горячими клавишами
 * 
 * Поддерживает:
 * - Однострочный режим по умолчанию (компактная высота)
 * - Enter для отправки сообщения
 * - Shift+Enter для переноса строки с расширением поля
 * - Автоматическое расширение до maxHeight
 */

#pragma once

#include <QTextEdit>
#include <QKeyEvent>

namespace BarkFluff {

/**
 * @brief QTextEdit с автоматическим изменением высоты и обработкой Enter/Shift+Enter
 */
class ExpandingTextEdit : public QTextEdit {
    Q_OBJECT
    
public:
    explicit ExpandingTextEdit(QWidget* parent = nullptr);
    
    /**
     * @brief Установить минимальную высоту (в пикселях)
     */
    void setMinHeight(int height);
    
    /**
     * @brief Установить максимальную высоту (в пикселях)
     */
    void setMaxHeight(int height);
    
    /**
     * @brief Получить минимальную высоту
     */
    int minHeight() const { return minHeight_; }
    
    /**
     * @brief Получить максимальную высоту
     */
    int maxHeight() const { return maxHeight_; }
    
signals:
    /**
     * @brief Сигнал отправки сообщения (нажат Enter без Shift)
     */
    void sendRequested();
    
protected:
    void keyPressEvent(QKeyEvent* e) override;
    
private slots:
    void onContentsChanged();
    
private:
    void adjustHeight();
    
    int minHeight_ = 40;   ///< Минимальная высота (одна строка)
    int maxHeight_ = 120;  ///< Максимальная высота (~4-5 строк)
};

} // namespace BarkFluff