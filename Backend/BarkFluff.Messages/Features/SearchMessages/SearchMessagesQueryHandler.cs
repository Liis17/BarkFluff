using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Messages.Persistence.Services;
using BarkFluff.Proto.Messages;
using BarkFluff.Proto.Users;
using Google.Protobuf.WellKnownTypes;
using MediatR;

namespace BarkFluff.Messages.Features.SearchMessages;

public sealed class SearchMessagesQueryHandler(
    UserContext userContext,
    MessagesStorage messagesStorage,
    UsersServerApi.UsersServerApiClient usersClient)
    : IRequestHandler<SearchMessagesQuery, SearchMessagesResponse>
{
    public async Task<SearchMessagesResponse> Handle(SearchMessagesQuery request, CancellationToken cancellationToken)
    {
        var pageSize = Math.Clamp(request.Filter.PageSize, 1, 50);
        var found = await messagesStorage.SearchMessages(userContext.UserId, request.Filter, cancellationToken);
        var response = new SearchMessagesResponse();
        if (found.Count == 0) return response;
        var messages = found.Take(pageSize).ToList();
        var chats = await messagesStorage.GetSearchChats(
            userContext.UserId, messages.Select(message => message.ChatId).Distinct().ToArray(), cancellationToken);
        var chatsById = chats.ToDictionary(chat => chat.Id);
        var localIds = messages.Where(message => message.SenderId.HasValue).Select(message => message.SenderId!.Value)
            .Concat(chats.Where(chat => !chat.IsGroupChat && chat.PeerUserUuid is null)
                .Select(chat => chat.PeerUserId ?? userContext.UserId))
            .Concat(chats.Where(chat => chat.PeerUserId.HasValue).Select(chat => chat.PeerUserId!.Value))
            .Distinct().ToArray();
        var remoteUuids = messages.Where(message => message.SenderId is null && message.SenderUuid.HasValue)
            .Select(message => message.SenderUuid!.Value.ToString())
            .Concat(chats.Where(chat => chat.PeerUserId is null && chat.PeerUserUuid.HasValue)
                .Select(chat => chat.PeerUserUuid!.Value.ToString()))
            .Distinct().ToArray();
        var localNames = new Dictionary<long, string>();
        if (localIds.Length > 0)
        {
            var profiles = await usersClient.ListByIdsAsync(
                new ListByIdsRequest { Ids = { localIds } }, cancellationToken: cancellationToken);
            foreach (var user in profiles.Users)
                localNames[user.Id] = DisplayName(user.FirstName, user.LastName, user.Username);
        }
        var remoteNames = new Dictionary<string, string>();
        if (remoteUuids.Length > 0)
        {
            var profiles = await usersClient.GetUsersByUuidAsync(
                new GetUsersByUuidRequest { Uuids = { remoteUuids } }, cancellationToken: cancellationToken);
            foreach (var user in profiles.Users)
                remoteNames[user.Uuid] = DisplayName(user.FirstName, user.LastName, user.Username);
        }
        foreach (var message in messages)
        {
            if (!chatsById.TryGetValue(message.ChatId, out var chat)) continue;
            var peerId = chat.PeerUserId ?? (chat.PeerUserUuid is null ? userContext.UserId : 0L);
            var title = chat.IsGroupChat ? chat.Title ?? string.Empty
                : peerId > 0 ? localNames.GetValueOrDefault(peerId, string.Empty)
                : remoteNames.GetValueOrDefault(chat.PeerUserUuid?.ToString() ?? string.Empty, string.Empty);
            var senderUuid = message.SenderUuid?.ToString() ?? string.Empty;
            response.Hits.Add(new MessageSearchHit
            {
                MessageId = message.Id,
                ChatId = message.ChatId.ToString(),
                ChatTitle = title,
                IsGroupChat = chat.IsGroupChat,
                ChatPictureFileId = Guid.TryParse(chat.Picture, out var pictureId) ? pictureId.ToString() : string.Empty,
                OtherUserId = chat.IsGroupChat ? 0L : peerId,
                Author = new MessageSearchAuthor
                {
                    UserId = message.SenderId ?? 0L,
                    UserUuid = senderUuid,
                    DisplayName = message.SenderId is { } senderId
                        ? localNames.GetValueOrDefault(senderId, string.Empty)
                        : remoteNames.GetValueOrDefault(senderUuid, string.Empty),
                },
                SentAt = Timestamp.FromDateTime(DateTime.SpecifyKind(message.SentAt, DateTimeKind.Utc)),
                Text = message.Content?.Text ?? string.Empty,
                AttachmentTypes = { message.Content?.Attachments?
                    .Where(attachment => attachment.Type != Domain.MessageAttachmentType.Unknown
                        && attachment.Type != Domain.MessageAttachmentType.ForwardedMessage)
                    .Select(attachment => (Proto.Shared.MessageAttachmentType)attachment.Type).Distinct() ?? [] },
            });
        }
        if (found.Count > pageSize && response.Hits.Count > 0)
        {
            var last = response.Hits[^1];
            response.NextCursor = new MessageSearchCursor { MessageId = last.MessageId, SentAt = last.SentAt };
        }
        return response;
    }

    private static string DisplayName(string firstName, string lastName, string username)
    {
        var name = $"{firstName} {lastName}".Trim();
        return string.IsNullOrEmpty(name) ? username : name;
    }
}
