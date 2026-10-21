using System.IO;
using System.Text;

namespace BarkFluff.Builder;

public sealed class BuilderValidationException(IReadOnlyList<string> errors)
    : InvalidOperationException(string.Join(Environment.NewLine, errors))
{
    public IReadOnlyList<string> Errors { get; } = errors;
}

public static class BuilderOutputWriter
{
    private static readonly UTF8Encoding Utf8WithoutBom = new(false);
    private static readonly HashSet<string> OwnedFiles = new(StringComparer.Ordinal)
    {
        ".env",
        "docker-compose.yml",
        "docker-compose.setup.yml",
        "secrets/setup_token",
        "livekit/livekit.yaml",
        "nginx/nginx.conf",
        "nginx/sites/00-default.conf",
        "nginx/sites/00-rate-limits.conf",
        "nginx/sites/01-ssl-params.conf",
        "nginx/sites/00-real-ip.conf",
        "nginx/sites/identity.conf",
        "nginx/sites/users.conf",
        "nginx/sites/messages.conf",
        "nginx/sites/files.conf",
        "nginx/sites/files-media.conf",
        "nginx/sites/updates.conf",
        "nginx/sites/onliner.conf",
        "nginx/sites/fast-auth.conf",
        "nginx/sites/federation.conf",
        "nginx/sites/beacon.conf",
        "nginx/sites/bots.conf",
        "nginx/sites/calls.conf",
        "nginx/sites/livekit.conf",
        "nginx/sites/web.conf",
        "nginx/sites/developers.conf",
        "nginx/sites/admin-panel.conf",
        "nginx/sites/setup.conf",
        "certs/barkfluff-crt.pem",
        "certs/barkfluff-key.pem",
        "lognara/postgres.Dockerfile",
        "lognara/rabbitmq.Dockerfile",
        "lognara/redis.Dockerfile",
        "lognara/livekit.Dockerfile",
        "lognara/seq.Dockerfile",
        "lognara/minio.Dockerfile",
        "lognara/nginx.Dockerfile",
        "NEXT_STEPS.md"
    };

    public static IReadOnlyList<string> Write(BuilderModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (string.IsNullOrWhiteSpace(model.OutputPath))
            throw new ArgumentException("OutputPath is required.", nameof(model));

        var errors = BuilderModelValidator.Validate(model);
        if (errors.Count > 0)
            throw new BuilderValidationException(errors);

        var root = Path.GetFullPath(model.OutputPath);
        var textFiles = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [".env"] = ComposeGenerator.BuildEnv(model),
            ["docker-compose.yml"] = ComposeGenerator.BuildCompose(model),
            ["docker-compose.setup.yml"] = ComposeGenerator.BuildSetupCompose(model),
            ["secrets/setup_token"] = model.SetupToken,
            ["livekit/livekit.yaml"] = ComposeGenerator.BuildLiveKitConfig(model),
            ["NEXT_STEPS.md"] = BuilderNextStepsGenerator.Build(model)
        };

        foreach (var (path, content) in NginxGenerator.BuildFiles(model))
            textFiles[path] = content;
        foreach (var (name, content) in ComposeGenerator.BuildLognaraDockerfiles(model))
            textFiles[$"lognara/{name}"] = content;

        var certificateFiles = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        if (model.NginxMode == NginxMode.Compose)
        {
            certificateFiles["certs/barkfluff-crt.pem"] = File.ReadAllBytes(model.NginxCertificateSourcePath);
            certificateFiles["certs/barkfluff-key.pem"] = File.ReadAllBytes(model.NginxKeySourcePath);
        }

        Directory.CreateDirectory(root);
        foreach (var stale in OwnedFiles.Except(textFiles.Keys, StringComparer.Ordinal).Except(certificateFiles.Keys, StringComparer.Ordinal))
        {
            var path = FullPath(root, stale);
            if (File.Exists(path))
                File.Delete(path);
        }

        foreach (var (relativePath, content) in textFiles)
        {
            var path = FullPath(root, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var normalized = content.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
            File.WriteAllText(path, normalized, Utf8WithoutBom);
        }

        foreach (var (relativePath, content) in certificateFiles)
        {
            var path = FullPath(root, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, content);
        }

        return textFiles.Keys.Concat(certificateFiles.Keys).Order(StringComparer.Ordinal).ToArray();
    }

    private static string FullPath(string root, string relativePath) =>
        Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
}
