using BarkFluff.Builder;
using Xunit;

namespace BarkFluff.Builder.Tests;

internal static class BuilderTestSupport
{
    public static BuilderModel ValidModel() => new()
    {
        PublicDomain = "node.example.com",
        PostgresPassword = "synthetic-postgres-password",
        RabbitPassword = "synthetic-rabbit-password",
        MinioRootPassword = "synthetic-minio-password",
        SetupToken = "synthetic-setup-token",
        LiveKitApiKey = "LK0123456789abcdef",
        LiveKitApiSecret = "synthetic-livekit-secret",
        TelegramBotToken = "123456:synthetic-bot-token",
        TelegramOwnerId = "123456789:owner"
    };

    public static string FindRepositoryRoot()
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
        {
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
                if (File.Exists(Path.Combine(directory.FullName, "BarkFluff.sln")))
                    return directory.FullName;
        }

        throw new DirectoryNotFoundException("Could not find BarkFluff.sln in parent directories.");
    }

    public static string Block(string compose, string service)
    {
        var services = ServiceSection(compose);
        var match = System.Text.RegularExpressions.Regex.Match(
            services,
            $@"(?ms)^  {System.Text.RegularExpressions.Regex.Escape(service)}:\s*\n(?<body>.*?)(?=^  [A-Za-z0-9_-]+:\s*$|\z)");
        Assert.True(match.Success, $"Compose service '{service}' was not found.");
        return match.Groups["body"].Value;
    }

    public static HashSet<string> ServiceNames(string compose)
    {
        var services = ServiceSection(compose);
        return System.Text.RegularExpressions.Regex.Matches(services, @"(?m)^  ([A-Za-z0-9_-]+):\s*$")
            .Select(match => match.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static string ServiceSection(string compose)
    {
        var start = compose.IndexOf("services:", StringComparison.Ordinal);
        Assert.True(start >= 0, "Compose file has no services section.");
        var services = compose[(start + "services:".Length)..];
        var end = System.Text.RegularExpressions.Regex.Match(services, @"(?m)^(?:networks|volumes|secrets):\s*$");
        return end.Success ? services[..end.Index] : services;
    }

    public static HashSet<string> SectionItems(string block, string sectionName, bool keyItems = false)
    {
        var lines = block.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var inSection = false;
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in lines)
        {
            if (!inSection)
            {
                if (line == $"    {sectionName}:")
                    inSection = true;
                continue;
            }
            if (line.Length > 0 && line.Length - line.TrimStart().Length <= 4)
                break;
            if (keyItems)
            {
                var key = System.Text.RegularExpressions.Regex.Match(line, @"^      ([A-Za-z0-9_-]+):");
                if (key.Success)
                    result.Add(key.Groups[1].Value);
            }
            else
            {
                var value = System.Text.RegularExpressions.Regex.Match(line, @"^      - (.+)$");
                if (value.Success)
                    result.Add(value.Groups[1].Value.Trim());
            }
        }
        return result;
    }

    public static HashSet<string> DependencyItems(string block) =>
        SectionItems(block, "depends_on").Concat(SectionItems(block, "depends_on", keyItems: true))
            .ToHashSet(StringComparer.Ordinal);
}
