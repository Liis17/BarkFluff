namespace Barkfluff.UsersRedirector
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);
            Console.WriteLine("Запуск");
            builder.WebHost.UseKestrel(options =>
            {
                options.ListenAnyIP(64641);
            });

            var app = builder.Build();

            app.Map("/{**catch-all}", async context =>
            {
                var host = context.Request.Host.Host;
                var subdomain = GetFirstLevelSubdomain(host);

                var html = await LoadHtmlForSubdomainAsync(subdomain);

                context.Response.ContentType = "text/html; charset=utf-8";
                await context.Response.WriteAsync(html);
            });

            // Вот ЭТО держит приложение живым
            app.Run();
        }

        private static string GetFirstLevelSubdomain(string host)
        {
            if (string.IsNullOrEmpty(host)) return string.Empty;

            host = host.ToLowerInvariant();
            const string domain = "barkfluff.com";

            if (host == domain) return string.Empty;

            var suffix = "." + domain;
            if (host.EndsWith(suffix))
            {
                var prefix = host.Substring(0, host.Length - suffix.Length);
                var parts = prefix.Split('.', StringSplitOptions.RemoveEmptyEntries);
                return parts.Length > 0 ? parts[parts.Length - 1] : string.Empty;
            }

            return string.Empty;
        }

        private static async System.Threading.Tasks.Task<string> LoadHtmlForSubdomainAsync(string subdomain)
        {
            Console.WriteLine($"Запрос по {subdomain}");
            var baseDir = AppContext.BaseDirectory;
            var folder = Path.Combine(baseDir, "html");

            var fileName = "page.html";
            var path = Path.Combine(folder, fileName);

            if (!File.Exists(path))
            {
                // Fallback to default if specific file not found
                var defaultPath = Path.Combine(folder, "default.html");
                if (File.Exists(defaultPath))
                    return await File.ReadAllTextAsync(defaultPath);

                // If nothing found, return minimal message
                return "<html><body><h1>Not found</h1></body></html>";
            }
            var html = await File.ReadAllTextAsync(path);
            html = html.Replace("%%username%%", subdomain);
            return html;
        }
    }
}
