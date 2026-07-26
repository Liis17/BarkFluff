using Microsoft.AspNetCore.Mvc;
using BarkFluff.GrpcServer.Metrics;

namespace Barkfluff.WebServer.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class DownloadController : ControllerBase
    {
        private readonly MetricsCollector _metrics;

        public DownloadController(MetricsCollector metrics)
        {
            _metrics = metrics;
        }

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
            _metrics.Increment("installer_downloads");
            return File(fileBytes, "application/octet-stream", "Barkfluff.Updater.CLI.exe");
        }
    }
}
