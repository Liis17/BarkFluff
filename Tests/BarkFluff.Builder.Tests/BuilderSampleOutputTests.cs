using BarkFluff.Builder;
using Xunit;

namespace BarkFluff.Builder.Tests;

public sealed class BuilderSampleOutputTests
{
    [Fact]
    public void GenerateSamplesWhenOutputDirectoryIsConfigured()
    {
        var outputDirectory = Environment.GetEnvironmentVariable("BUILDER_SAMPLE_OUTPUT");
        if (string.IsNullOrWhiteSpace(outputDirectory))
            return;

        outputDirectory = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(outputDirectory);
        var (certificatePath, keyPath) = GetCertificateSources(outputDirectory);
        var dockerNetworkName = Environment.GetEnvironmentVariable("BUILDER_DOCKER_NETWORK")
            ?? "builder-sample-network";

        var samples = new[]
        {
            new Sample("compose", NginxMode.Compose, IncludeSeq: true, IncludeMinio: true, IncludeLognara: false),
            new Sample("compose-lognara", NginxMode.Compose, IncludeSeq: true, IncludeMinio: true, IncludeLognara: true),
            new Sample("external-container", NginxMode.ExternalContainer, IncludeSeq: false, IncludeMinio: false, IncludeLognara: false),
            new Sample("host", NginxMode.Host, IncludeSeq: true, IncludeMinio: false, IncludeLognara: false),
            new Sample("none", NginxMode.None, IncludeSeq: false, IncludeMinio: true, IncludeLognara: false)
        };

        foreach (var sample in samples)
        {
            var sampleDirectory = Path.Combine(outputDirectory, sample.Name);
            var model = BuilderTestSupport.ValidModel();
            model.OutputPath = sampleDirectory;
            model.DockerNetworkName = dockerNetworkName;
            model.NginxMode = sample.NginxMode;
            model.NginxCertificateSourcePath = certificatePath;
            model.NginxKeySourcePath = keyPath;
            model.NginxCertificatePath = "/etc/nginx/certs/barkfluff-crt.pem";
            model.NginxKeyPath = "/etc/nginx/certs/barkfluff-key.pem";
            model.IncludeSeq = sample.IncludeSeq;
            model.SeqAdminPassword = "synthetic-seq-admin-password";
            model.SeqApiKey = "synthetic-seq-api-key";
            model.IncludeMinio = sample.IncludeMinio;
            model.LognaraCoreUrl = "https://lognara.example.com/v1/batches";
            model.LognaraCoreToken = "synthetic-lognara-core-token";
            model.LognaraServer = "builder-sample";
            model.LognaraEnvironment = "ci";
            model.SetGlobalLognara(sample.IncludeLognara);

            var tokenNumber = 0;
            foreach (var target in model.AllLognaraTargets())
                target.RelayToken = $"synthetic-lognara-relay-{tokenNumber++}";

            BuilderOutputWriter.Write(model);

            var certificateDirectory = Path.Combine(sampleDirectory, "certs");
            Directory.CreateDirectory(certificateDirectory);
            File.Copy(certificatePath, Path.Combine(certificateDirectory, "barkfluff-crt.pem"), overwrite: true);
            File.Copy(keyPath, Path.Combine(certificateDirectory, "barkfluff-key.pem"), overwrite: true);
        }
    }

    private static (string CertificatePath, string KeyPath) GetCertificateSources(string outputDirectory)
    {
        var certificatePath = Environment.GetEnvironmentVariable("BUILDER_CERTIFICATE_SOURCE");
        var keyPath = Environment.GetEnvironmentVariable("BUILDER_KEY_SOURCE");
        if (!string.IsNullOrWhiteSpace(certificatePath) && !string.IsNullOrWhiteSpace(keyPath))
        {
            if (!File.Exists(certificatePath))
                throw new FileNotFoundException("Builder sample certificate was not found.", certificatePath);
            if (!File.Exists(keyPath))
                throw new FileNotFoundException("Builder sample key was not found.", keyPath);
            return (certificatePath, keyPath);
        }

        certificatePath = Path.Combine(outputDirectory, ".certificate-placeholder.pem");
        keyPath = Path.Combine(outputDirectory, ".key-placeholder.pem");
        File.WriteAllText(certificatePath, "-----BEGIN CERTIFICATE-----\nsynthetic-placeholder\n-----END CERTIFICATE-----\n");
        File.WriteAllText(keyPath, "-----BEGIN PRIVATE KEY-----\nsynthetic-placeholder\n-----END PRIVATE KEY-----\n");
        return (certificatePath, keyPath);
    }

    private sealed record Sample(
        string Name,
        NginxMode NginxMode,
        bool IncludeSeq,
        bool IncludeMinio,
        bool IncludeLognara);
}
