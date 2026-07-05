using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Proto.Users;
using BarkFluff.Shared.Exceptions.Identity;
using BarkFluff.Shared.Exceptions.Users;
using BarkFluff.Users.Persistence.Services;
using BarkFluff.Users.Services;

using MediatR;

namespace BarkFluff.Users.Features.AddDraftUser;

public class AddDraftUserCommandHandler : IRequestHandler<AddDraftUserCommand, AddDraftUserResponse>
{

    private readonly UsersStorage _usersStorage;
    private readonly ReservedUsernamesService _reservedUsernamesService;
    private readonly MetricsCollector _metrics;
    private readonly ILogger<AddDraftUserCommandHandler> _logger;

    public AddDraftUserCommandHandler(
        UsersStorage usersStorage,
        ReservedUsernamesService reservedUsernamesService,
        MetricsCollector metrics,
        ILogger<AddDraftUserCommandHandler> logger)
    {
        _usersStorage = usersStorage;
        _reservedUsernamesService = reservedUsernamesService;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task<AddDraftUserResponse> Handle(AddDraftUserCommand request, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Создание черновика пользователя. Username: {Username}, Email: {MaskedEmail}, Имя: {FirstName} {LastName}",
            request.Username,
            MaskEmail(request.Email),
            request.FirstName,
            request.LastName
        );

        var email = request.Email?.Trim();
        var username = request.Username?.Trim();
        var firstName = request.FirstName?.Trim();
        var lastName = request.LastName?.Trim();

        if (!UsernameFormatValidator.IsValid(username))
        {
            _logger.LogWarning("Username {Username} имеет недопустимый формат", username);
            throw new UsernameInvalidFormatException();
        }

        if (UsernameFormatValidator.HasBotSuffix(username))
        {
            _logger.LogWarning("Username {Username} заканчивается на суффикс bot и зарезервирован", username);
            throw new UsernameBotSuffixReservedException();
        }

        _logger.LogDebug("Проверка существования email: {MaskedEmail}", MaskEmail(email));

        var userByEmail = await _usersStorage.GetUserByEmail(email);

        if (userByEmail != null)
        {
            _metrics.Increment("users_email_conflicts");
            if (userByEmail.IsDraft)
            {
                _logger.LogWarning(
                    "Email {MaskedEmail} уже занят черновиком пользователя {UserId}",
                    MaskEmail(request.Email),
                    userByEmail.Id
                );
                throw new UserIsDraftException();
            }

            _logger.LogWarning("Email {MaskedEmail} уже существует у пользователя {UserId}", MaskEmail(email), userByEmail.Id);
            throw new EmailExistException();
        }

        _logger.LogDebug("Проверка на зарезервированное имя: {Username}", username);

        if (_reservedUsernamesService.IsReserved(username))
        {
            _metrics.Increment("users_reserved_username_blocked");
            _logger.LogWarning("Username {Username} является зарезервированным именем", username);
            throw new UsernameReservedException();
        }

        _logger.LogDebug("Проверка существования username: {Username}", username);

        var userByUsername = await _usersStorage.GetUserByUsername(username);

        if (userByUsername != null)
        {
            _metrics.Increment("users_username_conflicts");
            if (userByUsername.IsDraft)
            {
                _logger.LogWarning(
                    "Username {Username} уже занят черновиком пользователя {UserId}",
                    request.Username,
                    userByUsername.Id
                );
                throw new UserIsDraftException();
            }

            _logger.LogWarning(
                "Username {Username} уже существует у пользователя {UserId}",
                username,
                userByUsername.Id
            );
            throw new UsernameExistException();
        }

        var user = await _usersStorage.CreateUser(username, firstName, lastName, email);

        _logger.LogInformation(
            "Черновик пользователя создан. UserId: {UserId}, Username: {Username}, Email: {MaskedEmail}",
            user.Id,
            username,
            MaskEmail(email)
        );

        return new AddDraftUserResponse { UserId = user.Id };
    }

    private static string MaskEmail(string? email)
    {
        if (string.IsNullOrEmpty(email))
            return "***";

        var at = email.IndexOf('@');
        return at > 1 ? email[..2] + "***" + email[at..] : "***";
    }
}