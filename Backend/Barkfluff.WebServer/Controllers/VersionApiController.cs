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
        var (
            androidRelease, androidBeta, androidDev, androidNightly,
            windowsRelease, windowsBeta, windowsDev, windowsNightly,
            macosRelease, macosBeta, macosDev, macosNightly) = _store.GetAll();
        return Ok(new
        {
            android = new { release = androidRelease, beta = androidBeta, dev = androidDev, nightly = androidNightly },
            windows = new { release = windowsRelease, beta = windowsBeta, dev = windowsDev, nightly = windowsNightly },
            macos   = new { release = macosRelease, beta = macosBeta, dev = macosDev, nightly = macosNightly }
        });
    }
}
