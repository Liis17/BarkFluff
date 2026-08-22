using System.Text.RegularExpressions;

using BarkFluff.Bots.Persistence.Services;
using BarkFluff.Bots.Services;
using BarkFluff.Proto.Bots;
using BarkFluff.Proto.Users;
using BarkFluff.Shared.Exceptions.Identity;
using BarkFluff.Shared.Exceptions.Bots;

using Grpc.Core;

using MediatR;

namespace BarkFluff.Bots.Features.UpdateBotProfile;

public class UpdateBotProfileCommandHandler : IRequestHandler<UpdateBotProfileCommand, UpdateBotProfileResponse>
{
    private static readonly Regex UsernamePattern = new("^[A-Za-z0-9_]{3,32}$", RegexOptions.Compiled);

    private readonly BotsStorage _botsStorage;
    private readonly BotRegistryCache _registryCache;
    private readonly UsersServerApi.UsersServerApiClient _usersClient;
    private readonly ILogger<UpdateBotProfileCommandHandler> _logger;

    public UpdateBotProfileCommandHandler(
        BotsStorage botsStorage,
        BotRegistryCache registryCache,
        UsersServerApi.UsersServerApiClient usersClient,
        ILogger<UpdateBotProfileCommandHandler> logger)
    {
        _botsStorage = botsStorage;
        _registryCache = registryCache;
        _usersClient = usersClient;
        _logger = logger;
    }

    public async Task<UpdateBotProfileResponse> Handle(
        UpdateBotProfileCommand request,
        CancellationToken cancellationToken)
    {
        var bot = await _botsStorage.GetById(request.BotId);
        if (bot is null)
            throw new BotNotFoundException();

        var name = request.Name?.Trim() ?? string.Empty;
        var username = request.Username?.Trim() ?? string.Empty;

        if (name.Length == 0)
        {
            throw new RpcException(new Status(
                StatusCode.InvalidArgument,
                "Имя бота обязательно"));
        }

        if (name.Length > 100)
        {
            throw new RpcException(new Status(
                StatusCode.InvalidArgument,
                "Имя бота не может быть длиннее 100 символов"));
        }

        if (!UsernamePattern.IsMatch(username))
        {
            throw new RpcException(new Status(
                StatusCode.InvalidArgument,
                "Username должен содержать 3–32 символа: латинские буквы, цифры и _"));
        }

        if (!username.EndsWith("bot", StringComparison.OrdinalIgnoreCase))
        {
            throw new RpcException(new Status(
                StatusCode.InvalidArgument,
                "Username бота должен заканчиваться на bot"));
        }

        User user;
        try
        {
            var userResponse = await _usersClient.GetByIdAsync(
                new GetByIdRequest { UserId = bot.Id },
                cancellationToken: cancellationToken);
            user = userResponse.User;
        }
        catch (UserNotFoundException)
        {
            throw new BotNotFoundException();
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            throw new BotNotFoundException();
        }

        if (user is null || !user.IsBot)
            throw new BotNotFoundException();

        if (!string.Equals(user.Username, username, StringComparison.OrdinalIgnoreCase))
        {
            var usernameExists = await _usersClient.CheckExistUsernameAsync(
                new CheckExistUsernameRequest { Username = username },
                cancellationToken: cancellationToken);

            if (usernameExists.Exist)
            {
                throw new RpcException(new Status(
                    StatusCode.AlreadyExists,
                    "Username уже занят"));
            }
        }

        try
        {
            await _usersClient.UpdateProfileServerAsync(
                new UpdateProfileServerRequest
                {
                    UserId = bot.Id,
                    FirstName = name,
                    Username = username
                },
                cancellationToken: cancellationToken);
        }
        catch (UsernameExistException)
        {
            throw new RpcException(new Status(StatusCode.AlreadyExists, "Username уже занят"));
        }

        bot.Name = name;
        bot.Username = username;
        await _botsStorage.Update(bot);
        _registryCache.Set(bot);

        _logger.LogInformation(
            "Профиль бота {BotId} обновлён: {Name} (@{Username})",
            bot.Id,
            bot.Name,
            bot.Username);

        return new UpdateBotProfileResponse();
    }
}
