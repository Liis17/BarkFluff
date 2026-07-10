namespace BarkFluff.Messages.Domain;

/// <summary>Последнее прочитанное зашифрованное сообщение пользователя в чате.</summary>
public class PrivateChatReadState
{
    public Guid ChatId { get; set; }

    public long UserId { get; set; }

    public long LastReadMessageId { get; set; }
}
