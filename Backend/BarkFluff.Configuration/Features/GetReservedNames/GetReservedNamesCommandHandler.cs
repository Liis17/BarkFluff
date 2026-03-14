using BarkFluff.Configuration.Infrastructure;
using BarkFluff.Proto.Configuration;

using MediatR;

namespace BarkFluff.Configuration.Features.GetReservedNames;

public class GetReservedNamesCommandHandler : IRequestHandler<GetReservedNamesCommand, GetReservedNamesResponse>
{
    private readonly ConfigurationStorage _configurationStorage;
    private readonly ILogger<GetReservedNamesCommandHandler> _logger;

    public GetReservedNamesCommandHandler(ConfigurationStorage configurationStorage, ILogger<GetReservedNamesCommandHandler> logger)
    {
        _configurationStorage = configurationStorage;
        _logger = logger;
    }

    public async Task<GetReservedNamesResponse> Handle(GetReservedNamesCommand request, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Получение списка зарезервированных имён");

        var names = await _configurationStorage.GetReservedNamesAsync();

        _logger.LogInformation("Зарезервированных имён: {Count}", names.Count);

        var response = new GetReservedNamesResponse();
        response.Names.AddRange(names);
        return response;
    }
}
