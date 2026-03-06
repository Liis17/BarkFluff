/**
 * @file MessageGrouper.h
 * @brief Группировка сообщений по дате и отправителю
 * 
 * Аналог MessageGrouper.swift из macOS версии
 */

#pragma once

#include <QList>
#include <QDate>
#include <QDateTime>
#include <QString>
#include <QCalendar>
#include "../Models/Message.h"

namespace BarkFluff {

/**
 * @brief Информация о группировке сообщения
 */
struct MessageGroupInfo {
    bool isFirstInGroup = false;   ///< Первое сообщение в группе (показать имя)
    bool isLastInGroup = false;    ///< Последнее сообщение в группе (показать хвостик)
    bool isOnlyInGroup = false;    ///< Единственное сообщение в группе (first + last)
    bool showTime = false;         ///< Показывать время для этого сообщения
    
    MessageGroupInfo() = default;
    MessageGroupInfo(bool first, bool last, bool only, bool time) 
        : isFirstInGroup(first), isLastInGroup(last), isOnlyInGroup(only), showTime(time) {}
};

/**
 * @brief Элемент для отображения в списке сообщений
 */
class MessageListItem {
public:
    enum class Type {
        DateSeparator,  ///< Разделитель дат
        Message         ///< Сообщение с информацией о группировке
    };
    
    Type type;
    QDate date;                    ///< Для DateSeparator
    BarkFluff::Message message;    ///< Для Message
    MessageGroupInfo groupInfo;    ///< Для Message
    
    // Constructor for date separator
    static MessageListItem dateSeparator(const QDate& date) {
        MessageListItem item;
        item.type = Type::DateSeparator;
        item.date = date;
        return item;
    }
    
    // Constructor for message
    static MessageListItem messageItem(const BarkFluff::Message& msg, const MessageGroupInfo& info) {
        MessageListItem item;
        item.type = Type::Message;
        item.message = msg;
        item.groupInfo = info;
        return item;
    }
    
    // Unique ID for QML/ListView
    QString id() const {
        if (type == Type::DateSeparator) {
            return "date-" + date.toString(Qt::ISODate);
        }
        return message.uniqueId();
    }
    
private:
    MessageListItem() = default;
};

/**
 * @brief Группировщик сообщений
 * 
 * Группирует сообщения по дате (разделители) и по отправителю 
 * (последовательные сообщения от одного человека в течение 60 секунд)
 */
class MessageGrouper {
public:
    /// Интервал группировки сообщений от одного отправителя (в секундах)
    static constexpr qint64 groupingInterval = 60;
    
    /// Интервал показа времени (каждые N секунд в группе)
    static constexpr qint64 showTimeInterval = 300; // 5 минут
    
    /**
     * @brief Сгруппировать сообщения для отображения
     * @param messages Массив сообщений (должен быть отсортирован по дате)
     * @param currentUserID ID текущего пользователя
     * @return Массив элементов для отображения
     */
    static QList<MessageListItem> groupMessages(
        const QList<BarkFluff::Message>& messages,
        qint64 currentUserID
    ) {
        QList<MessageListItem> items;
        
        if (messages.isEmpty()) {
            return items;
        }
        
        QDate currentDate;
        qint64 currentSenderID = 0;
        QDateTime groupStartTime;
        
        for (int i = 0; i < messages.size(); ++i) {
            const BarkFluff::Message& message = messages[i];
            QDateTime messageTime = message.sentAt;
            QDate messageDate = messageTime.date();
            
            // Проверяем, нужно ли добавить разделитель дат
            if (!currentDate.isValid() || currentDate != messageDate) {
                items.append(MessageListItem::dateSeparator(messageDate));
                currentDate = messageDate;
                currentSenderID = 0; // Сбрасываем группировку при новой дате
                groupStartTime = QDateTime();
            }
            
            // Определяем группировку по отправителю
            bool isFirstInGroup = false;
            bool isLastInGroup = false;
            
            // Проверяем, является ли это новым отправителем
            bool isNewSender = (currentSenderID != message.senderId);
            
            // Проверяем интервал времени
            qint64 timeInterval = 0;
            if (groupStartTime.isValid()) {
                timeInterval = groupStartTime.secsTo(messageTime);
            }
            
            // Начинаем новую группу если:
            // - Новый отправитель
            // - Прошло больше groupingInterval секунд
            bool shouldStartNewGroup = isNewSender || timeInterval > groupingInterval;
            
            if (shouldStartNewGroup) {
                isFirstInGroup = true;
                groupStartTime = messageTime;
            }
            
            // Проверяем, последнее ли это сообщение в группе
            const BarkFluff::Message* nextMessage = nullptr;
            if (i + 1 < messages.size()) {
                nextMessage = &messages[i + 1];
            }
            
            bool isLastSender = !nextMessage || nextMessage->senderId != message.senderId;
            qint64 nextTimeInterval = groupingInterval + 1;
            if (nextMessage && groupStartTime.isValid()) {
                nextTimeInterval = groupStartTime.secsTo(nextMessage->sentAt);
            }
            
            isLastInGroup = isLastSender || nextTimeInterval > groupingInterval;
            
            // Единственное в группе = первое И последнее
            bool isOnlyInGroup = isFirstInGroup && isLastInGroup;
            
            // Определяем, показывать ли время
            // Показываем время для последнего сообщения в группе или если прошло достаточно времени
            bool showTime = isLastInGroup || timeInterval >= showTimeInterval;
            
            MessageGroupInfo groupInfo(isFirstInGroup, isLastInGroup, isOnlyInGroup, showTime);
            items.append(MessageListItem::messageItem(message, groupInfo));
            
            currentSenderID = message.senderId;
        }
        
        return items;
    }
    
    /**
     * @brief Форматировать дату для разделителя
     */
    static QString formatDateSeparator(const QDate& date) {
        QDate today = QDate::currentDate();
        
        if (date == today) {
            return QObject::tr("Сегодня");
        } else if (date == today.addDays(-1)) {
            return QObject::tr("Вчера");
        } else {
            // Формат: "1 марта", "15 января"
            QString monthName;
            switch (date.month()) {
                case 1: monthName = QObject::tr("января"); break;
                case 2: monthName = QObject::tr("февраля"); break;
                case 3: monthName = QObject::tr("марта"); break;
                case 4: monthName = QObject::tr("апреля"); break;
                case 5: monthName = QObject::tr("мая"); break;
                case 6: monthName = QObject::tr("июня"); break;
                case 7: monthName = QObject::tr("июля"); break;
                case 8: monthName = QObject::tr("августа"); break;
                case 9: monthName = QObject::tr("сентября"); break;
                case 10: monthName = QObject::tr("октября"); break;
                case 11: monthName = QObject::tr("ноября"); break;
                case 12: monthName = QObject::tr("декабря"); break;
            }
            
            // Если год не текущий, добавляем год
            if (date.year() != today.year()) {
                return QString("%1 %2 %3").arg(date.day()).arg(monthName).arg(date.year());
            }
            
            return QString("%1 %2").arg(date.day()).arg(monthName);
        }
    }
    
    /**
     * @brief Форматировать время сообщения
     */
    static QString formatMessageTime(const QDateTime& time) {
        return time.time().toString("HH:mm");
    }
    
    /**
     * @brief Форматировать статус прочтения
     */
    static QString formatReadStatus(const BarkFluff::Message& message, qint64 currentUserId) {
        if (message.senderId != currentUserId) {
            return QString(); // Для чужих сообщений статуса нет
        }
        
        if (message.isSending()) {
            return QObject::tr("Отправка...");
        }
        
        if (message.hasSendError()) {
            return QObject::tr("Ошибка");
        }
        
        // Считаем сколько людей прочитало (кроме отправителя)
        int readByOthers = 0;
        for (qint64 readerId : message.readBy) {
            if (readerId != message.senderId) {
                readByOthers++;
            }
        }
        
        if (readByOthers > 0) {
            return QObject::tr("Прочитано");
        }
        
        return QObject::tr("Отправлено");
    }
};

} // namespace BarkFluff