using Barkfluff.AdminPanel.Models;

using BarkFluff.Proto.Messages;
using BarkFluff.Proto.Users;

namespace Barkfluff.AdminPanel.Endpoints;

public static class ChatsEndpoints
{
    public static void MapChatsEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/chats")
            .WithTags("Chats");

        // GET /api/chats/{chatId}
        group.MapGet("/{chatId}", async (
            string chatId,
            MessagesServerApi.MessagesServerApiClient messagesClient,
            UsersServerApi.UsersServerApiClient usersClient,
            HttpContext context) =>
        {
            if (context.Items["AuthToken"] is not AuthToken)
                return Results.Unauthorized();

            var chatInfo = await messagesClient.GetChatInfoServerAsync(
                new GetChatInfoServerRequest { ChatId = chatId });

            if (!chatInfo.Found)
                return Results.NotFound();

            var members = Enumerable.Empty<object>();
            if (chatInfo.MemberIds.Count > 0)
            {
                var usersResponse = await usersClient.ListByIdsAsync(
                    new ListByIdsRequest { Ids = { chatInfo.MemberIds } });

                members = usersResponse.Users.Select(u => new
                {
                    id = u.Id,
                    firstName = u.FirstName,
                    lastName = u.LastName,
                    username = u.Username,
                    profilePicturePreview = u.ProfilePicturePreview
                });
            }

            if (chatInfo.IsGroupChat)
            {
                return Results.Ok(new
                {
                    chatId,
                    isGroup = true,
                    title = chatInfo.Title,
                    picture = chatInfo.Picture,
                    members
                });
            }

            return Results.Ok(new
            {
                chatId,
                isGroup = false,
                members
            });
        })
        .WithName("GetChatDetails");
    }
}
