using System.Security.Cryptography;
using System.Text;

namespace BarkFluff.Settings.Catalog;

public enum SetupInputType
{
    Text,
    TextArea,
    Color,
    Host,
    Integer,
    Email,
    Secret,
    Url,
    DnsName,
    FingerprintList,
    Boolean
}

public enum SetupRequirement
{
    None,
    Always,
    FederationEnabled
}

public sealed record SetupFieldMetadata(
    string GroupId,
    int Order,
    string Label,
    string Description,
    SetupInputType InputType,
    SetupRequirement Requirement,
    string ValidatorId,
    string Placeholder = "");

public sealed record SetupGroupMetadata(
    string Id,
    int Order,
    string Title,
    string Description);

public static class SettingsSetupMetadata
{
    public static IReadOnlyList<SetupGroupMetadata> Groups { get; } =
    [
        new("server", 10, "Сведения о сервере", "Эти данные увидят пользователи при выборе вашей ноды."),
        new("email", 20, "Почтовая доставка", "SMTP нужен для системных писем и уведомлений."),
        new("media", 30, "Публичный адрес медиа", "Адрес, по которому клиенты будут получать медиафайлы."),
        new("federation", 40, "Федерация", "Параметры связи с другими нодами BarkFluff.")
    ];

    public static SetupFieldMetadata Server(string key) => key switch
    {
        "Name" => new("server", 10, "Название сервера", "Внутреннее имя ноды, отображаемое в административных списках.", SetupInputType.Text, SetupRequirement.Always, "server-name", "Например, BarkFluff Home"),
        "Description" => new("server", 20, "Описание", "Коротко опишите назначение или особенности этой ноды.", SetupInputType.TextArea, SetupRequirement.Always, "description", "Например, домашний сервер сообщества"),
        "PublicName" => new("server", 30, "Публичное название", "Название, которое будет показано пользователям в списке серверов.", SetupInputType.Text, SetupRequirement.Always, "public-name", "Например, BarkFluff"),
        "Location" => new("server", 40, "Расположение", "Город, регион или дата-центр сервера.", SetupInputType.Text, SetupRequirement.Always, "location", "Например, Москва"),
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, "Unknown server setup field.")
    };

    public static SetupFieldMetadata Color(string key) => key switch
    {
        "Lite" => new("server", 50, "Светлый цвет", "Светлый оттенок палитры сервера для интерфейса клиентов.", SetupInputType.Color, SetupRequirement.Always, "color", "#F5E6DF"),
        "Main" => new("server", 60, "Основной цвет", "Главный акцентный цвет бренда сервера.", SetupInputType.Color, SetupRequirement.Always, "color", "#8C351C"),
        "Hard" => new("server", 70, "Тёмный цвет", "Тёмный оттенок палитры для контраста и состояний навигации.", SetupInputType.Color, SetupRequirement.Always, "color", "#5C1D0D"),
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, "Unknown server color setup field.")
    };

    public static SetupFieldMetadata Email(string key) => key switch
    {
        "Host" => new("email", 10, "SMTP-сервер", "Имя хоста или IP-адрес SMTP-сервера без схемы и пути.", SetupInputType.Host, SetupRequirement.Always, "smtp-host", "smtp.example.com"),
        "Port" => new("email", 20, "SMTP-порт", "Порт SMTP-сервера. Обычно 587 для STARTTLS или 465 для TLS.", SetupInputType.Integer, SetupRequirement.Always, "port", "587"),
        "SenderEmail" => new("email", 30, "Адрес отправителя", "Адрес, от имени которого будут уходить системные письма.", SetupInputType.Email, SetupRequirement.Always, "email", "noreply@example.com"),
        "SenderPassword" => new("email", 40, "Пароль отправителя", "Пароль SMTP-аккаунта. Значение не показывается после сохранения.", SetupInputType.Secret, SetupRequirement.Always, "secret"),
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, "Unknown email setup field.")
    };

    public static SetupFieldMetadata Media() => new(
        "media", 10, "Публичный адрес медиа",
        "HTTPS-origin без пути и query. Этот адрес используется клиентами для загрузки медиа.",
        SetupInputType.Url, SetupRequirement.Always, "public-https-origin", "https://files.example.com");

    public static SetupFieldMetadata Federation(string key) => key switch
    {
        "Enabled" => new("federation", 10, "Включить федерацию", "Включайте только если сервер должен обмениваться данными с другими нодами.", SetupInputType.Boolean, SetupRequirement.None, "boolean"),
        "ServerName" => new("federation", 20, "Имя ноды в федерации", "Канонический DNS-домен ноды. IP-адреса и localhost не подходят.", SetupInputType.DnsName, SetupRequirement.FederationEnabled, "federation-server-name", "chat.example.com"),
        "ExternalEndpoint" => new("federation", 30, "Публичный S2S-адрес", "HTTPS-origin, по которому другие ноды подключаются к федерации.", SetupInputType.Url, SetupRequirement.FederationEnabled, "public-https-origin", "https://chat.example.com"),
        "TlsSpkiSha256" => new("federation", 40, "SPKI SHA-256 отпечаток", "Base64-отпечаток SubjectPublicKeyInfo сертификата Nginx; несколько значений разделяйте запятыми.", SetupInputType.FingerprintList, SetupRequirement.FederationEnabled, "spki-sha256", "Base64 SHA-256"),
        "WellKnownPort" => new("federation", 50, "Порт well-known", "HTTP/1-порт endpoint /.well-known/barkfluff внутри контейнера Federation.", SetupInputType.Integer, SetupRequirement.FederationEnabled, "port", "7031"),
        "KeyRotationOverlapDays" => new("federation", 60, "Перекрытие ключей, дни", "Сколько дней старый ключ остаётся действительным после ротации.", SetupInputType.Integer, SetupRequirement.FederationEnabled, "rotation-days", "30"),
        "SignatureWindowSeconds" => new("federation", 70, "Окно подписи, секунды", "Максимальный возраст или упреждение подписанного S2S-запроса.", SetupInputType.Integer, SetupRequirement.FederationEnabled, "signature-window", "300"),
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, "Unknown federation setup field.")
    };

    public static bool IsApplicable(SetupRequirement requirement, bool federationEnabled) =>
        requirement is SetupRequirement.Always
        || requirement is SetupRequirement.FederationEnabled && federationEnabled;

    public static string GetFieldId(SettingsCatalogEntry entry) =>
        $"{(int)entry.ServiceId}:{entry.StorageKey}";

    public static string ComputeFingerprint(IEnumerable<SettingsCatalogEntry> entries)
    {
        var snapshot = string.Join('\n', entries
            .Where(entry => entry.Setup is not null)
            .OrderBy(entry => GetFieldId(entry), StringComparer.Ordinal)
            .Select(entry =>
            {
                var metadata = entry.Setup!;
                return string.Join('|',
                    GetFieldId(entry), metadata.GroupId, metadata.Order, metadata.InputType,
                    metadata.Requirement, metadata.ValidatorId, metadata.Label, metadata.Description,
                    metadata.Placeholder, entry.IsSensitive, entry.RequiresManualValue);
            }));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(snapshot)));
    }
}
