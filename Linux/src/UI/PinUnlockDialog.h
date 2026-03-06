#pragma once

#include <QDialog>
#include <QLabel>
#include <QPushButton>
#include "Widgets/PinCodeInputWidget.h"

namespace BarkFluff {

/**
 * @brief Диалог для ввода PIN-кода при разблокировке сессии
 */
class PinUnlockDialog : public QDialog {
    Q_OBJECT
    
signals:
    /// Эмитится когда пользователь нажал Unlock - проверка PIN происходит в MainWindow
    void unlockRequested();
    
public:
    explicit PinUnlockDialog(QWidget* parent = nullptr);
    
    /// Установить имя пользователя для приветствия
    void setUsername(const QString& username);
    
    /// Установить название сервера
    void setServerName(const QString& serverName);
    
    /// Установить длину PIN-кода
    void setPinLength(int length);
    
    /// Получить введённый PIN
    QString pin() const;
    
    /// Показать сообщение об ошибке
    void showError(const QString& message);
    
    /// Скрыть ошибку
    void clearError();
    
    /// Включить/выключить ввод
    void setInputsEnabled(bool enabled);

private:
    void setupUi();
    
    QLabel* serverLabel_ = nullptr;
    QLabel* welcomeLabel_ = nullptr;
    PinCodeInputWidget* pinInput_ = nullptr;
    QLabel* errorLabel_ = nullptr;
    QPushButton* unlockButton_ = nullptr;
    QPushButton* switchAccountButton_ = nullptr;
    
    QString username_;
    QString serverName_;
};

} // namespace BarkFluff