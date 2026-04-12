using Microsoft.AspNetCore.Mvc;

namespace Barkfluff.WebServer.Controllers
{
    [ApiController]
    public class AssetsController : ControllerBase
    {
        private static readonly Dictionary<string, string> _allowedFiles = new()
        {
            { "barkfluff.windows.png", "image/png" },
            { "barkfluff.web.png", "image/png" },
            { "barkfluff.android.jpg", "image/jpeg" },
            { "linkpreview.png", "image/png" },
        };

        [HttpGet("/assets/{filename}")]
        public IActionResult GetAsset(string filename)
        {
            if (!_allowedFiles.TryGetValue(filename, out var contentType))
                return NotFound();

            var filePath = Path.Combine(AppContext.BaseDirectory, "files", filename);

            if (!System.IO.File.Exists(filePath))
                return NotFound();

            var fileBytes = System.IO.File.ReadAllBytes(filePath);
            return File(fileBytes, contentType);
        }
    }
}
