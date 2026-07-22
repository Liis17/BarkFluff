using BarkFluff.Shared.Queue.Federation;

namespace BarkFluff.Messages.Domain;

public static class ChatMemberExtensions
{
    // Локальные участники чата. Remote-участник fed-DM (этап 2.3) имеет UserId = NULL —
    // у него нет локального аккаунта, его нельзя включать в списки получателей внутренних событий.
    public static List<long> LocalUserIds(this IEnumerable<ChatMember> members)
        => members.Where(m => m.UserId.HasValue).Select(m => m.UserId!.Value).ToList();

    // Remote-участники fed-DM: ServerName задан, UserId = NULL. Пара к LocalUserIds() —
    // используется при рассылке федеративных событий (SendMessage/Edit/Delete/MarkAsRead).
    public static List<FederatedParticipant> RemoteParticipants(this IEnumerable<ChatMember> members)
        => members
            .Where(m => !string.IsNullOrEmpty(m.ServerName) && m.UserUuid.HasValue)
            .Select(m => new FederatedParticipant { Uuid = m.UserUuid!.Value, ServerName = m.ServerName! })
            .ToList();
}
