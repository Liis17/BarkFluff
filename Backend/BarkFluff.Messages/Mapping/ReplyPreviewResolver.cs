using BarkFluff.Messages.Persistence.Services;
using BarkFluff.Proto.Shared;
using BarkFluff.Proto.Users;

namespace BarkFluff.Messages.Mapping;

/// <summary>
/// Собирает превью цитируемых сообщений для страницы выдачи.
///
/// Ответ хранится ссылкой, а не снапшотом, поэтому превью строится заново на каждой выдаче.
/// Это и есть смысл разделения: правка оригинала сразу видна в цитате, а удаление скрывает его
/// текст (раньше снапшот в чужом сообщении переживал удаление оригинала).
///
/// Стоимость — два батч-запроса на страницу независимо от количества ответов в ней:
/// один в БД за оригиналами, один в Users за именами.
/// </summary>
public class ReplyPreviewResolver
{
    /// <summary>
    /// Ограничение длины превью. Полный текст оригинала клиенту в цитате не нужен, а сообщение
    /// может быть на 4096 символов — страница из 50 ответов иначе раздувала бы ответ вчетверо.
    /// </summary>
    private const int MaxPreviewLength = 200;

    private readonly MessagesStorage _messagesStorage;
    private readonly UsersServerApi.UsersServerApiClient _usersServerApiClient;

    public ReplyPreviewResolver(
        MessagesStorage messagesStorage,
        UsersServerApi.UsersServerApiClient usersServerApiClient)
    {
        _messagesStorage = messagesStorage;
        _usersServerApiClient = usersServerApiClient;
    }

    /// <summary>
    /// Возвращает превью по id ОРИГИНАЛА (не отвечающего сообщения). Пустой словарь, если среди
    /// сообщений нет ни одного ответа — обычный случай, лишних запросов в нём не делается.
    /// </summary>
    public async Task<IReadOnlyDictionary<long, ReplyInfo>> ResolveAsync(IEnumerable<Domain.Message> messages)
    {
        var replyTargetIds = messages
            .Where(m => m.ReplyToMessageId.HasValue)
            .Select(m => m.ReplyToMessageId!.Value)
            .Distinct()
            .ToList();

        if (replyTargetIds.Count == 0)
            return new Dictionary<long, ReplyInfo>();

        // Намеренно НЕ GetMessagesByIds: тот фильтрует удалённые, а удалённый оригинал нужно
        // показать как «сообщение удалено», а не как отсутствующую цитату.
        var originals = await _messagesStorage.GetMessagesByIdsIncludingDeletedAsync(replyTargetIds);

        var senderIds = originals
            .Where(m => m is { IsDeleted: false, SenderId: not null })
            .Select(m => m.SenderId!.Value)
            .Distinct()
            .ToList();

        var namesById = new Dictionary<long, string>();
        if (senderIds.Count > 0)
        {
            var usersResponse = await _usersServerApiClient.ListByIdsAsync(
                new ListByIdsRequest { Ids = { senderIds } });

            foreach (var user in usersResponse.Users)
                namesById[user.Id] = $"{user.FirstName} {user.LastName}";
        }

        var previews = new Dictionary<long, ReplyInfo>(originals.Count);

        foreach (var original in originals)
        {
            var preview = new ReplyInfo
            {
                MessageId = original.Id,
                SenderId = original.SenderId ?? 0,
                IsDeleted = original.IsDeleted,
            };

            if (original.FederatedId.HasValue)
                preview.FederatedMessageId = original.FederatedId.Value.ToString();

            // У удалённого не отдаём ни текст, ни автора, ни тип вложения: цитата не должна
            // становиться способом прочитать удалённое сообщение.
            if (!original.IsDeleted)
            {
                preview.SenderName = original.SenderId is { } senderId
                    ? namesById.GetValueOrDefault(senderId, string.Empty)
                    : string.Empty;

                preview.TextPreview = Truncate(original.Content?.Text);

                var firstAttachment = original.Content?.Attachments?
                    .FirstOrDefault(a => a.Type != Domain.MessageAttachmentType.ForwardedMessage);

                if (firstAttachment is not null)
                    preview.FirstAttachmentType = (MessageAttachmentType)(int)firstAttachment.Type;
            }

            previews[original.Id] = preview;
        }

        return previews;
    }

    private static string Truncate(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        return text.Length <= MaxPreviewLength ? text : text[..MaxPreviewLength];
    }
}
