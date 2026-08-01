using Barkfluff.WebServer.Services;

using Microsoft.AspNetCore.Mvc;

namespace Barkfluff.WebServer.Controllers
{
    [ApiController]
    public class FallbackController : ControllerBase
    {
        private readonly UserPageService _userPageService;
        private readonly LegalPageService _legalPageService;

        public FallbackController(UserPageService userPageService, LegalPageService legalPageService)
        {
            _userPageService = userPageService;
            _legalPageService = legalPageService;
        }

        [HttpGet("/{**catchAll}")]
        public IActionResult HandleFallback(string catchAll)
        {
            // Проверяем, является ли запрос к legal-странице
            if (catchAll.StartsWith("legal/", StringComparison.OrdinalIgnoreCase))
            {
                var pageName = catchAll.Substring(6); // убираем "legal/"
                var processedHtml = _legalPageService.ProcessLegalPage(pageName);

                if (!string.IsNullOrEmpty(processedHtml))
                {
                    return Content(processedHtml, "text/html");
                }
            }

            // Проверяем, является ли запрос к selfhosted странице
            if (catchAll.Equals("selfhosted", StringComparison.OrdinalIgnoreCase))
            {
                var processedHtml = _legalPageService.ProcessSelfhostedPage();

                if (!string.IsNullOrEmpty(processedHtml))
                {
                    return Content(processedHtml, "text/html");
                }
            }

            // Обработка пользовательских страниц
            var userPageHtml = _userPageService.ProcessUserPage(catchAll);

            if (string.IsNullOrEmpty(userPageHtml))
            {
                return NotFoundPage();
            }

            return Content(userPageHtml, "text/html");
        }

        private IActionResult NotFoundPage()
        {
            var path = Path.Combine(AppContext.BaseDirectory, "html", "404.html");

            if (!System.IO.File.Exists(path))
            {
                return NotFound("Page not found");
            }

            var result = Content(System.IO.File.ReadAllText(path), "text/html");
            result.StatusCode = StatusCodes.Status404NotFound;
            return result;
        }
    }
}
