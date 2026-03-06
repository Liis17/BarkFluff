/**
 * @file ExpandingTextEdit.cpp
 * @brief Реализация виджета текстового поля с авто-расширением
 */

#include "ExpandingTextEdit.h"
#include <QScrollBar>
#include <QTextDocument>

namespace BarkFluff {

ExpandingTextEdit::ExpandingTextEdit(QWidget* parent)
    : QTextEdit(parent)
{
    // Начальная высота - одна строка
    setMinimumHeight(minHeight_);
    setMaximumHeight(minHeight_);
    
    // Отключаем горизонтальную прокрутку
    setHorizontalScrollBarPolicy(Qt::ScrollBarAlwaysOff);
    setVerticalScrollBarPolicy(Qt::ScrollBarAlwaysOff);
    
    // Подключаем сигнал изменения содержимого
    connect(document(), &QTextDocument::contentsChanged,
            this, &ExpandingTextEdit::onContentsChanged);
}

void ExpandingTextEdit::setMinHeight(int height) {
    minHeight_ = height;
    adjustHeight();
}

void ExpandingTextEdit::setMaxHeight(int height) {
    maxHeight_ = height;
    adjustHeight();
}

void ExpandingTextEdit::keyPressEvent(QKeyEvent* e) {
    // Enter без модификаторов - отправка сообщения
    if (e->key() == Qt::Key_Return || e->key() == Qt::Key_Enter) {
        if (!(e->modifiers() & Qt::ShiftModifier)) {
            // Просто Enter - отправляем
            emit sendRequested();
            e->accept();
            return;
        }
        // Shift+Enter - продолжаем для переноса строки
    }
    
    // Передаём событие базовому классу
    QTextEdit::keyPressEvent(e);
}

void ExpandingTextEdit::onContentsChanged() {
    adjustHeight();
}

void ExpandingTextEdit::adjustHeight() {
    // Получаем идеальную высоту документа
    document()->setTextWidth(width());
    qreal docHeight = document()->size().height();
    
    // Добавляем отступы (padding из stylesheet)
    int margins = 16;  // 8px сверху + 8px снизу (из padding: 8px 12px)
    int newHeight = static_cast<int>(docHeight) + margins;
    
    // Ограничиваем minHeight и maxHeight
    newHeight = qBound(minHeight_, newHeight, maxHeight_);
    
    // Включаем вертикальную прокрутку если достигли maxHeight
    if (newHeight >= maxHeight_) {
        setVerticalScrollBarPolicy(Qt::ScrollBarAsNeeded);
    } else {
        setVerticalScrollBarPolicy(Qt::ScrollBarAlwaysOff);
    }
    
    // Устанавливаем новую высоту
    setMinimumHeight(newHeight);
    setMaximumHeight(newHeight);
}

} // namespace BarkFluff