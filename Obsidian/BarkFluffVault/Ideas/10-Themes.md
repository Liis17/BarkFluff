# Темы и виджеты

Parent: [[Ideas/Index]]

Приоритет/область: Низкий; персонализация.

Клиентские темы уже существуют; реальные настройки и ограничения описаны в [[Clients/Android]], [[Clients/macOS]], [[Clients/iOS]] и Windows-заметках.

Android уже имеет pinned-chats home-screen widget (`Android/Barkfluff.Client.Android/app/src/main/java/com/barkfluff/client/widget/`): настроенные чаты с preview/unread/аватаром и переходом в чат; быстрый ответ/online-status не подтверждены. Темы Android сейчас system DayNight, runtime custom-theme picker не подтверждён.

Предложения: AMOLED, пользовательские цвета/шрифты, JSON импорт/экспорт, публикация и превью тем, тема на чат, обои из галереи или свои, размытие и анимированные фоны.

Виджеты: последние чаты, быстрый переход/ответ и статус контакта на Android/iOS; быстрый доступ из трея Windows. Для Android рассмотреть AppWidget/Glance, для iOS — WidgetKit; частоту обновления, кеш и приватность определить по платформе.

Публичные темы в [[Backend/Users]] и фоны через [[Backend/Files]] — предложение. Таблицы, RPC и новые типы файлов не считать существующими.
