using System.ComponentModel.DataAnnotations;

namespace BarkFluff.Messages.Domain;

public class ChatMember
{
    [Key]
    public long Id { get; set; }

    // NULL для remote-участника fed-DM (этап 2.3) — у него нет локального аккаунта.
    public long? UserId { get; set; }

    public DateTime JoinedAt { get; set; }

    public Guid ChatId { get; set; }

    public Chat Chat { get; set; }

    public Guid? UserUuid { get; set; }

    // Домен ноды remote-участника (punycode A-label lowercase); NULL для локального участника.
    public string? ServerName { get; set; }
}