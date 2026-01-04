using Microsoft.AspNetCore.Mvc;
using Barkfluff.WebServer.Services;

namespace Barkfluff.WebServer.Controllers
{
    [ApiController]
    public class FallbackController : ControllerBase
    {
        private readonly UserPageService _userPageService;

        public FallbackController(UserPageService userPageService)
        {
            _userPageService = userPageService;
        }

        [HttpGet("/{**catchAll}")]
        public IActionResult HandleFallback(string catchAll)
        {
            var processedHtml = _userPageService.ProcessUserPage(catchAll);
            
            if (string.IsNullOrEmpty(processedHtml))
            {
                return NotFound("Page not found");
            }

            return Content(processedHtml, "text/html");
        }
    }
}
