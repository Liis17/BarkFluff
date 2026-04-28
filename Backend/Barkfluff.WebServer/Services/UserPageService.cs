namespace Barkfluff.WebServer.Services
{
    public class UserPageService
    {
        public string ProcessUserPage(string path)
        {
            var assemblyLocation = AppContext.BaseDirectory;

            // Специальная страница только для пользователя li_is
            if (string.Equals(path, "li_is", StringComparison.OrdinalIgnoreCase))
            {
                var liIsPath = Path.Combine(assemblyLocation, "html", "UniqueUsers", "paws.page.html");
                if (File.Exists(liIsPath))
                {
                    return File.ReadAllText(liIsPath).Replace("%%username%%", path);
                }
            }

            var htmlPath = Path.Combine(assemblyLocation, "html", "userpage.html");

            if (!File.Exists(htmlPath))
            {
                return string.Empty;
            }

            var htmlContent = File.ReadAllText(htmlPath).Replace("%%username%%", $"{path}"); ;



            return htmlContent;
        }
    }
}
