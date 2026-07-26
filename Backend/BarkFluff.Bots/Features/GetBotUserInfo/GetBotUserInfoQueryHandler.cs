using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Proto.Bots;
using BarkFluff.Proto.Users;

using Grpc.Core;

using MediatR;

namespace BarkFluff.Bots.Features.GetBotUserInfo;

public class GetBotUserInfoQueryHandler : IRequestHandler<GetBotUserInfoQuery, GetUserInfoResponse>
{
    private readonly UsersServerApi.UsersServerApiClient _usersClient;
    private readonly MetricsCollector _metrics;

    public GetBotUserInfoQueryHandler(UsersServerApi.UsersServerApiClient usersClient, MetricsCollector metrics)
    {
        _usersClient = usersClient;
        _metrics = metrics;
    }

    public async Task<GetUserInfoResponse> Handle(GetBotUserInfoQuery request, CancellationToken cancellationToken)
    {
        _metrics.Increment("bot_api_user_info_requests");

        if (!string.IsNullOrWhiteSpace(request.Username))
        {
            // Privacy применяет Users
            var response = await _usersClient.GetUserByUsernameAsync(
                new GetUserByUsernameRequest { Username = request.Username },
                cancellationToken: cancellationToken);

            return new GetUserInfoResponse
            {
                Id = response.Id,
                Username = request.Username,
                FirstName = response.FirstName,
                LastName = response.LastName,
                Bio = response.Bio,
                AvatarUrl = response.ProfilePicture,
                IsBot = response.IsBot,
            };
        }

        if (request.UserId is > 0)
        {
            var response = await _usersClient.GetByIdAsync(
                new GetByIdRequest { UserId = request.UserId.Value },
                cancellationToken: cancellationToken);

            // Только публичные поля
            return new GetUserInfoResponse
            {
                Id = response.User.Id,
                Username = response.User.Username,
                FirstName = response.User.FirstName,
                LastName = response.User.LastName,
                Bio = response.User.Bio,
                AvatarUrl = response.User.ProfilePicture,
                IsBot = response.User.IsBot,
            };
        }

        throw new RpcException(new Status(StatusCode.InvalidArgument, "user_id или username обязателен"));
    }
}
