using Barkfluff.AdminPanel.Models;
using Barkfluff.AdminPanel.Models.Dtos;

namespace Barkfluff.AdminPanel.Services;

/// <summary>
/// Result of creating an auth request
/// </summary>
public class CreateAuthRequestResult
{
    public bool Success { get; set; }
    public string? RequestId { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }

    public static CreateAuthRequestResult Ok(string requestId) => new()
    {
        Success = true,
        RequestId = requestId
    };

    public static CreateAuthRequestResult Fail(string errorCode, string errorMessage) => new()
    {
        Success = false,
        ErrorCode = errorCode,
        ErrorMessage = errorMessage
    };
}

public class AuthService
{
    private readonly PendingAuthService _pendingAuthService;
    private readonly TokenService _tokenService;
    private readonly TelegramBotService _telegramBotService;
    private readonly AdminService _adminService;

    public AuthService(
        PendingAuthService pendingAuthService,
        TokenService tokenService,
        TelegramBotService telegramBotService,
        AdminService adminService)
    {
        _pendingAuthService = pendingAuthService;
        _tokenService = tokenService;
        _telegramBotService = telegramBotService;
        _adminService = adminService;
    }

    public async Task<CreateAuthRequestResult> CreateAuthRequestAsync(AuthRequestDto dto)
    {
        // Validate nickname (username) is provided
        var nickname = dto.Nickname?.Trim();
        if (string.IsNullOrEmpty(nickname))
        {
            return CreateAuthRequestResult.Fail("missing_username", "Введите имя пользователя");
        }

        // Find the admin by username
        nickname = AdminService.NormalizeUsername(nickname);
        var targetAdmin = _adminService.GetByUsername(nickname);
        if (targetAdmin == null)
        {
            return CreateAuthRequestResult.Fail("unknown_user", "Пользователь не найден");
        }

        // Check if the bot can reach the admin
        var reachability = await _telegramBotService.CheckBotReachabilityAsync(targetAdmin.TelegramUserId);
        if (!reachability.CanSend)
        {
            return CreateAuthRequestResult.Fail(
                reachability.ErrorCode ?? "unknown_error",
                reachability.ErrorMessage ?? "Не удалось отправить запрос");
        }

        var (browser, os) = ParseUserAgent(dto.UserAgent);

        var request = _pendingAuthService.CreateRequest(
            dto.IpAddress,
            browser,
            os,
            dto.UserAgent,
            dto.TokenName ?? "Web Session",
            nickname,
            targetAdmin.TelegramUserId);

        await _telegramBotService.SendAuthRequestAsync(request);

        return CreateAuthRequestResult.Ok(request.RequestId);
    }

    /// <summary>
    /// Legacy method for backward compatibility
    /// </summary>
    public async Task<string> CreateAuthRequestAsyncLegacy(AuthRequestDto dto)
    {
        var (browser, os) = ParseUserAgent(dto.UserAgent);

        var request = _pendingAuthService.CreateRequest(
            dto.IpAddress,
            browser,
            os,
            dto.UserAgent,
            dto.TokenName ?? "Web Session",
            dto.Nickname);

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

        if (userAgent.Contains("Edg/")) browser = "Edge";
        else if (userAgent.Contains("Chrome") && !userAgent.Contains("Edg/")) browser = "Chrome";
        else if (userAgent.Contains("Firefox")) browser = "Firefox";
        else if (userAgent.Contains("Safari") && !userAgent.Contains("Chrome")) browser = "Safari";
        else if (userAgent.Contains("Opera") || userAgent.Contains("OPR/")) browser = "Opera";
        else browser = "Unknown";

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
