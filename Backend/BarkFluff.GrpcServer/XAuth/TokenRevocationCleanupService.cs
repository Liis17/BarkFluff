using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BarkFluff.GrpcServer.XAuth;

public class TokenRevocationCleanupService : BackgroundService
{
    private readonly TokenRevocationCache _cache;
    private readonly ILogger<TokenRevocationCleanupService> _logger;

    public TokenRevocationCleanupService(TokenRevocationCache cache, ILogger<TokenRevocationCleanupService> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            _cache.Cleanup();
        }
    }
}
