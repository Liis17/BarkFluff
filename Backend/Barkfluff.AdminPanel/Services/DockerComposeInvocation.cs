namespace Barkfluff.AdminPanel.Services;

internal static class DockerComposeInvocation
{
    public static IReadOnlyList<string> ResolveFiles(string baseComposeFile, string? activeFiles)
    {
        var basePath = Path.GetFullPath(baseComposeFile);
        if (string.IsNullOrWhiteSpace(activeFiles))
            return [basePath];

        var baseDirectory = Path.GetDirectoryName(basePath)!;
        var files = activeFiles
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(path => !string.Equals(path, "<no value>", StringComparison.Ordinal))
            .Select(path => Path.GetFullPath(path, baseDirectory))
            .Distinct(PathComparer)
            .ToArray();

        return files.Length == 0 ? [basePath] : files;
    }

    public static IReadOnlyList<string> CreateCommandArguments(
        IReadOnlyList<string> composeFiles,
        string envFile,
        params string[] commandArguments)
    {
        var result = new List<string>
        {
            "compose",
            "--project-name",
            "barkfluff",
            "--env-file",
            envFile
        };

        foreach (var file in composeFiles)
        {
            result.Add("-f");
            result.Add(file);
        }

        result.AddRange(commandArguments);
        return result;
    }

    public static void AddReadOnlyMounts(List<string> arguments, IReadOnlyList<string> composeFiles, string envFile)
    {
        var mountedDirectories = new HashSet<string>(PathComparer);
        foreach (var file in composeFiles)
        {
            var directory = Path.GetDirectoryName(file);
            if (directory is null || !mountedDirectories.Add(directory))
                continue;

            arguments.Add("--mount");
            arguments.Add($"type=bind,src={directory},dst={directory},readonly");
        }

        var fullEnvFile = Path.GetFullPath(envFile);
        var envDirectory = Path.GetDirectoryName(fullEnvFile);
        if (envDirectory is null || !mountedDirectories.Contains(envDirectory))
        {
            arguments.Add("--mount");
            arguments.Add($"type=bind,src={fullEnvFile},dst={fullEnvFile},readonly");
        }
    }

    public static string ToShellCommand(IEnumerable<string> arguments) =>
        string.Join(" ", arguments.Select(QuoteShellArgument));

    private static string QuoteShellArgument(string argument) =>
        $"'{argument.Replace("'", "'\\''", StringComparison.Ordinal)}'";

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;
}
