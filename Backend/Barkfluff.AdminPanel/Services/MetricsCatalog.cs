namespace Barkfluff.AdminPanel.Services;

/// <summary>Explicit allow-list of dashboard metrics. Internal implementation counters never leak through the API.</summary>
public static class MetricsCatalog
{
    public static readonly IReadOnlyList<MetricServiceDefinition> Services =
    [
        Service("BarkFluff.Messages", "Messages", true,
            Counter("messages_sent", "Обычные сообщения"),
            Counter("private_messages_sent", "Приватные сообщения"),
            Counter("secret_messages_sent", "Секретные сообщения"),
            Counter("attachments_total", "Вложения")),
        Service("BarkFluff.Files", "Files", true,
            Counter("files_uploaded", "Загруженные файлы"), Counter("files_downloaded", "Скачанные файлы"),
            Counter("upload_bytes_total", "Входящий трафик", "bytes"), Counter("download_bytes_total", "Исходящий трафик", "bytes"),
            Counter("file_traffic_bytes_total", "Файловый трафик", "bytes"),
            Counter("files_upload_errors", "Ошибки загрузки"), Counter("files_download_errors", "Ошибки скачивания")),
        Service("BarkFluff.Identity", "Identity", true,
            Counter("accounts_confirmed", "Подтверждённые аккаунты"), Counter("auth_login_success", "Успешные входы"),
            Counter("auth_login_failed", "Неуспешные входы"), Counter("password_resets_confirmed", "Успешные сбросы пароля"),
            Counter("password_reset_confirmation_failed", "Ошибки сброса пароля")),
        Service("BarkFluff.Users", "Users", false,
            Counter("device_registrations", "Новые устройства"), Counter("profile_name_updates", "Изменения профиля"),
            Counter("profile_username_updates", "Изменения username"), Counter("user_searches", "Поиск пользователей"),
            Counter("user_search_errors", "Ошибки поиска"), Counter("prekey_bundle_registrations", "Зарегистрированные key bundles")),
        Service("BarkFluff.Updates", "Updates", true,
            Counter("new_messages_broadcast", "Доставленные realtime-сообщения"), Counter("new_messages_broadcast_errors", "Ошибки realtime-доставки"),
            Counter("push_notifications_sent", "Отправленные push"), Counter("push_notifications_errors", "Ошибки push"),
            Gauge("subscriptions_active_total", "Активные realtime-подписки")),
        Service("BarkFluff.Onliner", "Onliner", true,
            Gauge("online_users_count", "Пользователи онлайн"), Gauge("active_subscriptions", "Активные presence-подписки"),
            Counter("status_notifications_sent", "Presence-уведомления"), Counter("status_notification_errors", "Ошибки presence"),
            Counter("typing_notifications_sent", "Typing-уведомления"), Counter("typing_notification_errors", "Ошибки typing")),
        Service("BarkFluff.Calls", "Calls", false,
            Counter("calls_initiated", "Начатые звонки"), Counter("calls_answered", "Принятые звонки"), Counter("calls_rejected", "Отклонённые звонки"),
            Counter("calls_missed", "Пропущенные звонки"), Counter("calls_ended", "Завершённые звонки")),
        Service("BarkFluff.FastAuth", "FastAuth", false,
            Counter("sessions_generated", "Созданные FastAuth-сессии"), Counter("sessions_accepted", "Принятые FastAuth-сессии"),
            Counter("sessions_rejected", "Отклонённые FastAuth-сессии"), Counter("sessions_expired", "Истёкшие FastAuth-сессии")),
        Service("BarkFluff.Bots", "Bots", false,
            Counter("bot_api_messages_sent", "Сообщения ботов"), Counter("bot_updates_stored", "Сохранённые bot updates"),
            Counter("login_notifications_errors", "Ошибки bot-уведомлений")),
        Service("BarkFluff.Notification", "Notification", false,
            Counter("emails_sent", "Отправленные email"), Counter("emails_failed", "Ошибки email")),
        Service("BarkFluff.CloudMessaging", "CloudMessaging", false,
            Counter("push_jobs_received", "Полученные push-задачи"), Counter("push_target_devices", "Целевые устройства"),
            Counter("fcm_pushes_sent", "Отправленные FCM push"), Counter("fcm_pushes_failed", "Ошибки FCM push")),
        Service("BarkFluff.Federation", "Federation", false,
            Counter("s2s_requests_in", "Входящие S2S-события"), Counter("outbox_delivered", "Доставленные S2S-события"),
            Counter("outbox_retry", "Повторные S2S-доставки"), Counter("outbox_dispatch_errors", "Ошибки S2S-доставки"),
            Counter("outbox_deadletter.max_attempts", "S2S dead-letter"), Gauge("known_servers_active", "Известные серверы"), Gauge("presence_streams_out", "Активные presence streams")),
        Service("BarkFluff.ClientStorage", "ClientStorage", false,
            Counter("client_releases_uploaded", "Опубликованные релизы"), Counter("client_releases_downloaded", "Скачанные релизы"),
            Counter("client_storage_upload_bytes", "Входящий трафик релизов", "bytes"), Counter("client_storage_download_bytes", "Исходящий трафик релизов", "bytes"),
            Counter("client_cache_hits", "Cache hit"), Counter("client_cache_misses", "Cache miss"), Counter("client_storage_errors", "Ошибки хранилища")),
        Service("BarkFluff.Navigator", "Navigator", false,
            Counter("server_registrations", "Регистрации серверов"), Counter("server_lookups", "Поиск серверов")),
        Service("BarkFluff.Configuration", "Configuration", false,
            Counter("config_get_success", "Чтения конфигурации"), Counter("config_update_success", "Успешные изменения конфигурации"), Counter("config_update_errors", "Ошибки изменения конфигурации"), Gauge("configurations_total", "Записи конфигурации")),
        Service("BarkFluff.Beacon", "Beacon", false,
            Counter("server_info_requests", "Запросы server info"), Counter("navigator_registrations", "Регистрации Navigator"), Counter("navigator_registration_errors", "Ошибки регистрации Navigator")),
        Service("BarkFluff.Web", "Web", false, Counter("http_requests_total", "HTTP-запросы"), Counter("http_requests_errors", "HTTP-ошибки")),
        Service("BarkFluff.Developers", "Developers", false, Counter("grpc_requests_total", "gRPC-запросы"), Counter("grpc_requests_errors", "Ошибки gRPC")),
        Service("BarkFluff.WebServer", "WebServer", false,
            Counter("installer_downloads", "Скачивания инсталлятора"), Counter("support_requests", "Support-запросы"),
            Counter("http_requests_total", "HTTP-запросы"), Counter("http_requests_errors", "HTTP-ошибки"))
    ];

    public static MetricServiceDefinition? Find(string serviceName) =>
        Services.FirstOrDefault(x => string.Equals(x.Name, serviceName, StringComparison.OrdinalIgnoreCase));

    private static MetricServiceDefinition Service(string name, string title, bool expanded, params MetricDefinition[] metrics) =>
        new(name, title, expanded, metrics);

    private static MetricDefinition Counter(string id, string title, string unit = "count") => new(id, title, unit, "counter");
    private static MetricDefinition Gauge(string id, string title, string unit = "count") => new(id, title, unit, "gauge");
}

public sealed record MetricServiceDefinition(string Name, string Title, bool ExpandedByDefault, IReadOnlyList<MetricDefinition> Metrics);
public sealed record MetricDefinition(string Id, string Title, string Unit, string Kind);
