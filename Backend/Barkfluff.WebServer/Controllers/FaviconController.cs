using Microsoft.AspNetCore.Mvc;

namespace Barkfluff.WebServer.Controllers
{
    [ApiController]
    public class FaviconController : ControllerBase
    {
        [HttpGet("favicon.ico")]
        public IActionResult GetFavicon()
        {
            var assemblyLocation = AppContext.BaseDirectory;
            var faviconPath = Path.Combine(assemblyLocation, "files", "favicon.ico");

            if (!System.IO.File.Exists(faviconPath))
            {
                return NotFound("Favicon not found");
            }

            var fileBytes = System.IO.File.ReadAllBytes(faviconPath);
            return File(fileBytes, "image/x-icon", "favicon.ico");
        }
    }
}
