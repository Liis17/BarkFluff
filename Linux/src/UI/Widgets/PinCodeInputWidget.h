#pragma once

#include <QWidget>
#include <QVector>
#include <QLineEdit>
#include <QEvent>

namespace BarkFluff {

/**
 * @brief Виджет для ввода PIN-кода с отдельными ячейками
 * 
 * Создаёт N ячеек для ввода PIN-кода с автоматическим переходом между ними.
 */
class PinCodeInputWidget : public QWidget {
    Q_OBJECT
    
public:
    explicit PinCodeInputWidget(int pinLength = 4, QWidget* parent = nullptr);
    
    /// Установить количество ячеек
    void setPinLength(int length);
    
    /// Получить текущий PIN
    QString pin() const;
    
    /// Очистить поля
    void clear();
    
    /// Установить фокус на первое поле
    void setFocusToFirst();
    
    /// Установить режим только цифр (по умолчанию true)
    void setNumericOnly(bool numericOnly);
    
    /// Включить/выключить поля
    void setEnabled(bool enabled);

signals:
    /// Сигнал когда все ячейки заполнены
    void pinEntered(const QString& pin);
    
    /// Сигнал при изменении PIN (частично заполнен)
    void pinChanged(const QString& pin);

private slots:
    void onTextChanged(int index, const QString& text);

protected:
    bool eventFilter(QObject* watched, QEvent* event) override;

private:
    void setupUi();
    void updateLayout();
    
    int pinLength_;
    bool numericOnly_ = true;
    QVector<QLineEdit*> cells_;
    
    static constexpr int CELL_SIZE = 48;
    static constexpr int CELL_SPACING = 8;
};

} // namespace BarkFluff