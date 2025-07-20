namespace Barkfluff.SiteWebServer
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);
            var app = builder.Build();

            app.MapGet("/", async context =>
            {
                var filePath = Path.Combine("Pages", "index.html");
                if (!File.Exists(filePath))
                {
                    context.Response.StatusCode = 404;
                    await context.Response.WriteAsync("Page not found.");
                    return;
                }

                context.Response.ContentType = "text/html";
                await context.Response.SendFileAsync(filePath);
            });

            app.Run("http://0.0.0.0:12121");
        }
    }
}
