using Barkfluff.Docker.Control.Models;
using Barkfluff.Docker.Control.Models.Dtos;

namespace Barkfluff.Docker.Control.Services;

public class AuthService
{
    private readonly PendingAuthService _pendingAuthService;
    private readonly TokenService _tokenService;
    private readonly TelegramBotService _telegramBotService;

    public AuthService(
        PendingAuthService pendingAuthService,
        TokenService tokenService,
        TelegramBotService telegramBotService)
    {
        _pendingAuthService = pendingAuthService;
        _tokenService = tokenService;
        _telegramBotService = telegramBotService;
    }

    public async Task<string> CreateAuthRequestAsync(AuthRequestDto dto)
    {
        // Detect browser and OS from user agent
        var (browser, os) = ParseUserAgent(dto.UserAgent);

        var request = _pendingAuthService.CreateRequest(
            dto.IpAddress,
            browser,
            os,
            dto.UserAgent,
            dto.TokenName ?? "Web Session");

        // Send notification to Telegram
        await _telegramBotService.SendAuthRequestAsync(request);

        return request.RequestId;
    }

    public AuthStatusResponse GetStatus(string requestId)
    {
        var request = _pendingAuthService.GetRequest(requestId);
        if (request == null)
        {
            return new AuthStatusResponse
            {
                Status = AuthRequestStatus.Expired,
                Message = "Request not found or expired."
            };
        }

        if (request.Status == AuthRequestStatus.Approved && request.TokenId.HasValue)
        {
            return new AuthStatusResponse
            {
                Status = AuthRequestStatus.Approved,
                Token = request.TokenId.Value.ToString()
            };
        }

        return new AuthStatusResponse
        {
            Status = request.Status,
            Message = request.Status switch
            {
                AuthRequestStatus.Pending => "Waiting for approval...",
                AuthRequestStatus.Rejected => "Authorization was rejected.",
                AuthRequestStatus.Expired => "Request has expired.",
                _ => "Unknown status."
            }
        };
    }

    private (string? Browser, string? Os) ParseUserAgent(string? userAgent)
    {
        if (string.IsNullOrEmpty(userAgent))
            return (null, null);

        string browser = null;
        string os = null;

        // Simple browser detection
        if (userAgent.Contains("Edg/")) browser = "Edge";
        else if (userAgent.Contains("Chrome") && !userAgent.Contains("Edg/")) browser = "Chrome";
        else if (userAgent.Contains("Firefox")) browser = "Firefox";
        else if (userAgent.Contains("Safari") && !userAgent.Contains("Chrome")) browser = "Safari";
        else if (userAgent.Contains("Opera") || userAgent.Contains("OPR/")) browser = "Opera";
        else browser = "Unknown";

        // Simple OS detection
        if (userAgent.Contains("Windows NT 10.0")) os = "Windows 10/11";
        else if (userAgent.Contains("Windows NT 6.3")) os = "Windows 8.1";
        else if (userAgent.Contains("Windows NT 6.1")) os = "Windows 7";
        else if (userAgent.Contains("Windows")) os = "Windows";
        else if (userAgent.Contains("Mac OS X")) os = "macOS";
        else if (userAgent.Contains("Linux")) os = "Linux";
        else if (userAgent.Contains("Android")) os = "Android";
        else if (userAgent.Contains("iPhone") || userAgent.Contains("iPad")) os = "iOS";
        else os = "Unknown";

        return (browser, os);
    }
}
