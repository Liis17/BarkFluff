# 🔔 Умные сводные уведомления и digest

> Категория: Уведомления
> Платформы: ВСЕ (бэкенд) + **Android** / **iOS** (продвинутые уведомления)
> Приоритет: 🟡 Средний
> Сложность: ⭐⭐⭐

---

## Описание

Вместо N отдельных push-уведомлений («Иван: привет», «Иван: ты здесь?», «Иван: окей») приходит **одно умное**: «Иван: 3 новых сообщения». Для групп — «Команда: Мария, Алекс и ещё 2 написали». Плюс **ежедневный дайджест** для неактивных пользователей.

---

## Ключевые возможности

### Умные уведомления
- Группировка push по чату (стек уведомлений Android / iOS notification grouping)
- «Свёрнутое» уведомление: «3 чата, 12 сообщений»
- «Развёрнутое»: список последних N сообщений из каждого чата
- **Inline-ответ** прямо из уведомления (Android RemoteInput / iOS UNTextInputNotificationAction)
- **Быстрые действия**: «Прочитано» / «Заглушить на 1 час» прямо из уведомления

### Email-дайджест (для неактивных)
- Если пользователь не заходил N дней — письмо «Вы пропустили 5 сообщений»
- Краткое превью (только количество, без текста сообщений из соображений приватности)
- Кнопка «Открыть BarkFluff» → deep link `bf://`
- Настройка частоты: ежедневно / еженедельно / никогда

---

## Архитектура

### Inline-ответ из уведомления

```
Android RemoteInput → BroadcastReceiver → gRPC SendMessage
                                            → обновить уведомление
```

```kotlin
// QuickReplyReceiver.kt — новый BroadcastReceiver
class QuickReplyReceiver : BroadcastReceiver() {
    override fun onReceive(context: Context, intent: Intent) {
        val replyText = RemoteInput.getResultsFromIntent(intent)
            ?.getCharSequence(QUICK_REPLY_KEY)?.toString() ?: return
        val chatId = intent.getStringExtra(EXTRA_CHAT_ID) ?: return

        // Запустить корутину → gRPC SendMessage → отметить уведомление как отвеченное
        CoroutineScope(Dispatchers.IO).launch {
            grpcManager.sendMessage(chatId, replyText)
        }
    }
}
```

### iOS — UNUserNotificationCenter

```swift
// Категория уведомления с текстовым полем ответа
let replyAction = UNTextInputNotificationAction(
    identifier: "REPLY_ACTION",
    title: "Ответить",
    options: [],
    textInputButtonTitle: "Отправить",
    textInputPlaceholder: "Сообщение..."
)

// UNUserNotificationCenterDelegate.didReceive response:
// → вызвать gRPC SendMessage
```

### Email-дайджест (Notification сервис)

- `DigestScheduler` — BackgroundService в [[../Backend/Notification]], запуск раз в день
- Запрос к [[../Backend/Messages]]: пользователи с непрочитанными + неактивные > 2 дней
- Шаблон письма: `digest_template.html` (Razor или Scriban)
- Отправка через существующий SMTP-стек в [[../Backend/Notification]]

---

## Изменения в существующих сервисах

| Сервис | Изменение |
|--------|-----------|
| [[../Backend/Notification]] | `DigestScheduler`, шаблон дайджеста |
| [[../Backend/CloudMessaging]] | Поддержка `notification group`, `reply action` в FCM payload |
| [[../Backend/Messages]] | API для получения непрочитанного по пользователю (для дайджеста) |

---

## Группировка на Android

```kotlin
// В NotificationManager:
// 1. Каждое уведомление чата — своя группа (groupKey = chatId)
// 2. Summary-уведомление для всей группы
val summaryNotification = NotificationCompat.Builder(context, CHANNEL_ID)
    .setContentTitle("BarkFluff")
    .setContentText("$count новых сообщений")
    .setGroupSummary(true)
    .setGroup(GROUP_KEY_MESSAGES)
    .build()
```

