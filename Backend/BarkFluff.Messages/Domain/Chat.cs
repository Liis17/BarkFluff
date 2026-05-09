using System.ComponentModel.DataAnnotations;

namespace BarkFluff.Messages.Domain;

public class Chat
{
    [Key]
    public Guid Id { get; set; }

    public string? Title { get; set; }

    public string? Picture { get; set; }

    public bool IsGroupChat { get; set; }

    public Message? LastMessage { get; set; }

    public List<ChatMember>? Members { get; set; }

    public int CountUnread { get; set; }

    public long? FirstUnreadMessageId { get; set; }

    public ChatType Type { get; set; } = ChatType.Regular;

    public byte[]? KdfSalt { get; set; }

    public byte[]? PassphraseVerifier { get; set; }
}