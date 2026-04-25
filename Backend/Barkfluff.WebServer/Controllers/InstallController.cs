using Microsoft.AspNetCore.Mvc;

namespace Barkfluff.WebServer.Controllers
{
    [ApiController]
    public class InstallController : ControllerBase
    {
        [HttpGet("/install.ps1")]
        public IActionResult GetInstallScript()
            => ServeFile("install.ps1", "application/octet-stream");

        [HttpGet("/installbeta.ps1")]
        public IActionResult GetInstallBetaScript()
            => ServeFile("installbeta.ps1", "application/octet-stream");

        [HttpGet("/install.sh")]
        public IActionResult GetInstallShScript()
            => ServeFile("install.sh", "application/x-sh");

        [HttpGet("/installbeta.sh")]
        public IActionResult GetInstallBetaShScript()
            => ServeFile("installbeta.sh", "application/x-sh");

        private IActionResult ServeFile(string fileName, string contentType)
        {
            var path = Path.Combine(AppContext.BaseDirectory, "files", fileName);
            if (!System.IO.File.Exists(path))
                return NotFound();

            return PhysicalFile(path, contentType, fileName);
        }
    }
}
