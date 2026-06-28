using BarkFluff.Users.Domain;
using BarkFluff.Users.Mapping;
using FluentAssertions;
using Google.Protobuf.WellKnownTypes;

namespace BarkFluff.Users.Tests.Mapping;

public class ChatFolderMappingTests
{
    [Fact]
    public void ToGrpc_MapsAllFields()
    {
        var folder = new Domain.ChatFolder
        {
            Id = 1,
            OwnerUserId = 100,
            FolderId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            FolderName = "Work",
            FolderIcon = "💼",
            ChatList = [Guid.Parse("22222222-2222-2222-2222-222222222222")],
            SortOrder = 5,
        };

        var grpc = folder.ToGrpc();

        grpc.FolderId.Should().Be("11111111-1111-1111-1111-111111111111");
        grpc.FolderName.Should().Be("Work");
        grpc.FolderIcon.Should().Be("💼");
        grpc.SortOrder.Should().Be(5);
        grpc.ChatList.Should().ContainSingle("22222222-2222-2222-2222-222222222222");
    }

    [Fact]
    public void ToGrpc_NullIcon_MapsToEmpty()
    {
        var folder = new Domain.ChatFolder
        {
            Id = 1,
            OwnerUserId = 1,
            FolderId = Guid.NewGuid(),
            FolderName = "Test",
            FolderIcon = null,
            ChatList = [],
            SortOrder = 0,
        };

        var grpc = folder.ToGrpc();

        grpc.FolderIcon.Should().BeEmpty();
    }

    [Fact]
    public void ToGrpc_EmptyChatList_MapsToEmpty()
    {
        var folder = new Domain.ChatFolder
        {
            Id = 1,
            OwnerUserId = 1,
            FolderId = Guid.NewGuid(),
            FolderName = "Test",
            ChatList = [],
            SortOrder = 0,
        };

        var grpc = folder.ToGrpc();

        grpc.ChatList.Should().BeEmpty();
    }
}
