using System.Text.Encodings.Web;
using System.Text.RegularExpressions;

namespace Barkfluff.WebServer.Services
{
    public partial class UserPageService
    {
        // Тот же формат, что и UsernameFormatValidator в BarkFluff.Users. Всё остальное
        // (пути со слэшами, кавычки, теги) страницей профиля быть не может — такие
        // запросы отдаются как 404, а не подставляются в шаблон.
        [GeneratedRegex(@"^[a-zA-Z0-9_]{3,32}$")]
        private static partial Regex UsernamePattern();

        public string ProcessUserPage(string path)
        {
            if (string.IsNullOrEmpty(path) || !UsernamePattern().IsMatch(path))
            {
                return string.Empty;
            }

            // Экранируем на случай, если формат username когда-нибудь расширят:
            // значение попадает и в разметку, и в JS-строку шаблона.
            var username = HtmlEncoder.Default.Encode(path);

            var assemblyLocation = AppContext.BaseDirectory;

            // Специальная страница только для пользователя li_is
            if (string.Equals(path, "li_is", StringComparison.OrdinalIgnoreCase))
            {
                var liIsPath = Path.Combine(assemblyLocation, "html", "UniqueUsers", "paws.page.html");
                if (File.Exists(liIsPath))
                {
                    return File.ReadAllText(liIsPath).Replace("%%username%%", username);
                }
            }

            var htmlPath = Path.Combine(assemblyLocation, "html", "userpage.html");

            if (!File.Exists(htmlPath))
            {
                return string.Empty;
            }

            return File.ReadAllText(htmlPath).Replace("%%username%%", username);
        }
    }
}
