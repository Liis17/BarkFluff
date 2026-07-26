namespace BarkFluff.Files.Domain;

public class TempFile
{
    public Guid Id { get; set; }

    public Guid OriginalFileId { get; set; }

    public DateTime ExpiresAt { get; set; }

    // ---- federated-вложение (этап 3.3) ----
    // Байты живут на чужой ноде; capability-ссылка та же, но скачивание идёт другой веткой.

    /// <summary>NULL = локальный файл (прежнее поведение), NOT NULL = байты на origin-ноде.</summary>
    public string? OriginServer { get; set; }

    /// <summary>
    /// Снапшот из Messages (3.1), чтобы скачивание не ходило туда второй раз:
    /// имя — для Content-Disposition, размер — для отсечения по объёму (риск №44).
    /// </summary>
    public string? FileName { get; set; }

    public long? SizeBytes { get; set; }

    /// <summary>Значения <c>barkfluff.shared.MessageAttachmentType</c> — fallback для Content-Type.</summary>
    public int? AttachmentType { get; set; }
}
