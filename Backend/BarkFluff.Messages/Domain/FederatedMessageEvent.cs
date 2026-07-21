using System.ComponentModel.DataAnnotations;

namespace BarkFluff.Messages.Domain;

// Последний применённый state-event входящего fed-сообщения (wire-байты подписанного
// FederationEvent) — нужен catch-up (2.6): позволяет отдавать историю с той же подписью origin,
// с которой событие пришло изначально. Пишем начиная с 2.3 (docs/rearch/phase-2/step-2.3, Изменение 1).
public class FederatedMessageEvent
{
    public Guid ChatId { get; set; }

    public Guid FederatedId { get; set; }

    [MaxLength(int.MaxValue)]
    public byte[] EventBytes { get; set; } = [];

    public DateTime ReceivedAt { get; set; }
}
