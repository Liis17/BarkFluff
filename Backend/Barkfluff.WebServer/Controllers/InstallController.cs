using Microsoft.AspNetCore.Mvc;

namespace Barkfluff.WebServer.Controllers
{
    [ApiController]
    public class InstallController : ControllerBase
    {
        [HttpGet("/install.ps1")]
        public IActionResult GetInstallScript()
        {
            var assemblyLocation = AppContext.BaseDirectory;
            var scriptPath = Path.Combine(assemblyLocation, "files", "install.ps1");

            if (!System.IO.File.Exists(scriptPath))
            {
                return NotFound("Install script not found");
            }

            var scriptContent = System.IO.File.ReadAllText(scriptPath);
            return Content(scriptContent, "text/plain");
        }
    }
}
