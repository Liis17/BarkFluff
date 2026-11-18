using BarkFluff.Messages.Features.Shared;
using BarkFluff.Shared.Queue.Users;

using MassTransit;

namespace BarkFluff.Messages.Consumers;

public sealed class UserAccountStatusChangedConsumer(UserAccountStatusCache statusCache)
    : IConsumer<UserAccountStatusChanged>
{
    public Task Consume(ConsumeContext<UserAccountStatusChanged> context)
    {
        statusCache.Invalidate(context.Message.UserId);
        return Task.CompletedTask;
    }
}
