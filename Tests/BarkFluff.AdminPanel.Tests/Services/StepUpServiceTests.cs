using Barkfluff.AdminPanel.Models;
using Barkfluff.AdminPanel.Services;

using Xunit;

namespace Barkfluff.AdminPanel.Tests.Services;

public class StepUpServiceTests
{
    private const string Action = "docker.branch";
    private const string Params = "container=users";
    private static readonly Guid TokenId = Guid.NewGuid();

    private static PendingStepUp ApprovedRequest(StepUpService service, string actionKey = Action, string parameters = Params, Guid? tokenId = null, long userId = 100)
    {
        var request = new PendingStepUp
        {
            ActionKey = actionKey,
            Params = parameters,
            TokenId = tokenId ?? TokenId,
            TargetTelegramUserId = userId
        };
        service.CreateRequest(request);
        Assert.True(service.Resolve(request.ConfirmationId, StepUpStatus.Approved, userId));
        return request;
    }

    [Fact]
    public void TryConsume_ValidConfirmation_SucceedsOnce()
    {
        var service = new StepUpService();
        var request = ApprovedRequest(service);

        Assert.True(service.TryConsume(request.ConfirmationId, TokenId, Action, Params));
        Assert.False(service.TryConsume(request.ConfirmationId, TokenId, Action, Params));
    }

    [Fact]
    public void TryConsume_PendingRequest_Fails()
    {
        var service = new StepUpService();
        var request = new PendingStepUp { ActionKey = Action, Params = Params, TokenId = TokenId, TargetTelegramUserId = 100 };
        service.CreateRequest(request);

        Assert.False(service.TryConsume(request.ConfirmationId, TokenId, Action, Params));
    }

    [Fact]
    public void TryConsume_RejectedRequest_Fails()
    {
        var service = new StepUpService();
        var request = new PendingStepUp { ActionKey = Action, Params = Params, TokenId = TokenId, TargetTelegramUserId = 100 };
        service.CreateRequest(request);
        Assert.True(service.Resolve(request.ConfirmationId, StepUpStatus.Rejected, 100));

        Assert.False(service.TryConsume(request.ConfirmationId, TokenId, Action, Params));
    }

    [Fact]
    public void TryConsume_DifferentToken_Fails()
    {
        var service = new StepUpService();
        var request = ApprovedRequest(service);

        Assert.False(service.TryConsume(request.ConfirmationId, Guid.NewGuid(), Action, Params));
    }

    [Fact]
    public void TryConsume_DifferentAction_Fails()
    {
        var service = new StepUpService();
        var request = ApprovedRequest(service);

        Assert.False(service.TryConsume(request.ConfirmationId, TokenId, "docker.restart-all", Params));
    }

    [Fact]
    public void TryConsume_DifferentParams_Fails()
    {
        var service = new StepUpService();
        var request = ApprovedRequest(service);

        Assert.False(service.TryConsume(request.ConfirmationId, TokenId, Action, "container=messages"));
    }

    [Fact]
    public void Resolve_ByOtherAdmin_Fails()
    {
        var service = new StepUpService();
        var request = new PendingStepUp { ActionKey = Action, Params = Params, TokenId = TokenId, TargetTelegramUserId = 100 };
        service.CreateRequest(request);

        Assert.False(service.Resolve(request.ConfirmationId, StepUpStatus.Approved, 200));
        Assert.Equal(StepUpStatus.Pending, request.Status);
    }

    [Fact]
    public void Resolve_Twice_SecondFails()
    {
        var service = new StepUpService();
        var request = new PendingStepUp { ActionKey = Action, Params = Params, TokenId = TokenId, TargetTelegramUserId = 100 };
        service.CreateRequest(request);

        Assert.True(service.Resolve(request.ConfirmationId, StepUpStatus.Approved, 100));
        Assert.False(service.Resolve(request.ConfirmationId, StepUpStatus.Rejected, 100));
        Assert.Equal(StepUpStatus.Approved, request.Status);
    }

    [Fact]
    public void Resolve_ExpiredPendingRequest_Fails()
    {
        var service = new StepUpService();
        var request = new PendingStepUp
        {
            ActionKey = Action,
            Params = Params,
            TokenId = TokenId,
            TargetTelegramUserId = 100,
            CreatedAt = DateTime.UtcNow - StepUpService.PendingTimeout - TimeSpan.FromSeconds(1)
        };
        service.CreateRequest(request);

        Assert.False(service.Resolve(request.ConfirmationId, StepUpStatus.Approved, 100));
        Assert.Equal(StepUpStatus.Expired, request.Status);
    }

    [Fact]
    public void TryConsume_ExpiredApproval_Fails()
    {
        var service = new StepUpService();
        var request = ApprovedRequest(service);
        request.ResolvedAt = DateTime.UtcNow - StepUpService.ApprovalValidFor - TimeSpan.FromSeconds(1);

        Assert.False(service.TryConsume(request.ConfirmationId, TokenId, Action, Params));
        Assert.Equal(StepUpStatus.Expired, request.Status);
    }

    [Fact]
    public void Resolve_InvalidStatus_Fails()
    {
        var service = new StepUpService();
        var request = new PendingStepUp
        {
            ActionKey = Action,
            Params = Params,
            TokenId = TokenId,
            TargetTelegramUserId = 100
        };
        service.CreateRequest(request);

        Assert.False(service.Resolve(request.ConfirmationId, StepUpStatus.Used, 100));
        Assert.Equal(StepUpStatus.Pending, request.Status);
    }

    [Fact]
    public void ComputeParamsHash_IsStableAndActionBound()
    {
        var hash1 = StepUpService.ComputeParamsHash(Action, Params);
        var hash2 = StepUpService.ComputeParamsHash(Action, Params);
        var hashOtherAction = StepUpService.ComputeParamsHash("other", Params);

        Assert.Equal(hash1, hash2);
        Assert.NotEqual(hash1, hashOtherAction);
    }
}
