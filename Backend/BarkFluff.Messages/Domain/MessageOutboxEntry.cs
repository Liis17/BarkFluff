namespace BarkFluff.Messages.Domain;

/// <summary>
/// Durable message event written with its corresponding state change.
/// Delivery is at-least-once; EventId remains stable across dispatcher retries.
/// </summary>
public class MessageOutboxEntry
{
    public long Id { get; set; }

    public Guid EventId { get; set; }

    public long MessageId { get; set; }

    public MessageOutboxEventKind EventKind { get; set; }

    public byte[] Payload { get; set; } = [];

    public string? TraceParent { get; set; }

    public string? TraceState { get; set; }

    public string? CorrelationId { get; set; }

    public DateTime CreatedAt { get; set; }

    public int Attempts { get; set; }

    public DateTime NextAttemptAt { get; set; }

    public MessageOutboxStatus Status { get; set; } = MessageOutboxStatus.Pending;

    public string? LastError { get; set; }
}

public enum MessageOutboxEventKind
{
    NewMessage = 0,
    ReadBy = 1,
}

public enum MessageOutboxStatus
{
    Pending = 0,
    Delivered = 1,
    DeadLetter = 2,
    Processing = 3,
}
