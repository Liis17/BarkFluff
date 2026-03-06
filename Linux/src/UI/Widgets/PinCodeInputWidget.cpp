#include "PinCodeInputWidget.h"
#include <QHBoxLayout>
#include <QKeyEvent>
#include <QApplication>
#include <QClipboard>

namespace BarkFluff {

PinCodeInputWidget::PinCodeInputWidget(int pinLength, QWidget* parent)
    : QWidget(parent)
    , pinLength_(pinLength)
{
    setupUi();
}

void PinCodeInputWidget::setupUi() {
    auto* layout = new QHBoxLayout(this);
    layout->setContentsMargins(0, 0, 0, 0);
    layout->setSpacing(CELL_SPACING);
    
    QString cellStyle = R"(
        QLineEdit {
            padding: 0;
            border: 2px solid #ddd;
            border-radius: 10px;
            font-size: 22px;
            font-weight: bold;
            background: white;
            text-align: center;
        }
        QLineEdit:focus {
            border-color: #3DAEE9;
            background: #f0f7ff;
        }
        QLineEdit:disabled {
            background: #f5f5f5;
            color: #999;
        }
    )";
    
    for (int i = 0; i < pinLength_; ++i) {
        auto* cell = new QLineEdit(this);
        cell->setFixedSize(CELL_SIZE, CELL_SIZE);
        cell->setMaxLength(1);
        cell->setAlignment(Qt::AlignCenter);
        cell->setStyleSheet(cellStyle);
        cell->setEchoMode(QLineEdit::Password);
        
        // Install event filter for key handling
        cell->installEventFilter(this);
        
        // Connect signals with index
        connect(cell, &QLineEdit::textChanged, this, [this, i](const QString& text) {
            onTextChanged(i, text);
        });
        
        cells_.append(cell);
        layout->addWidget(cell);
    }
    
    // Add stretch on both sides for centering
    layout->insertStretch(0);
    layout->addStretch();
}

void PinCodeInputWidget::setPinLength(int length) {
    if (length == pinLength_ || length < 1) return;
    
    // Clear existing cells
    for (auto* cell : cells_) {
        delete cell;
    }
    cells_.clear();
    
    pinLength_ = length;
    setupUi();
}

QString PinCodeInputWidget::pin() const {
    QString result;
    for (const auto* cell : cells_) {
        result += cell->text();
    }
    return result;
}

void PinCodeInputWidget::clear() {
    for (auto* cell : cells_) {
        cell->clear();
    }
    if (!cells_.isEmpty()) {
        cells_.first()->setFocus();
    }
}

void PinCodeInputWidget::setFocusToFirst() {
    if (!cells_.isEmpty()) {
        cells_.first()->setFocus();
    }
}

void PinCodeInputWidget::setNumericOnly(bool numericOnly) {
    numericOnly_ = numericOnly;
}

void PinCodeInputWidget::setEnabled(bool enabled) {
    for (auto* cell : cells_) {
        cell->setEnabled(enabled);
    }
}

void PinCodeInputWidget::onTextChanged(int index, const QString& text) {
    // Filter non-numeric characters if numericOnly
    if (numericOnly_ && !text.isEmpty()) {
        bool isNumeric = false;
        text.toInt(&isNumeric);
        if (!isNumeric) {
            cells_[index]->clear();
            return;
        }
    }
    
    // Auto-advance to next cell
    if (text.length() == 1 && index < cells_.size() - 1) {
        cells_[index + 1]->setFocus();
    }
    
    // Emit signals
    QString currentPin = pin();
    emit pinChanged(currentPin);
    
    if (currentPin.length() == pinLength_) {
        emit pinEntered(currentPin);
    }
}

bool PinCodeInputWidget::eventFilter(QObject* watched, QEvent* event) {
    if (event->type() == QEvent::KeyPress) {
        auto* keyEvent = static_cast<QKeyEvent*>(event);
        int index = cells_.indexOf(static_cast<QLineEdit*>(watched));
        
        if (index >= 0) {
            // Handle Backspace - go to previous cell
            if (keyEvent->key() == Qt::Key_Backspace) {
                if (cells_[index]->text().isEmpty() && index > 0) {
                    // Current cell is empty, go to previous
                    cells_[index - 1]->setFocus();
                    cells_[index - 1]->clear();
                } else {
                    // Clear current cell
                    cells_[index]->clear();
                }
                return true;
            }
            
            // Handle paste from clipboard
            if (keyEvent->key() == Qt::Key_V && (keyEvent->modifiers() & Qt::ControlModifier)) {
                QClipboard* clipboard = QApplication::clipboard();
                QString text = clipboard->text().trimmed();
                
                // Filter for numeric if needed
                if (numericOnly_) {
                    QString filtered;
                    for (const QChar& c : text) {
                        if (c.isDigit()) {
                            filtered += c;
                        }
                    }
                    text = filtered;
                }
                
                // Paste into cells
                int pasteIndex = 0;
                for (int i = 0; i < text.length() && i < cells_.size(); ++i) {
                    cells_[i]->setText(text.at(i));
                    pasteIndex = i;
                }
                
                // Focus last filled cell or next empty
                if (pasteIndex < cells_.size() - 1) {
                    cells_[pasteIndex + 1]->setFocus();
                } else {
                    cells_.last()->setFocus();
                }
                
                return true;
            }
            
            // Handle left/right arrow keys
            if (keyEvent->key() == Qt::Key_Left && index > 0) {
                cells_[index - 1]->setFocus();
                return true;
            }
            if (keyEvent->key() == Qt::Key_Right && index < cells_.size() - 1) {
                cells_[index + 1]->setFocus();
                return true;
            }
        }
    }
    
    return QWidget::eventFilter(watched, event);
}

} // namespace BarkFluff