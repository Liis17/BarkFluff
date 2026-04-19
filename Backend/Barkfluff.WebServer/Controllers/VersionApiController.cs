using Barkfluff.WebServer.Services;

using Microsoft.AspNetCore.Mvc;

namespace Barkfluff.WebServer.Controllers;

[ApiController]
public class VersionApiController : ControllerBase
{
    private readonly VersionStore _store;

    public VersionApiController(VersionStore store) => _store = store;

    [HttpGet("/api/versions")]
    public IActionResult GetVersions()
    {
        var (androidRelease, androidBeta, windowsRelease, windowsBeta) = _store.GetAll();
        return Ok(new
        {
            android = new { release = androidRelease, beta = androidBeta },
            windows = new { release = windowsRelease, beta = windowsBeta }
        });
    }
}
