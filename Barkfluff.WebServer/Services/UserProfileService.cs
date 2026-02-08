using BarkFluff.Proto.Users;
using Grpc.Core;
using Microsoft.Extensions.Caching.Memory;

namespace Barkfluff.WebServer.Services;

public class UserProfileService
{
    private readonly UsersServerApi.UsersServerApiClient _client;
    private readonly IMemoryCache _cache;
    private readonly ILogger<UserProfileService> _logger;
    private readonly string _serviceToken;

    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan NegativeCacheDuration = TimeSpan.FromMinutes(5);

    public UserProfileService(
        UsersServerApi.UsersServerApiClient client,
        IMemoryCache cache,
        string serviceToken,
        ILogger<UserProfileService> logger)
    {
        _client = client;
        _cache = cache;
        _logger = logger;
        _serviceToken = serviceToken;
    }

    public async Task<UserProfileData?> GetUserProfileAsync(string username)
    {
        var cacheKey = $"user_profile:{username.ToLowerInvariant()}";

        if (_cache.TryGetValue(cacheKey, out UserProfileData? cached))
        {
            return cached;
        }

        try
        {
            var metadata = new Metadata();
            if (!string.IsNullOrEmpty(_serviceToken))
            {
                metadata.Add("x-auth-token", _serviceToken);
            }

            var response = await _client.GetUserByUsernameAsync(
                new GetUserByUsernameRequest { Username = username },
                new CallOptions(metadata));

            if (!response.Found)
            {
                _cache.Set(cacheKey, (UserProfileData?)null, NegativeCacheDuration);
                return null;
            }

            var data = new UserProfileData
            {
                FirstName = response.FirstName,
                LastName = response.LastName,
                Username = response.Username,
                Bio = response.Bio,
                ProfilePicture = response.ProfilePicture,
            };

            _cache.Set(cacheKey, (UserProfileData?)data, CacheDuration);
            return data;
        }
        catch (RpcException ex)
        {
            _logger.LogError(ex, "gRPC error fetching user profile for '{Username}'", username);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error fetching user profile for '{Username}'", username);
            return null;
        }
    }
}

public class UserProfileData
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Bio { get; set; } = string.Empty;
    public string ProfilePicture { get; set; } = string.Empty;
}
