using BarkFluff.DBEditor.Models;

using System.IO;
using System.Text;
using System.Text.Json;

namespace BarkFluff.DBEditor.Services
{
    public static class CredentialsService
    {
        private static readonly string FolderPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
        private static readonly string FilePath = Path.Combine(FolderPath, "db_config.json");

        public static void SaveCredentials(DbCredentials creds)
        {
            if (!Directory.Exists(FolderPath)) Directory.CreateDirectory(FolderPath);

            string json = JsonSerializer.Serialize(creds);
            string base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));

            File.WriteAllText(FilePath, base64);
        }

        public static DbCredentials? LoadCredentials()
        {
            if (!File.Exists(FilePath)) return null;

            try
            {
                string base64 = File.ReadAllText(FilePath);
                string json = Encoding.UTF8.GetString(Convert.FromBase64String(base64));
                return JsonSerializer.Deserialize<DbCredentials>(json);
            }
            catch
            {
                return null;
            }
        }
    }
}
