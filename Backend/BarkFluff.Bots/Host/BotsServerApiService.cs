using BarkFluff.Bots.Features.CreateSystemBot;
using BarkFluff.Bots.Features.DeleteBot;
using BarkFluff.Bots.Features.ListBots;
using BarkFluff.Bots.Features.RegenerateToken;
using BarkFluff.Bots.Features.UpdateBotProfile;
using BarkFluff.Bots.Features.GetBotToken;
using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Proto.Bots;
using BarkFluff.Shared.Identity;

using Grpc.Core;

using MediatR;

using Microsoft.AspNetCore.Authorization;

namespace BarkFluff.Bots.Host;

[Authorize(Policy = nameof(TokenType.Service))]
public class BotsServerApiService : BotsServerApi.BotsServerApiBase
{
    private readonly IMediator _mediator;
    private readonly MetricsCollector _metrics;

    public BotsServerApiService(IMediator mediator, MetricsCollector metrics)
    {
        _mediator = mediator;
        _metrics = metrics;
    }

    public override Task<CreateSystemBotResponse> CreateSystemBot(CreateSystemBotRequest request, ServerCallContext context)
    {
        _metrics.Increment("system_bots_create_requests");
        return _mediator.Send(new CreateSystemBotCommand
        {
            Username = request.Username?.Trim() ?? string.Empty,
            Name = request.Name?.Trim() ?? string.Empty,
        }, context.CancellationToken);
    }

    public override Task<ListBotsResponse> ListBots(ListBotsRequest request, ServerCallContext context)
    {
        return _mediator.Send(new ListBotsQuery(), context.CancellationToken);
    }

    public override Task<UpdateBotProfileResponse> UpdateBotProfile(
        UpdateBotProfileRequest request, ServerCallContext context)
    {
        _metrics.Increment("bots_profile_updates");
        return _mediator.Send(new UpdateBotProfileCommand
        {
            BotId = request.BotId,
            Name = request.Name,
            Username = request.Username
        }, context.CancellationToken);
    }

    public override Task<GetBotTokenResponse> GetBotToken(
        GetBotTokenRequest request, ServerCallContext context)
    {
        _metrics.Increment("bots_token_reads");
        return _mediator.Send(new GetBotTokenQuery { BotId = request.BotId }, context.CancellationToken);
    }

    public override Task<DeleteBotResponse> DeleteBot(DeleteBotRequest request, ServerCallContext context)
    {
        _metrics.Increment("bots_delete_requests");
        return _mediator.Send(new DeleteBotCommand { BotId = request.BotId }, context.CancellationToken);
    }

    public override Task<RegenerateTokenResponse> RegenerateToken(RegenerateTokenRequest request, ServerCallContext context)
    {
        _metrics.Increment("bots_token_regenerations");
        return _mediator.Send(new RegenerateTokenCommand { BotId = request.BotId }, context.CancellationToken);
    }
}
