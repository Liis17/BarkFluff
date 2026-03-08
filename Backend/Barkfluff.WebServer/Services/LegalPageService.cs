namespace Barkfluff.WebServer.Services
{
    public class LegalPageService
    {
        private readonly string _legalPath;
        private readonly string _htmlPath;

        public LegalPageService()
        {
            var assemblyLocation = AppContext.BaseDirectory;
            _legalPath = Path.Combine(assemblyLocation, "html", "legal");
            _htmlPath = Path.Combine(assemblyLocation, "html");
        }

        public string ProcessLegalPage(string pageName)
        {
            // Сопоставляем имена страниц с файлами
            var fileName = pageName switch
            {
                "privacy-policy" => "privacy-policy.html",
                "terms-of-service" => "terms-of-service.html",
                "account-deletion" => "account-deletion.html",
                "encryption" => "encryption.html",
                _ => null
            };

            if (fileName == null)
            {
                return string.Empty;
            }

            var filePath = Path.Combine(_legalPath, fileName);

            if (!File.Exists(filePath))
            {
                return string.Empty;
            }

            return File.ReadAllText(filePath);
        }

        public string ProcessSelfhostedPage()
        {
            var filePath = Path.Combine(_htmlPath, "selfhosted.html");

            if (!File.Exists(filePath))
            {
                return string.Empty;
            }

            return File.ReadAllText(filePath);
        }
    }
}
