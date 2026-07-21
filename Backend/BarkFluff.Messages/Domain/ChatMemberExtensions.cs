namespace BarkFluff.Messages.Domain;

public static class ChatMemberExtensions
{
    // Локальные участники чата. Remote-участник fed-DM (этап 2.3) имеет UserId = NULL —
    // у него нет локального аккаунта, его нельзя включать в списки получателей внутренних событий.
    public static List<long> LocalUserIds(this IEnumerable<ChatMember> members)
        => members.Where(m => m.UserId.HasValue).Select(m => m.UserId!.Value).ToList();
}
