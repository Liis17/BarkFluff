namespace BarkFluff.Shared.Queue.Federation;

/// <summary>
/// Снапшот метаданных вложения федеративного сообщения (этап 3.1, docs/rearch/06-files.md).
/// </summary>
/// <remarks>
/// Файлы между нодами <b>не реплицируются</b> — байты живут только на origin-ноде. Реплицируется
/// ровно этот снапшот, чтобы принимающая нода рендерила сообщение (имя, размер, тип, превью,
/// размеры картинки) без единого сетевого похода на чужую ноду. Сами байты тянутся отдельно и
/// только когда пользователь действительно открывает вложение (этапы 3.2/3.3).
///
/// Заполняется сервисом Messages только для fed-чатов; Federation маппит это в
/// <c>FederatedFileRef</c> исходящего события.
/// </remarks>
public class FederatedFileRefInfo
{
    public string OriginServer { get; set; } = string.Empty;

    /// <summary>Guid файла на origin-ноде в строковой форме.</summary>
    public string FileId { get; set; } = string.Empty;

    public string? FileName { get; set; }

    public long SizeBytes { get; set; }

    /// <summary>Значения <c>barkfluff.shared.MessageAttachmentType</c>.</summary>
    public int AttachmentType { get; set; }

    public string? PreviewFileId { get; set; }

    public int? ImageWidth { get; set; }

    public int? ImageHeight { get; set; }
}
