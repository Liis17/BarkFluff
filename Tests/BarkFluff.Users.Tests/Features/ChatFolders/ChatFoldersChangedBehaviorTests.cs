using BarkFluff.Proto.Users;
using BarkFluff.Shared.Queue.Users;
using BarkFluff.Users.Features.ChatFolders;
using BarkFluff.Users.Features.ChatFolders.DeleteChatFolder;
using BarkFluff.Users.Features.ChatFolders.GetChatFolders;
using FluentAssertions;
using MassTransit;
using Moq;

namespace BarkFluff.Users.Tests.Features.ChatFolders;

public class ChatFoldersChangedBehaviorTests : IAsyncDisposable
{
    private const long UserId = 42;
    private readonly TestHelper _h = new();

    private ChatFoldersChangedBehavior<TRequest, TResponse> Behavior<TRequest, TResponse>() where TRequest : notnull =>
        new(_h.CreateUserContext(UserId), _h.QueueSender,
            TestHelper.CreateLogger<ChatFoldersChangedBehavior<TRequest, TResponse>>());

    [Fact]
    public async Task Handle_SuccessfulFolderMutation_PublishesChatFoldersChangedForCurrentUser()
    {
        var response = new DeleteChatFolderResponse();

        var result = await Behavior<DeleteChatFolderCommand, DeleteChatFolderResponse>()
            .Handle(new DeleteChatFolderCommand(), _ => Task.FromResult(response), CancellationToken.None);

        result.Should().BeSameAs(response);
        _h.PublishEndpointMock.Verify(p => p.Publish(
            It.Is<ChatFoldersChanged>(e => e.UserId == UserId), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_FailedFolderMutation_DoesNotPublish()
    {
        var act = () => Behavior<DeleteChatFolderCommand, DeleteChatFolderResponse>()
            .Handle(new DeleteChatFolderCommand(), _ => throw new InvalidOperationException(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        _h.PublishEndpointMock.Verify(p => p.Publish(It.IsAny<ChatFoldersChanged>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ReadOnlyRequest_DoesNotPublish()
    {
        await Behavior<GetChatFoldersQuery, GetChatFoldersResponse>()
            .Handle(new GetChatFoldersQuery(), _ => Task.FromResult(new GetChatFoldersResponse()), CancellationToken.None);

        _h.PublishEndpointMock.Verify(p => p.Publish(It.IsAny<ChatFoldersChanged>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_PublishFails_ReturnsCommittedResponse()
    {
        _h.PublishEndpointMock
            .Setup(p => p.Publish(It.IsAny<ChatFoldersChanged>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("broker down"));
        var response = new DeleteChatFolderResponse();

        var result = await Behavior<DeleteChatFolderCommand, DeleteChatFolderResponse>()
            .Handle(new DeleteChatFolderCommand(), _ => Task.FromResult(response), CancellationToken.None);

        result.Should().BeSameAs(response);
    }

    public ValueTask DisposeAsync() => _h.DbContext.DisposeAsync();
}
