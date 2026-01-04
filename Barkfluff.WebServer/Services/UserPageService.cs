namespace Barkfluff.WebServer.Services
{
    public class UserPageService
    {
        public string ProcessUserPage(string path)
        {
            var assemblyLocation = AppContext.BaseDirectory;
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
