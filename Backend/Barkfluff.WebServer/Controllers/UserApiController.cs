using Barkfluff.WebServer.Services;

using Microsoft.AspNetCore.Mvc;

namespace Barkfluff.WebServer.Controllers;

[ApiController]
public class UserApiController : ControllerBase
{
    private readonly UserProfileService _userProfileService;

    public UserApiController(UserProfileService userProfileService)
    {
        _userProfileService = userProfileService;
    }

    [HttpGet("/api/user/{username}")]
    public async Task<IActionResult> GetUserProfile(string username)
    {
        if (string.IsNullOrWhiteSpace(username) || username.Length > 64)
        {
            return BadRequest(new { found = false });
        }

        var profile = await _userProfileService.GetUserProfileAsync(username);

        if (profile is null)
        {
            return Ok(new { found = false });
        }

        return Ok(new
        {
            found = true,
            firstName = profile.FirstName,
            lastName = profile.LastName,
            username = profile.Username,
            bio = profile.Bio,
            profilePicture = profile.ProfilePicture,
            profilePosterUrl = profile.ProfilePosterUrl,
        });
    }
}
