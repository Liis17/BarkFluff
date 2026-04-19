using Microsoft.AspNetCore.Mvc;

namespace Barkfluff.Developers.Controllers
{
    [ApiController]
    public class HomeController : ControllerBase
    {
        [HttpGet("/")]
        public IActionResult Index()
        {
            var path = Path.Combine(AppContext.BaseDirectory, "html", "index.html");
            return PhysicalFile(path, "text/html; charset=utf-8");
        }

        [HttpGet("/files/{*filePath}")]
        public IActionResult Files(string filePath)
        {
            var fullPath = Path.Combine(AppContext.BaseDirectory, "files", filePath);
            if (!System.IO.File.Exists(fullPath))
                return NotFound();

            var ext = Path.GetExtension(fullPath).ToLowerInvariant();
            var contentType = ext switch
            {
                ".proto" => "text/plain; charset=utf-8",
                ".md"    => "text/plain; charset=utf-8",
                _        => "application/octet-stream"
            };

            return PhysicalFile(fullPath, contentType);
        }
    }
}
