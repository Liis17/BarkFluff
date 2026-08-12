using BarkFluff.Messages.Mapping;
using BarkFluff.Shared.Exceptions.Messages;

using ProtoOutgoingMessage = BarkFluff.Proto.Messages.OutgoingMessage;

namespace BarkFluff.Messages.Tests.Mapping;

/// <summary>
/// Граница совместимости. iOS, macOS, ClientV2.WPF и Linux ещё шлют одиночное
/// <c>forwarded_message_id</c> — оно обязано работать ровно как раньше, иначе разделение
/// reply/forward сломает их молча.
/// </summary>
public class OutgoingMessageMappingTests
{
    [Fact]
    public void LegacyForwardedMessageId_BecomesSingleForward()
    {
        var mapped = new ProtoOutgoingMessage { Text = "note", ForwardedMessageId = 42 }.ToCommandMessage();

        mapped.ForwardedMessageIds.Should().Equal(42);
        mapped.ReplyToMessageId.Should().BeNull();
    }

    [Fact]
    public void LegacyZero_MeansNoForward()
    {
        var mapped = new ProtoOutgoingMessage { Text = "note", ForwardedMessageId = 0 }.ToCommandMessage();

        mapped.ForwardedMessageIds.Should().BeNull();
    }

    [Fact]
    public void NewFields_MapAsGiven()
    {
        var mapped = new ProtoOutgoingMessage
        {
            Text = "note",
            ReplyToMessageId = 7,
            ForwardedMessageIds = { 3, 1, 2 }
        }.ToCommandMessage();

        mapped.ReplyToMessageId.Should().Be(7);
        mapped.ForwardedMessageIds.Should().Equal(3, 1, 2);
    }

    [Fact]
    public void LegacyMixedWithReply_IsRejected()
    {
        var message = new ProtoOutgoingMessage { ForwardedMessageId = 42, ReplyToMessageId = 7 };

        // Молча выбранная за клиента трактовка была бы хуже явной ошибки.
        var act = () => message.ToCommandMessage();

        act.Should().Throw<ConflictingForwardFieldsException>();
    }

    [Fact]
    public void LegacyMixedWithForwardList_IsRejected()
    {
        var message = new ProtoOutgoingMessage { ForwardedMessageId = 42, ForwardedMessageIds = { 43 } };

        var act = () => message.ToCommandMessage();

        act.Should().Throw<ConflictingForwardFieldsException>();
    }
}
