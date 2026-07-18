using BarkFluff.Federation.Domain.Enums;

namespace BarkFluff.Federation.Domain.Entities;

// Исходящее федеративное событие. Диспетчер (OutboxDispatcher) гарантирует at-least-once доставку
// с упорядочиванием per-(Destination, ChatId). События разных чатов едут независимо.
//
// PayloadBytes — это уже подписанный wire-format FederationEvent (с проставленным origin_signature) —
// диспетчер отправляет его как есть, без пере-сериализации.
public class FederationOutbox
{
    public long Id { get; set; }

    public string Destination { get; set; } = string.Empty;

    /// <summary>
    /// Чат для упорядочивания. NULL = вне-чатовое событие (профильные, этап 2.9).
    /// </summary>
    public Guid? ChatId { get; set; }

    public Guid EventId { get; set; }

    public string EventType { get; set; } = string.Empty;

    public byte[] PayloadBytes { get; set; } = Array.Empty<byte>();

    public DateTime CreatedAt { get; set; }

    public int Attempts { get; set; }

    public DateTime NextAttemptAt { get; set; }

    public OutboxStatus Status { get; set; } = OutboxStatus.Pending;

    /// <summary>
    /// Для RETRY — последняя ошибка; для DeadLetter — финальная причина/код.
    /// </summary>
    public string? LastError { get; set; }
}
