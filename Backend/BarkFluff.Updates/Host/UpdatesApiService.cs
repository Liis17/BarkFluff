namespace BarkFluff.Updates.Host;

using BarkFluff.GrpcServer.Metrics;

using Features.SubscribeNewMessages;

using Grpc.Core;

using GrpcServer.XAuth;

using Microsoft.AspNetCore.Authorization;

using Proto.Updates;

using Shared.Identity;

using System.Threading;
using System.Threading.Tasks;

[Authorize(Policy = nameof(TokenType.User))]
public class UpdatesApiService : BarkFluff.Proto.Updates.UpdatesApi.UpdatesApiBase
{
    private readonly UserContext _userContext;
    private readonly StreamSubscriptionsManager _newMessagesSubscriptionsManager;
    private readonly Features.SubscribeMessagesRead.StreamSubscriptionsManager _newReadBySubscriptionsManager;
    private readonly Features.SubscribeMessagesEdited.StreamSubscriptionsManager _editedSubscriptionsManager;
    private readonly Features.SubscribeMessagesDeleted.StreamSubscriptionsManager _deletedSubscriptionsManager;
    private readonly Features.SubscribeMessagesPinned.StreamSubscriptionsManager _pinnedSubscriptionsManager;
    private readonly Features.SubscribeMessagesUnpinned.StreamSubscriptionsManager _unpinnedSubscriptionsManager;
    private readonly Features.SubscribeAllMessagesUnpinned.StreamSubscriptionsManager _allUnpinnedSubscriptionsManager;
    private readonly Features.SubscribePrivateMessages.StreamSubscriptionsManager _privateMessagesSubscriptionsManager;
    private readonly Features.SubscribePrivateMessageEdits.StreamSubscriptionsManager _privateMessageEditsSubscriptionsManager;
    private readonly Features.SubscribePrivateMessageDeletes.StreamSubscriptionsManager _privateMessageDeletesSubscriptionsManager;
    private readonly Features.SubscribePrivateMessagesRead.StreamSubscriptionsManager _privateMessagesReadSubscriptionsManager;
    private readonly Features.SubscribePrivateChatInvites.StreamSubscriptionsManager _privateChatInvitesSubscriptionsManager;
    private readonly Features.SubscribePrivateChatInviteResolutions.StreamSubscriptionsManager _privateChatInviteResolutionsSubscriptionsManager;
    private readonly Features.SubscribeSecretChatInvites.StreamSubscriptionsManager _secretChatInvitesSubscriptionsManager;
    private readonly Features.SubscribeSecretChatResolutions.StreamSubscriptionsManager _secretChatResolutionsSubscriptionsManager;
    private readonly Features.SubscribeSecretMessages.StreamSubscriptionsManager _secretMessagesSubscriptionsManager;
    private readonly MetricsCollector _metrics;

    public UpdatesApiService(
        UserContext userContext,
        StreamSubscriptionsManager newMessagesSubscriptionsManager,
        Features.SubscribeMessagesRead.StreamSubscriptionsManager newReadBySubscriptionsManager,
        Features.SubscribeMessagesEdited.StreamSubscriptionsManager editedSubscriptionsManager,
        Features.SubscribeMessagesDeleted.StreamSubscriptionsManager deletedSubscriptionsManager,
        Features.SubscribeMessagesPinned.StreamSubscriptionsManager pinnedSubscriptionsManager,
        Features.SubscribeMessagesUnpinned.StreamSubscriptionsManager unpinnedSubscriptionsManager,
        Features.SubscribeAllMessagesUnpinned.StreamSubscriptionsManager allUnpinnedSubscriptionsManager,
        Features.SubscribePrivateMessages.StreamSubscriptionsManager privateMessagesSubscriptionsManager,
        Features.SubscribePrivateMessageEdits.StreamSubscriptionsManager privateMessageEditsSubscriptionsManager,
        Features.SubscribePrivateMessageDeletes.StreamSubscriptionsManager privateMessageDeletesSubscriptionsManager,
        Features.SubscribePrivateMessagesRead.StreamSubscriptionsManager privateMessagesReadSubscriptionsManager,
        Features.SubscribePrivateChatInvites.StreamSubscriptionsManager privateChatInvitesSubscriptionsManager,
        Features.SubscribePrivateChatInviteResolutions.StreamSubscriptionsManager privateChatInviteResolutionsSubscriptionsManager,
        Features.SubscribeSecretChatInvites.StreamSubscriptionsManager secretChatInvitesSubscriptionsManager,
        Features.SubscribeSecretChatResolutions.StreamSubscriptionsManager secretChatResolutionsSubscriptionsManager,
        Features.SubscribeSecretMessages.StreamSubscriptionsManager secretMessagesSubscriptionsManager,
        MetricsCollector metrics)
    {
        _userContext = userContext;
        _newMessagesSubscriptionsManager = newMessagesSubscriptionsManager;
        _newReadBySubscriptionsManager = newReadBySubscriptionsManager;
        _editedSubscriptionsManager = editedSubscriptionsManager;
        _deletedSubscriptionsManager = deletedSubscriptionsManager;
        _pinnedSubscriptionsManager = pinnedSubscriptionsManager;
        _unpinnedSubscriptionsManager = unpinnedSubscriptionsManager;
        _allUnpinnedSubscriptionsManager = allUnpinnedSubscriptionsManager;
        _privateMessagesSubscriptionsManager = privateMessagesSubscriptionsManager;
        _privateMessageEditsSubscriptionsManager = privateMessageEditsSubscriptionsManager;
        _privateMessageDeletesSubscriptionsManager = privateMessageDeletesSubscriptionsManager;
        _privateMessagesReadSubscriptionsManager = privateMessagesReadSubscriptionsManager;
        _privateChatInvitesSubscriptionsManager = privateChatInvitesSubscriptionsManager;
        _privateChatInviteResolutionsSubscriptionsManager = privateChatInviteResolutionsSubscriptionsManager;
        _secretChatInvitesSubscriptionsManager = secretChatInvitesSubscriptionsManager;
        _secretChatResolutionsSubscriptionsManager = secretChatResolutionsSubscriptionsManager;
        _secretMessagesSubscriptionsManager = secretMessagesSubscriptionsManager;
        _metrics = metrics;
    }

    private long TotalActive =>
        _newMessagesSubscriptionsManager.ActiveCount
        + _newReadBySubscriptionsManager.ActiveCount
        + _editedSubscriptionsManager.ActiveCount
        + _deletedSubscriptionsManager.ActiveCount
        + _pinnedSubscriptionsManager.ActiveCount
        + _unpinnedSubscriptionsManager.ActiveCount
        + _allUnpinnedSubscriptionsManager.ActiveCount
        + _privateMessagesSubscriptionsManager.ActiveCount
        + _privateMessageEditsSubscriptionsManager.ActiveCount
        + _privateMessageDeletesSubscriptionsManager.ActiveCount
        + _privateMessagesReadSubscriptionsManager.ActiveCount
        + _privateChatInvitesSubscriptionsManager.ActiveCount
        + _privateChatInviteResolutionsSubscriptionsManager.ActiveCount
        + _secretChatInvitesSubscriptionsManager.ActiveCount
        + _secretChatResolutionsSubscriptionsManager.ActiveCount
        + _secretMessagesSubscriptionsManager.ActiveCount;

    private Guid RequireDeviceId()
    {
        if (string.IsNullOrEmpty(_userContext.DeviceId) || !Guid.TryParse(_userContext.DeviceId, out var deviceId))
        {
            throw new RpcException(new Status(StatusCode.Unauthenticated, "Device ID is required for this stream"));
        }
        return deviceId;
    }

    public override async Task SubscribeNewMessages(
        SubscribeNewMessagesRequest request,
        IServerStreamWriter<NewMessageEvent> responseStream,
        ServerCallContext context)
    {
        long userId = _userContext.UserId;

        var subscriptionId = _newMessagesSubscriptionsManager.RegisterSubscription(userId, responseStream);
        _metrics.Increment("new_messages_subscriptions_opened");
        _metrics.Increment("active_subscriptions"); // обратная совместимость
        _metrics.Set("new_messages_subscriptions_active", _newMessagesSubscriptionsManager.ActiveCount);
        _metrics.Set("subscriptions_active_total", TotalActive);

        try
        {
            await Task.Delay(Timeout.Infinite, context.CancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _newMessagesSubscriptionsManager.RemoveSubscription(userId, subscriptionId);
            _metrics.Increment("new_messages_subscriptions_closed");
            _metrics.Increment("active_subscriptions_removed"); // обратная совместимость
            _metrics.Set("new_messages_subscriptions_active", _newMessagesSubscriptionsManager.ActiveCount);
            _metrics.Set("subscriptions_active_total", TotalActive);
        }
    }

    public override async Task SubscribeMessagesRead(SubscribeMessagesReadRequest request, IServerStreamWriter<MessageReadEvent> responseStream,
        ServerCallContext context)
    {
        long userId = _userContext.UserId;

        var subscriptionId = _newReadBySubscriptionsManager.RegisterSubscription(userId, responseStream);
        _metrics.Increment("read_by_subscriptions_opened");
        _metrics.Increment("active_subscriptions"); // обратная совместимость
        _metrics.Set("read_by_subscriptions_active", _newReadBySubscriptionsManager.ActiveCount);
        _metrics.Set("subscriptions_active_total", TotalActive);

        try
        {
            await Task.Delay(Timeout.Infinite, context.CancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _newReadBySubscriptionsManager.RemoveSubscription(userId, subscriptionId);
            _metrics.Increment("read_by_subscriptions_closed");
            _metrics.Increment("active_subscriptions_removed"); // обратная совместимость
            _metrics.Set("read_by_subscriptions_active", _newReadBySubscriptionsManager.ActiveCount);
            _metrics.Set("subscriptions_active_total", TotalActive);
        }
    }

    public override async Task SubscribeMessagesEdited(SubscribeMessagesEditedRequest request,
        IServerStreamWriter<MessageEditedEvent> responseStream, ServerCallContext context)
    {
        long userId = _userContext.UserId;

        var subscriptionId = _editedSubscriptionsManager.RegisterSubscription(userId, responseStream);
        _metrics.Increment("messages_edited_subscriptions_opened");
        _metrics.Increment("active_subscriptions"); // обратная совместимость
        _metrics.Set("messages_edited_subscriptions_active", _editedSubscriptionsManager.ActiveCount);
        _metrics.Set("subscriptions_active_total", TotalActive);

        try
        {
            await Task.Delay(Timeout.Infinite, context.CancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _editedSubscriptionsManager.RemoveSubscription(userId, subscriptionId);
            _metrics.Increment("messages_edited_subscriptions_closed");
            _metrics.Increment("active_subscriptions_removed"); // обратная совместимость
            _metrics.Set("messages_edited_subscriptions_active", _editedSubscriptionsManager.ActiveCount);
            _metrics.Set("subscriptions_active_total", TotalActive);
        }
    }

    public override async Task SubscribeMessagesDeleted(SubscribeMessagesDeletedRequest request,
        IServerStreamWriter<MessageDeletedEvent> responseStream, ServerCallContext context)
    {
        long userId = _userContext.UserId;

        var subscriptionId = _deletedSubscriptionsManager.RegisterSubscription(userId, responseStream);
        _metrics.Increment("messages_deleted_subscriptions_opened");
        _metrics.Increment("active_subscriptions"); // обратная совместимость
        _metrics.Set("messages_deleted_subscriptions_active", _deletedSubscriptionsManager.ActiveCount);
        _metrics.Set("subscriptions_active_total", TotalActive);

        try
        {
            await Task.Delay(Timeout.Infinite, context.CancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _deletedSubscriptionsManager.RemoveSubscription(userId, subscriptionId);
            _metrics.Increment("messages_deleted_subscriptions_closed");
            _metrics.Increment("active_subscriptions_removed"); // обратная совместимость
            _metrics.Set("messages_deleted_subscriptions_active", _deletedSubscriptionsManager.ActiveCount);
            _metrics.Set("subscriptions_active_total", TotalActive);
        }
    }

    public override async Task SubscribeMessagesPinned(SubscribeMessagesPinnedRequest request,
        IServerStreamWriter<MessagePinnedEvent> responseStream, ServerCallContext context)
    {
        long userId = _userContext.UserId;

        var subscriptionId = _pinnedSubscriptionsManager.RegisterSubscription(userId, responseStream);
        _metrics.Increment("messages_pinned_subscriptions_opened");
        _metrics.Increment("active_subscriptions"); // обратная совместимость
        _metrics.Set("messages_pinned_subscriptions_active", _pinnedSubscriptionsManager.ActiveCount);
        _metrics.Set("subscriptions_active_total", TotalActive);

        try
        {
            await Task.Delay(Timeout.Infinite, context.CancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _pinnedSubscriptionsManager.RemoveSubscription(userId, subscriptionId);
            _metrics.Increment("messages_pinned_subscriptions_closed");
            _metrics.Increment("active_subscriptions_removed"); // обратная совместимость
            _metrics.Set("messages_pinned_subscriptions_active", _pinnedSubscriptionsManager.ActiveCount);
            _metrics.Set("subscriptions_active_total", TotalActive);
        }
    }

    public override async Task SubscribeMessagesUnpinned(SubscribeMessagesUnpinnedRequest request,
        IServerStreamWriter<MessageUnpinnedEvent> responseStream, ServerCallContext context)
    {
        long userId = _userContext.UserId;

        var subscriptionId = _unpinnedSubscriptionsManager.RegisterSubscription(userId, responseStream);
        _metrics.Increment("messages_unpinned_subscriptions_opened");
        _metrics.Increment("active_subscriptions"); // обратная совместимость
        _metrics.Set("messages_unpinned_subscriptions_active", _unpinnedSubscriptionsManager.ActiveCount);
        _metrics.Set("subscriptions_active_total", TotalActive);

        try
        {
            await Task.Delay(Timeout.Infinite, context.CancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _unpinnedSubscriptionsManager.RemoveSubscription(userId, subscriptionId);
            _metrics.Increment("messages_unpinned_subscriptions_closed");
            _metrics.Increment("active_subscriptions_removed"); // обратная совместимость
            _metrics.Set("messages_unpinned_subscriptions_active", _unpinnedSubscriptionsManager.ActiveCount);
            _metrics.Set("subscriptions_active_total", TotalActive);
        }
    }

    public override async Task SubscribeAllMessagesUnpinned(SubscribeAllMessagesUnpinnedRequest request,
        IServerStreamWriter<AllMessagesUnpinnedEvent> responseStream, ServerCallContext context)
    {
        long userId = _userContext.UserId;

        var subscriptionId = _allUnpinnedSubscriptionsManager.RegisterSubscription(userId, responseStream);
        _metrics.Increment("all_messages_unpinned_subscriptions_opened");
        _metrics.Increment("active_subscriptions"); // обратная совместимость
        _metrics.Set("all_messages_unpinned_subscriptions_active", _allUnpinnedSubscriptionsManager.ActiveCount);
        _metrics.Set("subscriptions_active_total", TotalActive);

        try
        {
            await Task.Delay(Timeout.Infinite, context.CancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _allUnpinnedSubscriptionsManager.RemoveSubscription(userId, subscriptionId);
            _metrics.Increment("all_messages_unpinned_subscriptions_closed");
            _metrics.Increment("active_subscriptions_removed"); // обратная совместимость
            _metrics.Set("all_messages_unpinned_subscriptions_active", _allUnpinnedSubscriptionsManager.ActiveCount);
            _metrics.Set("subscriptions_active_total", TotalActive);
        }
    }

    // -- Приватные чаты (user-scope) ----------------------------------------

    public override async Task SubscribePrivateMessages(SubscribePrivateMessagesRequest request,
        IServerStreamWriter<NewEncryptedMessageEvent> responseStream, ServerCallContext context)
    {
        long userId = _userContext.UserId;
        var subscriptionId = _privateMessagesSubscriptionsManager.RegisterSubscription(userId, responseStream);
        _metrics.Increment("private_messages_subscriptions_opened");
        _metrics.Set("private_messages_subscriptions_active", _privateMessagesSubscriptionsManager.ActiveCount);
        _metrics.Set("subscriptions_active_total", TotalActive);

        try
        {
            await Task.Delay(Timeout.Infinite, context.CancellationToken);
        }
        catch (OperationCanceledException) { }
        finally
        {
            _privateMessagesSubscriptionsManager.RemoveSubscription(userId, subscriptionId);
            _metrics.Increment("private_messages_subscriptions_closed");
            _metrics.Set("private_messages_subscriptions_active", _privateMessagesSubscriptionsManager.ActiveCount);
            _metrics.Set("subscriptions_active_total", TotalActive);
        }
    }

    public override async Task SubscribePrivateMessageEdits(SubscribePrivateMessageEditsRequest request,
        IServerStreamWriter<EncryptedMessageEditedEvent> responseStream, ServerCallContext context)
    {
        long userId = _userContext.UserId;
        var subscriptionId = _privateMessageEditsSubscriptionsManager.RegisterSubscription(userId, responseStream);
        _metrics.Increment("private_message_edits_subscriptions_opened");
        _metrics.Set("private_message_edits_subscriptions_active", _privateMessageEditsSubscriptionsManager.ActiveCount);
        _metrics.Set("subscriptions_active_total", TotalActive);

        try
        {
            await Task.Delay(Timeout.Infinite, context.CancellationToken);
        }
        catch (OperationCanceledException) { }
        finally
        {
            _privateMessageEditsSubscriptionsManager.RemoveSubscription(userId, subscriptionId);
            _metrics.Increment("private_message_edits_subscriptions_closed");
            _metrics.Set("private_message_edits_subscriptions_active", _privateMessageEditsSubscriptionsManager.ActiveCount);
            _metrics.Set("subscriptions_active_total", TotalActive);
        }
    }

    public override async Task SubscribePrivateMessageDeletes(SubscribePrivateMessageDeletesRequest request,
        IServerStreamWriter<EncryptedMessageDeletedEvent> responseStream, ServerCallContext context)
    {
        long userId = _userContext.UserId;
        var subscriptionId = _privateMessageDeletesSubscriptionsManager.RegisterSubscription(userId, responseStream);
        _metrics.Increment("private_message_deletes_subscriptions_opened");
        _metrics.Set("private_message_deletes_subscriptions_active", _privateMessageDeletesSubscriptionsManager.ActiveCount);
        _metrics.Set("subscriptions_active_total", TotalActive);

        try
        {
            await Task.Delay(Timeout.Infinite, context.CancellationToken);
        }
        catch (OperationCanceledException) { }
        finally
        {
            _privateMessageDeletesSubscriptionsManager.RemoveSubscription(userId, subscriptionId);
            _metrics.Increment("private_message_deletes_subscriptions_closed");
            _metrics.Set("private_message_deletes_subscriptions_active", _privateMessageDeletesSubscriptionsManager.ActiveCount);
            _metrics.Set("subscriptions_active_total", TotalActive);
        }
    }

    public override async Task SubscribePrivateMessagesRead(SubscribePrivateMessagesReadRequest request,
        IServerStreamWriter<PrivateMessagesReadEvent> responseStream, ServerCallContext context)
    {
        long userId = _userContext.UserId;
        var subscriptionId = _privateMessagesReadSubscriptionsManager.RegisterSubscription(userId, responseStream);
        _metrics.Increment("private_messages_read_subscriptions_opened");
        _metrics.Set("private_messages_read_subscriptions_active", _privateMessagesReadSubscriptionsManager.ActiveCount);
        _metrics.Set("subscriptions_active_total", TotalActive);

        try
        {
            await Task.Delay(Timeout.Infinite, context.CancellationToken);
        }
        catch (OperationCanceledException) { }
        finally
        {
            _privateMessagesReadSubscriptionsManager.RemoveSubscription(userId, subscriptionId);
            _metrics.Increment("private_messages_read_subscriptions_closed");
            _metrics.Set("private_messages_read_subscriptions_active", _privateMessagesReadSubscriptionsManager.ActiveCount);
            _metrics.Set("subscriptions_active_total", TotalActive);
        }
    }

    public override async Task SubscribePrivateChatInvites(SubscribePrivateChatInvitesRequest request,
        IServerStreamWriter<PrivateChatInviteEvent> responseStream, ServerCallContext context)
    {
        long userId = _userContext.UserId;
        var subscriptionId = _privateChatInvitesSubscriptionsManager.RegisterSubscription(userId, responseStream);
        _metrics.Increment("private_chat_invites_subscriptions_opened");
        _metrics.Set("private_chat_invites_subscriptions_active", _privateChatInvitesSubscriptionsManager.ActiveCount);
        _metrics.Set("subscriptions_active_total", TotalActive);

        try
        {
            await Task.Delay(Timeout.Infinite, context.CancellationToken);
        }
        catch (OperationCanceledException) { }
        finally
        {
            _privateChatInvitesSubscriptionsManager.RemoveSubscription(userId, subscriptionId);
            _metrics.Increment("private_chat_invites_subscriptions_closed");
            _metrics.Set("private_chat_invites_subscriptions_active", _privateChatInvitesSubscriptionsManager.ActiveCount);
            _metrics.Set("subscriptions_active_total", TotalActive);
        }
    }

    public override async Task SubscribePrivateChatInviteResolutions(SubscribePrivateChatInviteResolutionsRequest request,
        IServerStreamWriter<PrivateChatInviteResolutionEvent> responseStream, ServerCallContext context)
    {
        long userId = _userContext.UserId;
        var subscriptionId = _privateChatInviteResolutionsSubscriptionsManager.RegisterSubscription(userId, responseStream);
        _metrics.Increment("private_chat_invite_resolutions_subscriptions_opened");
        _metrics.Set("private_chat_invite_resolutions_subscriptions_active", _privateChatInviteResolutionsSubscriptionsManager.ActiveCount);
        _metrics.Set("subscriptions_active_total", TotalActive);

        try
        {
            await Task.Delay(Timeout.Infinite, context.CancellationToken);
        }
        catch (OperationCanceledException) { }
        finally
        {
            _privateChatInviteResolutionsSubscriptionsManager.RemoveSubscription(userId, subscriptionId);
            _metrics.Increment("private_chat_invite_resolutions_subscriptions_closed");
            _metrics.Set("private_chat_invite_resolutions_subscriptions_active", _privateChatInviteResolutionsSubscriptionsManager.ActiveCount);
            _metrics.Set("subscriptions_active_total", TotalActive);
        }
    }

    // -- Секретные чаты (device-scope) --------------------------------------

    public override async Task SubscribeSecretChatInvites(SubscribeSecretChatInvitesRequest request,
        IServerStreamWriter<SecretChatInviteEvent> responseStream, ServerCallContext context)
    {
        long userId = _userContext.UserId;
        var deviceId = RequireDeviceId();

        var subscriptionId = _secretChatInvitesSubscriptionsManager.RegisterSubscription(userId, deviceId, responseStream);
        _metrics.Increment("secret_chat_invites_subscriptions_opened");
        _metrics.Set("secret_chat_invites_subscriptions_active", _secretChatInvitesSubscriptionsManager.ActiveCount);
        _metrics.Set("subscriptions_active_total", TotalActive);

        try
        {
            await Task.Delay(Timeout.Infinite, context.CancellationToken);
        }
        catch (OperationCanceledException) { }
        finally
        {
            _secretChatInvitesSubscriptionsManager.RemoveSubscription(userId, deviceId, subscriptionId);
            _metrics.Increment("secret_chat_invites_subscriptions_closed");
            _metrics.Set("secret_chat_invites_subscriptions_active", _secretChatInvitesSubscriptionsManager.ActiveCount);
            _metrics.Set("subscriptions_active_total", TotalActive);
        }
    }

    public override async Task SubscribeSecretChatResolutions(SubscribeSecretChatResolutionsRequest request,
        IServerStreamWriter<SecretChatInviteResolutionEvent> responseStream, ServerCallContext context)
    {
        long userId = _userContext.UserId;
        var deviceId = RequireDeviceId();

        var subscriptionId = _secretChatResolutionsSubscriptionsManager.RegisterSubscription(userId, deviceId, responseStream);
        _metrics.Increment("secret_chat_resolutions_subscriptions_opened");
        _metrics.Set("secret_chat_resolutions_subscriptions_active", _secretChatResolutionsSubscriptionsManager.ActiveCount);
        _metrics.Set("subscriptions_active_total", TotalActive);

        try
        {
            await Task.Delay(Timeout.Infinite, context.CancellationToken);
        }
        catch (OperationCanceledException) { }
        finally
        {
            _secretChatResolutionsSubscriptionsManager.RemoveSubscription(userId, deviceId, subscriptionId);
            _metrics.Increment("secret_chat_resolutions_subscriptions_closed");
            _metrics.Set("secret_chat_resolutions_subscriptions_active", _secretChatResolutionsSubscriptionsManager.ActiveCount);
            _metrics.Set("subscriptions_active_total", TotalActive);
        }
    }

    public override async Task SubscribeSecretMessages(SubscribeSecretMessagesRequest request,
        IServerStreamWriter<NewSecretMessageEvent> responseStream, ServerCallContext context)
    {
        long userId = _userContext.UserId;
        var deviceId = RequireDeviceId();

        var subscriptionId = _secretMessagesSubscriptionsManager.RegisterSubscription(userId, deviceId, responseStream);
        _metrics.Increment("secret_messages_subscriptions_opened");
        _metrics.Set("secret_messages_subscriptions_active", _secretMessagesSubscriptionsManager.ActiveCount);
        _metrics.Set("subscriptions_active_total", TotalActive);

        try
        {
            await Task.Delay(Timeout.Infinite, context.CancellationToken);
        }
        catch (OperationCanceledException) { }
        finally
        {
            _secretMessagesSubscriptionsManager.RemoveSubscription(userId, deviceId, subscriptionId);
            _metrics.Increment("secret_messages_subscriptions_closed");
            _metrics.Set("secret_messages_subscriptions_active", _secretMessagesSubscriptionsManager.ActiveCount);
            _metrics.Set("subscriptions_active_total", TotalActive);
        }
    }
}
