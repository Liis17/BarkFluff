using Barkfluff.AdminPanel.Services;

using Xunit;

namespace BarkFluff.AdminPanel.Tests.Services;

public sealed class DockerComposeInvocationTests
{
    private static readonly string ComposeDirectory = Path.Combine(Path.GetTempPath(), "barkfluff-compose-tests");
    private static readonly string BaseComposeFile = Path.Combine(ComposeDirectory, "docker-compose.yml");
    private static readonly string LognaraComposeFile = Path.Combine(ComposeDirectory, "docker-compose.lognara.yml");
    private static readonly string EnvFile = Path.Combine(ComposeDirectory, ".env");

    [Fact]
    public void Base_only_pull_and_up_commands_include_only_the_base_file()
    {
        var files = DockerComposeInvocation.ResolveFiles(BaseComposeFile, BaseComposeFile);

        Assert.Equal(new[] { Path.GetFullPath(BaseComposeFile) }, files);
        Assert.Equal(
            new[] { "compose", "--project-name", "barkfluff", "--env-file", EnvFile, "-f", Path.GetFullPath(BaseComposeFile), "pull", "beacon" },
            DockerComposeInvocation.CreateCommandArguments(files, EnvFile, "pull", "beacon"));
        Assert.Equal(
            new[] { "compose", "--project-name", "barkfluff", "--env-file", EnvFile, "-f", Path.GetFullPath(BaseComposeFile), "up", "--force-recreate", "--no-deps", "--build", "-d", "beacon" },
            DockerComposeInvocation.CreateCommandArguments(files, EnvFile, "up", "--force-recreate", "--no-deps", "--build", "-d", "beacon"));

        var mounts = new List<string>();
        DockerComposeInvocation.AddReadOnlyMounts(mounts, files, EnvFile);
        Assert.Equal(
            new[] { "--mount", $"type=bind,src={Path.GetDirectoryName(Path.GetFullPath(BaseComposeFile))},dst={Path.GetDirectoryName(Path.GetFullPath(BaseComposeFile))},readonly" },
            mounts);
    }

    [Fact]
    public void Active_overlay_is_preserved_in_pull_and_up_commands()
    {
        var files = DockerComposeInvocation.ResolveFiles(
            BaseComposeFile,
            $"{Path.GetFullPath(BaseComposeFile)},{Path.GetFullPath(LognaraComposeFile)}");

        Assert.Equal(new[] { Path.GetFullPath(BaseComposeFile), Path.GetFullPath(LognaraComposeFile) }, files);
        Assert.Equal(
            new[] { "compose", "--project-name", "barkfluff", "--env-file", EnvFile, "-f", Path.GetFullPath(BaseComposeFile), "-f", Path.GetFullPath(LognaraComposeFile), "pull", "beacon" },
            DockerComposeInvocation.CreateCommandArguments(files, EnvFile, "pull", "beacon"));
        Assert.Equal(
            new[] { "compose", "--project-name", "barkfluff", "--env-file", EnvFile, "-f", Path.GetFullPath(BaseComposeFile), "-f", Path.GetFullPath(LognaraComposeFile), "up", "--force-recreate", "--no-deps", "--build", "-d", "beacon" },
            DockerComposeInvocation.CreateCommandArguments(files, EnvFile, "up", "--force-recreate", "--no-deps", "--build", "-d", "beacon"));

        var mounts = new List<string>();
        DockerComposeInvocation.AddReadOnlyMounts(mounts, files, EnvFile);
        Assert.Equal(
            new[] { "--mount", $"type=bind,src={Path.GetDirectoryName(Path.GetFullPath(BaseComposeFile))},dst={Path.GetDirectoryName(Path.GetFullPath(BaseComposeFile))},readonly" },
            mounts);
    }

    [Fact]
    public void Relative_active_file_paths_resolve_from_the_base_compose_directory()
    {
        var files = DockerComposeInvocation.ResolveFiles(
            BaseComposeFile,
            "docker-compose.yml,docker-compose.lognara.yml");

        Assert.Equal(new[] { Path.GetFullPath(BaseComposeFile), Path.GetFullPath(LognaraComposeFile) }, files);
    }
}
