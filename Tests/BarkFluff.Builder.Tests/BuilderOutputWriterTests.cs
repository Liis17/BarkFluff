using BarkFluff.Builder;
using Xunit;

namespace BarkFluff.Builder.Tests;

public sealed class BuilderOutputWriterTests
{
    [Fact]
    public void WriterRemovesOnlyItsStaleFilesAndPreservesUserData()
    {
        var temporaryRoot = Path.Combine(Path.GetTempPath(), "barkfluff-builder-tests-" + Guid.NewGuid().ToString("N"));
        var output = Path.Combine(temporaryRoot, "output");
        Directory.CreateDirectory(Path.Combine(output, "data"));
        Directory.CreateDirectory(Path.Combine(output, "backups"));
        Directory.CreateDirectory(Path.Combine(output, "admindata"));
        Directory.CreateDirectory(Path.Combine(output, "nginx", "sites"));
        File.WriteAllText(Path.Combine(output, "custom.txt"), "keep");
        File.WriteAllText(Path.Combine(output, "data", "keep.txt"), "data");
        File.WriteAllText(Path.Combine(output, "backups", "keep.txt"), "backup");
        File.WriteAllText(Path.Combine(output, "admindata", "keep.txt"), "admin");
        File.WriteAllText(Path.Combine(output, "nginx", "sites", "custom.conf"), "custom nginx");

        try
        {
            var certificate = Path.Combine(temporaryRoot, "source.crt");
            var key = Path.Combine(temporaryRoot, "source.key");
            File.WriteAllBytes(certificate, [1, 2, 3]);
            File.WriteAllBytes(key, [4, 5, 6]);
            var model = BuilderTestSupport.ValidModel();
            model.OutputPath = output;
            model.NginxMode = NginxMode.Compose;
            model.NginxCertificateSourcePath = certificate;
            model.NginxKeySourcePath = key;
            model.BehindCloudflare = true;
            model.IncludeLognara = true;
            model.LognaraCoreUrl = "https://core.example.com/v1/batches";
            model.LognaraCoreToken = "synthetic-core-token";
            model.LognaraServer = "node-1";
            model.Infrastructure("POSTGRES").LognaraEnable = true;
            model.Infrastructure("POSTGRES").RelayToken = "synthetic-relay-token";

            var written = BuilderOutputWriter.Write(model);

            Assert.Contains(".env", written);
            Assert.Contains("docker-compose.yml", written);
            Assert.Equal([1, 2, 3], File.ReadAllBytes(Path.Combine(output, "certs", "barkfluff-crt.pem")));
            Assert.Equal([4, 5, 6], File.ReadAllBytes(Path.Combine(output, "certs", "barkfluff-key.pem")));
            Assert.True(File.Exists(Path.Combine(output, "nginx", "sites", "00-real-ip.conf")));
            Assert.True(File.Exists(Path.Combine(output, "lognara", "postgres.Dockerfile")));
            Assert.Equal("synthetic-setup-token", File.ReadAllText(Path.Combine(output, "secrets", "setup_token")));
            Assert.Equal("custom nginx", File.ReadAllText(Path.Combine(output, "nginx", "sites", "custom.conf")));

            var environmentBytes = File.ReadAllBytes(Path.Combine(output, ".env"));
            Assert.False(environmentBytes.Length >= 3 && environmentBytes[0] == 0xEF && environmentBytes[1] == 0xBB && environmentBytes[2] == 0xBF);
            Assert.DoesNotContain((byte)'\r', environmentBytes);

            model.BehindCloudflare = false;
            model.IncludeLognara = false;
            model.NginxMode = NginxMode.None;
            BuilderOutputWriter.Write(model);

            Assert.False(File.Exists(Path.Combine(output, "nginx", "sites", "00-real-ip.conf")));
            Assert.False(File.Exists(Path.Combine(output, "nginx", "nginx.conf")));
            Assert.False(File.Exists(Path.Combine(output, "certs", "barkfluff-crt.pem")));
            Assert.False(File.Exists(Path.Combine(output, "lognara", "postgres.Dockerfile")));
            Assert.True(File.Exists(Path.Combine(output, "custom.txt")));
            Assert.Equal("data", File.ReadAllText(Path.Combine(output, "data", "keep.txt")));
            Assert.Equal("backup", File.ReadAllText(Path.Combine(output, "backups", "keep.txt")));
            Assert.Equal("admin", File.ReadAllText(Path.Combine(output, "admindata", "keep.txt")));
            Assert.Equal("custom nginx", File.ReadAllText(Path.Combine(output, "nginx", "sites", "custom.conf")));
        }
        finally
        {
            if (Directory.Exists(temporaryRoot))
                Directory.Delete(temporaryRoot, recursive: true);
        }
    }

    [Fact]
    public void WriterValidatesBeforeCreatingOutputDirectory()
    {
        var temporaryRoot = Path.Combine(Path.GetTempPath(), "barkfluff-builder-invalid-" + Guid.NewGuid().ToString("N"));
        try
        {
            var model = new BuilderModel { OutputPath = temporaryRoot };
            Assert.Throws<BuilderValidationException>(() => BuilderOutputWriter.Write(model));
            Assert.False(Directory.Exists(temporaryRoot));
        }
        finally
        {
            if (Directory.Exists(temporaryRoot))
                Directory.Delete(temporaryRoot, recursive: true);
        }
    }
}
