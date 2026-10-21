using System.Text.RegularExpressions;
using BarkFluff.Builder;
using Xunit;

namespace BarkFluff.Builder.Tests;

public sealed class BuilderCoreTests
{
    [Fact]
    public void ModelNotifiesChangesAndHasExpectedDefaults()
    {
        var model = new BuilderModel();
        var changed = new List<string?>();
        model.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        model.DockerNetworkName = "custom-network";

        Assert.Equal("barkfluff-network", new BuilderModel().DockerNetworkName);
        Assert.Equal(["DockerNetworkName"], changed);
        Assert.Equal("https://navigator.barkfluff.com", model.NavigatorUrl);
        Assert.Equal(NginxMode.None, model.NginxMode);
    }

    [Fact]
    public void ComposeHasAllRequiredServicesAndNamespacedContainers()
    {
        var model = BuilderTestSupport.ValidModel();
        model.IncludeSeq = true;
        model.IncludeMinio = true;
        var compose = ComposeGenerator.BuildCompose(model);

        var expected = new[]
        {
            "beacon", "settings", "files", "identity", "messages", "notification", "users", "fast-auth",
            "updates", "onliner", "federation", "cloud-messaging", "web", "developers", "calls", "bots",
            "admin-panel", "seq", "rabbitmq", "redis", "livekit", "postgres", "postgres-bootstrap", "minio"
        }.ToHashSet(StringComparer.Ordinal);
        Assert.True(expected.SetEquals(BuilderTestSupport.ServiceNames(compose)));
        Assert.StartsWith("name: barkfluff\n", compose);
        foreach (var service in expected)
            Assert.Contains($"container_name: barkfluff-{service}", BuilderTestSupport.Block(compose, service));
        Assert.Contains("docker.barkfluff.com/barkfluff-beacon-nightly:latest", compose);
        Assert.Contains("image: postgres:18", BuilderTestSupport.Block(compose, "postgres"));
        Assert.Contains("image: quay.io/minio/minio:RELEASE.2025-04-22T22-12-26Z-cpuv1", BuilderTestSupport.Block(compose, "minio"));
        Assert.Contains("external: true", compose);
        Assert.Contains("name: ${DOCKER_NETWORK_NAME}", compose);
    }

    [Theory]
    [InlineData("Nightly", "-nightly")]
    [InlineData("Dev", "-dev")]
    [InlineData("Release", "")]
    public void AppImagesUseSelectedChannel(string channel, string suffix)
    {
        var model = BuilderTestSupport.ValidModel();
        model.ImageChannel = channel;

        Assert.Contains($"docker.barkfluff.com/barkfluff-settings{suffix}:latest", ComposeGenerator.BuildCompose(model));
    }

    [Fact]
    public void OptionalServicesAndSeqReferencesFollowSelections()
    {
        var model = BuilderTestSupport.ValidModel();
        model.IncludeSeq = false;
        model.IncludeMinio = false;
        model.S3Endpoint = "https://storage.example.com";
        foreach (var target in model.AllLognaraTargets())
            target.SeqEnable = true;

        var compose = ComposeGenerator.BuildCompose(model);
        var env = ComposeGenerator.BuildEnv(model);

        Assert.DoesNotContain("\n  seq:\n", compose);
        Assert.DoesNotContain("\n  minio:\n", compose);
        Assert.DoesNotContain("\n      seq:\n", compose);
        Assert.Contains("Seq__Enabled: \"false\"", BuilderTestSupport.Block(compose, "admin-panel"));
        Assert.Contains("NODE_S3_ENDPOINT=\"https://storage.example.com\"", env);
        Assert.DoesNotContain("nginx:", compose);
        Assert.All(model.AllLognaraTargets(), target =>
            Assert.Contains($"{target.Prefix}_SEQ_ENABLE=\"false\"", env));
    }

    [Fact]
    public void HostNginxBindsOnlyActualUpstreamPortsToLoopback()
    {
        var model = BuilderTestSupport.ValidModel();
        model.NginxMode = NginxMode.Host;
        var compose = ComposeGenerator.BuildCompose(model);

        foreach (var port in new[] { 7000, 7001, 7002, 7005, 7006, 7007, 7008, 7009, 7015, 7016, 7020, 7021, 7025, 7027, 7028, 7030, 51888 })
            Assert.Contains($"127.0.0.1:{port}:{port}", compose);
        Assert.Contains("127.0.0.1:7880:7880", compose);
        Assert.DoesNotContain("127.0.0.1:7003:7003", compose);
        Assert.DoesNotContain("127.0.0.1:7004:7004", compose);
        Assert.Contains("127.0.0.1:${SETUP_HOST_PORT}:7032", ComposeGenerator.BuildSetupCompose(model));
    }

    [Fact]
    public void HostPortsRemainSeparateFromContainerPorts()
    {
        var model = BuilderTestSupport.ValidModel();
        model.PostgresHostPort = "5544";
        model.IncludeSeq = true;
        model.SeqHostPort = "8088";
        var compose = ComposeGenerator.BuildCompose(model);

        Assert.Contains("${POSTGRES_HOST_PORT}:5432", BuilderTestSupport.Block(compose, "postgres"));
        Assert.Contains("${SEQ_HOST_PORT}:80", BuilderTestSupport.Block(compose, "seq"));
        Assert.Contains("${MINIO_API_HOST_PORT}:9000", BuilderTestSupport.Block(compose, "minio"));
        Assert.Contains("${MINIO_CONSOLE_HOST_PORT}:9001", BuilderTestSupport.Block(compose, "minio"));
    }

    [Fact]
    public void BootstrapChecksDatabaseBeforeCreatingIt()
    {
        var compose = ComposeGenerator.BuildCompose(BuilderTestSupport.ValidModel());
        Assert.Contains("SELECT 1 FROM pg_database WHERE datname = '$$database'", compose);
        Assert.Contains("createdb --maintenance-db=postgres \"$$database\"", compose);
        Assert.DoesNotContain("createdb --if-not-exists", compose);
    }

    [Fact]
    public void SetupComposeSharesProjectNetworkAndLognaraRelaySpool()
    {
        var model = BuilderTestSupport.ValidModel();
        model.IncludeLognara = true;
        model.NginxMode = NginxMode.Compose;
        model.Application("SETUP").LognaraEnable = true;
        model.Application("SETUP_SETTINGS").LognaraEnable = true;
        model.Application("SETTINGS").LognaraEnable = true;
        model.Infrastructure("POSTGRES").LognaraEnable = true;
        model.Infrastructure("POSTGRES_BOOTSTRAP").LognaraEnable = true;
        model.Infrastructure("SETUP_POSTGRES").LognaraEnable = true;
        model.Infrastructure("SETUP_POSTGRES_BOOTSTRAP").LognaraEnable = true;

        var main = ComposeGenerator.BuildCompose(model);
        var setup = ComposeGenerator.BuildSetupCompose(model);

        Assert.StartsWith("name: barkfluff\n", setup);
        Assert.Contains("  nginx:", setup);
        Assert.Contains("name: barkfluff-lognara-relay-spool", main);
        Assert.Contains("name: barkfluff-lognara-relay-spool", setup);
        Assert.Contains("lognara-relay-spool:/var/lib/lognara-relay/spool", setup);
        Assert.Contains("NODE_PUBLIC_DOMAIN", BuilderTestSupport.Block(setup, "settings"));
        Assert.Contains("NODE_NAVIGATOR_URL", BuilderTestSupport.Block(setup, "settings"));
        Assert.Contains("NODE_S3_ENDPOINT", BuilderTestSupport.Block(setup, "settings"));
        Assert.Contains("init: true", BuilderTestSupport.Block(setup, "setup"));
        Assert.Contains("stop_grace_period: 90s", BuilderTestSupport.Block(setup, "setup"));
        Assert.Contains("lognara-buffer-setup:/app/logs", BuilderTestSupport.Block(setup, "setup"));
        Assert.Contains("/usr/local/bin/docker-entrypoint.sh", BuilderTestSupport.Block(main, "postgres"));
        Assert.Contains("max_connections=150", BuilderTestSupport.Block(main, "postgres"));
        Assert.Contains("\"/bin/sh\", \"-ec\"", BuilderTestSupport.Block(main, "postgres-bootstrap"));
        Assert.Contains("SELECT 1 FROM pg_database", BuilderTestSupport.Block(setup, "postgres-bootstrap"));
        Assert.Contains("Lognara__AgentExecutable", BuilderTestSupport.Block(main, "settings"));
    }

    [Fact]
    public void CommandsPreserveArgumentBoundaries()
    {
        var model = BuilderTestSupport.ValidModel();
        model.NginxMode = NginxMode.Compose;
        var compose = ComposeGenerator.BuildCompose(model);

        Assert.Contains("command: [\"redis-server\", \"--appendonly\", \"yes\"]", BuilderTestSupport.Block(compose, "redis"));
        Assert.Contains("command: [\"--config\", \"/etc/livekit/livekit.yaml\"]", BuilderTestSupport.Block(compose, "livekit"));
        Assert.Contains("command: [\"server\", \"/data\", \"--console-address\", \":9001\"]", BuilderTestSupport.Block(compose, "minio"));
        Assert.Contains("command: [\"nginx\", \"-g\", \"daemon off;\"]", BuilderTestSupport.Block(compose, "nginx"));
    }

    [Fact]
    public void LiveKitConfigUsesGeneratedCredentialsAndCallsWebhook()
    {
        var model = BuilderTestSupport.ValidModel();
        var config = ComposeGenerator.BuildLiveKitConfig(model);

        Assert.Contains($"\"{model.LiveKitApiKey}\": \"{model.LiveKitApiSecret}\"", config);
        Assert.Contains("http://calls:7026/livekit/webhook", config);
        Assert.DoesNotContain("https://", config);
    }

    [Fact]
    public void EnvironmentEscapingPreservesComposeDotenvSpecialCharacters()
    {
        var escaped = ComposeGenerator.EscapeEnv("$value # \"quoted\" \\\nsecond line");
        Assert.Contains("$$value #", escaped);
        Assert.Contains("\\\"quoted\\\"", escaped);
        Assert.Contains("\\\\", escaped);
        Assert.Contains("\\n", escaped);
        Assert.DoesNotContain("\n", escaped);

        var model = BuilderTestSupport.ValidModel();
        model.FirebasePrivateKey = "private line one\nprivate line two";
        Assert.Contains("FIREBASE_PRIVATE_KEY=\"private line one\\nprivate line two\"", ComposeGenerator.BuildEnv(model));
    }

    [Fact]
    public void RandomCredentialHelpersReturnNonEmptyValues()
    {
        var liveKit = ComposeGenerator.GenerateLiveKitCredentials();
        Assert.StartsWith("LK", liveKit.ApiKey);
        Assert.True(liveKit.ApiSecret.Length >= 40);
        Assert.Equal(64, ComposeGenerator.GenerateSetupToken().Length);
    }

    [Fact]
    public void LognaraDockerfileConstantsMatchInstallerAndChecksums()
    {
        var root = BuilderTestSupport.FindRepositoryRoot();
        var installer = File.ReadAllText(Path.Combine(root, "Docker", "Lognara", "agent", "install-agent.sh"));
        var checksums = File.ReadAllText(Path.Combine(root, "Docker", "Lognara", "agent", "SHA256SUMS"));
        var release = Regex.Match(installer, @"(?m)^release='([^']+)'$").Groups[1].Value;
        var model = BuilderTestSupport.ValidModel();
        model.IncludeLognara = true;
        model.Infrastructure("POSTGRES").LognaraEnable = true;

        var dockerfile = ComposeGenerator.BuildLognaraDockerfiles(model)["postgres.Dockerfile"];
        Assert.Contains($"ARG LOGNARA_AGENT_RELEASE={release}", dockerfile);
        foreach (Match checksum in Regex.Matches(checksums, @"(?m)^([0-9a-f]{64})\s+lognara-agent-linux-(?:amd64|arm64)\.tar\.gz$"))
            Assert.Contains($"checksum=\"{checksum.Groups[1].Value}\"", dockerfile);
        Assert.Equal(2, Regex.Matches(checksums, @"(?m)^[0-9a-f]{64}\s+lognara-agent-linux-").Count);
    }
}
