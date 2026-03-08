using Microsoft.AspNetCore.Mvc;

namespace Barkfluff.WebServer.Controllers
{
    [ApiController]
    public class HomeController : ControllerBase
    {
        [HttpGet("/")]
        public IActionResult Index()
        {
            var assemblyLocation = AppContext.BaseDirectory;
            var htmlPath = Path.Combine(assemblyLocation, "html", "barkfluff.html");

            if (!System.IO.File.Exists(htmlPath))
            {
                return NotFound("Main page not found");
            }

            var htmlContent = System.IO.File.ReadAllText(htmlPath);
            return Content(htmlContent, "text/html");
        }
    }
}
