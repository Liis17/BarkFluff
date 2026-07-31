using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Users.Persistence.Services;

using MediatR;

namespace BarkFluff.Users.Features.Legal.AcceptLegalConsent;

public class AcceptLegalConsentCommandHandler : IRequestHandler<AcceptLegalConsentCommand>
{
    private readonly UserContext _userContext;
    private readonly UsersStorage _usersStorage;
    private readonly ILogger<AcceptLegalConsentCommandHandler> _logger;

    public AcceptLegalConsentCommandHandler(
        UserContext userContext,
        UsersStorage usersStorage,
        ILogger<AcceptLegalConsentCommandHandler> logger)
    {
        _userContext = userContext;
        _usersStorage = usersStorage;
        _logger = logger;
    }

    public async Task Handle(AcceptLegalConsentCommand request, CancellationToken cancellationToken)
    {
        var revision = request.Revision?.Trim();

        // Редакция — дата из шапки документа, её определяет клиент. Пустое значение записывать
        // нельзя: по нему потом не отличить «согласия не было» от «согласие есть, версия неизвестна».
        if (string.IsNullOrEmpty(revision))
        {
            _logger.LogWarning(
                "Пользователь {UserId} прислал согласие без редакции документа — запись пропущена",
                _userContext.UserId);
            return;
        }

        await _usersStorage.AcceptLegalConsent(_userContext.UserId, revision);

        _logger.LogInformation(
            "Пользователь {UserId} принял документы редакции {Revision}",
            _userContext.UserId,
            revision);
    }
}
