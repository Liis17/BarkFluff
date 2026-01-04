using Microsoft.AspNetCore.Mvc;

namespace Barkfluff.WebServer.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class DownloadController : ControllerBase
    {
        [HttpGet("installer")]
        public IActionResult GetInstaller()
        {
            var assemblyLocation = AppContext.BaseDirectory;
            var installerPath = Path.Combine(assemblyLocation, "files", "Barkfluff.Updater.CLI.exe");
            
            if (!System.IO.File.Exists(installerPath))
            {
                return NotFound("Installer not found");
            }

            var fileBytes = System.IO.File.ReadAllBytes(installerPath);
            return File(fileBytes, "application/octet-stream", "Barkfluff.Updater.CLI.exe");
        }
    }
}
