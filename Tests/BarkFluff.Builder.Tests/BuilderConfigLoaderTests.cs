using BarkFluff.Builder;
using Xunit;

namespace BarkFluff.Builder.Tests;

public sealed class BuilderConfigLoaderTests
{
    public static IEnumerable<object[]> RoundTripConfigurations() =>
        from channel in new[] { "Nightly", "Dev", "Release" }
        from nginx in Enum.GetValues<NginxMode>()
        from includeSeq in new[] { false, true }
        from includeMinio in new[] { false, true }
        from includeLognara in new[] { false, true }
        select new object[] { channel, nginx, includeSeq, includeMinio, includeLognara };

    [Theory]
    [MemberData(nameof(RoundTripConfigurations))]
    public void BuildWriteLoadBuildPreservesEveryGeneratedFile(
        string channel,
        NginxMode nginx,
        bool includeSeq,
        bool includeMinio,
        bool includeLognara)
    {
        var temporaryRoot = CreateTemporaryDirectory();
        try
        {
            var output = Path.Combine(temporaryRoot, "node");
            var certificate = Path.Combine(temporaryRoot, "source-certificate.pem");
            var key = Path.Combine(temporaryRoot, "source-key.pem");
            File.WriteAllBytes(certificate, [0, 1, 2, 3, 254, 255]);
            File.WriteAllBytes(key, [9, 8, 7, 6, 1, 0]);

            var model = CreateModel(output, channel, nginx, includeSeq, includeMinio, includeLognara);
            if (nginx == NginxMode.Compose)
            {
                model.NginxCertificateSourcePath = certificate;
                model.NginxKeySourcePath = key;
            }

            var firstFiles = BuilderOutputWriter.Write(model);
            var firstContents = firstFiles.ToDictionary(
                relativePath => relativePath,
                relativePath => File.ReadAllBytes(Path.Combine(output, relativePath)),
                StringComparer.Ordinal);
            var loaded = BuilderConfigLoader.Load(output);

            Assert.Equal(Path.GetFullPath(output), loaded.OutputPath);
            Assert.Equal(channel, loaded.ImageChannel);
            Assert.Equal(includeSeq, loaded.IncludeSeq);
            Assert.Equal(includeMinio, loaded.IncludeMinio);
            Assert.Equal(includeLognara, loaded.IncludeLognara);
            Assert.Equal(nginx, loaded.NginxMode);
            if (nginx == NginxMode.Compose)
            {
                Assert.Equal(Path.Combine(output, "certs", "barkfluff-crt.pem"), loaded.NginxCertificateSourcePath);
                Assert.Equal(Path.Combine(output, "certs", "barkfluff-key.pem"), loaded.NginxKeySourcePath);
            }

            var secondFiles = BuilderOutputWriter.Write(loaded);
            Assert.Equal(firstFiles, secondFiles);
            foreach (var relativePath in firstFiles)
                Assert.Equal(firstContents[relativePath], File.ReadAllBytes(Path.Combine(output, relativePath)));
        }
        finally
        {
            Directory.Delete(temporaryRoot, recursive: true);
        }
    }

    [Fact]
    public void MissingComposeAndEnvironmentFilesHaveReadableErrors()
    {
        var temporaryRoot = CreateTemporaryDirectory();
        try
        {
            var missingCompose = Assert.Throws<FileNotFoundException>(() => BuilderConfigLoader.Load(temporaryRoot));
            Assert.Contains("docker-compose.yml", missingCompose.Message);

            File.WriteAllText(Path.Combine(temporaryRoot, "docker-compose.yml"), "services:\n  beacon:\n");
            var missingEnvironment = Assert.Throws<FileNotFoundException>(() => BuilderConfigLoader.Load(temporaryRoot));
            Assert.Contains(".env", missingEnvironment.Message);
        }
        finally
        {
            Directory.Delete(temporaryRoot, recursive: true);
        }
    }

    [Theory]
    [InlineData("secrets/setup_token")]
    [InlineData("livekit/livekit.yaml")]
    public void MissingSecretFilesHaveReadableErrors(string relativePath)
    {
        var temporaryRoot = CreateTemporaryDirectory();
        try
        {
            var output = Path.Combine(temporaryRoot, "node");
            BuilderOutputWriter.Write(CreateModel(output, "Nightly", NginxMode.None, false, false, false));
            File.Delete(Path.Combine(output, relativePath));

            var error = Assert.Throws<FileNotFoundException>(() => BuilderConfigLoader.Load(output));
            Assert.Contains(relativePath, error.Message);
        }
        finally
        {
            Directory.Delete(temporaryRoot, recursive: true);
        }
    }

    [Fact]
    public void SetupAndLiveKitCredentialsAreLoadedFromTheirFiles()
    {
        var temporaryRoot = CreateTemporaryDirectory();
        try
        {
            var output = Path.Combine(temporaryRoot, "node");
            var model = CreateModel(output, "Nightly", NginxMode.None, false, false, false);
            BuilderOutputWriter.Write(model);

            var environmentPath = Path.Combine(output, ".env");
            var lines = File.ReadAllLines(environmentPath).Select(line => line switch
            {
                var value when value.StartsWith("SETUP_TOKEN=", StringComparison.Ordinal) => "SETUP_TOKEN=\"env-setup-token\"",
                var value when value.StartsWith("LIVEKIT_API_KEY=", StringComparison.Ordinal) => "LIVEKIT_API_KEY=\"env-livekit-key\"",
                var value when value.StartsWith("LIVEKIT_API_SECRET=", StringComparison.Ordinal) => "LIVEKIT_API_SECRET=\"env-livekit-secret\"",
                _ => line
            });
            File.WriteAllLines(environmentPath, lines);

            var loaded = BuilderConfigLoader.Load(output);
            Assert.Equal(model.SetupToken, loaded.SetupToken);
            Assert.Equal(model.LiveKitApiKey, loaded.LiveKitApiKey);
            Assert.Equal(model.LiveKitApiSecret, loaded.LiveKitApiSecret);
        }
        finally
        {
            Directory.Delete(temporaryRoot, recursive: true);
        }
    }

    [Fact]
    public void ComposeEscapedDollarAndMultilineSecretsRoundTripExactly()
    {
        const string specialValue = "$value|${NOT_A_VAR}|$$|a$|\"|'|\\|\\n|#| # comment| leading and trailing |a=b|first line\nsecond line|Привет雪";
        var temporaryRoot = CreateTemporaryDirectory();
        try
        {
            var output = Path.Combine(temporaryRoot, "node");
            var model = CreateModel(output, "Nightly", NginxMode.None, false, false, false);
            model.FirebasePrivateKey = specialValue;
            BuilderOutputWriter.Write(model);

            var environmentPath = Path.Combine(output, ".env");
            var environmentBytes = File.ReadAllBytes(environmentPath);
            var environmentText = File.ReadAllText(environmentPath);
            Assert.False(environmentBytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }));
            Assert.DoesNotContain("\r\n", environmentText);

            var loaded = BuilderConfigLoader.Load(output);
            Assert.Equal(specialValue, loaded.FirebasePrivateKey);
        }
        finally
        {
            Directory.Delete(temporaryRoot, recursive: true);
        }
    }

    [Fact]
    public void UnquotedComposeDotenvValuesLoadAndAreRewrittenCanonically()
    {
        const string shellVariable = "BARKFLUFF_BUILDER_LOADER_TEST_VALUE";
        const string missingVariable = "BARKFLUFF_BUILDER_LOADER_TEST_MISSING";
        var priorShellValue = Environment.GetEnvironmentVariable(shellVariable);
        var priorMissingValue = Environment.GetEnvironmentVariable(missingVariable);
        Environment.SetEnvironmentVariable(shellVariable, "from-shell");
        Environment.SetEnvironmentVariable(missingVariable, null);
        var temporaryRoot = CreateTemporaryDirectory();
        try
        {
            var output = Path.Combine(temporaryRoot, "node");
            var model = CreateModel(output, "Nightly", NginxMode.None, false, false, true);
            BuilderOutputWriter.Write(model);

            var environmentPath = Path.Combine(output, ".env");
            var lines = File.ReadAllLines(environmentPath).Select(line => line switch
            {
                var value when value.StartsWith("LOGNARA_RELAY_CORE_TOKEN=", StringComparison.Ordinal) => "LOGNARA_RELAY_CORE_TOKEN=manual-core-token # pasted from Core",
                var value when value.StartsWith("BEACON_LOGNARA_RELAY_TOKEN=", StringComparison.Ordinal) => "BEACON_LOGNARA_RELAY_TOKEN=manual-beacon-token # pasted from Core",
                var value when value.StartsWith("POSTGRES_PASSWORD=", StringComparison.Ordinal) => "POSTGRES_PASSWORD=p#ss$$x # comment",
                var value when value.StartsWith("RABBITMQ_DEFAULT_PASS=", StringComparison.Ordinal) => "RABBITMQ_DEFAULT_PASS=  rabbit#pass$$word # pasted from Core",
                var value when value.StartsWith("SEQ_API_KEY=", StringComparison.Ordinal) => "SEQ_API_KEY=  padded value  ",
                var value when value.StartsWith("SEQ_ADMIN_PASSWORD=", StringComparison.Ordinal) => $"SEQ_ADMIN_PASSWORD=${shellVariable}",
                var value when value.StartsWith("MINIO_ROOT_PASSWORD=", StringComparison.Ordinal) => "MINIO_ROOT_PASSWORD= # comment",
                var value when value.StartsWith("S3_ENDPOINT=", StringComparison.Ordinal) => "S3_ENDPOINT=${" + missingVariable + ":-https://fallback.example.com}",
                var value when value.StartsWith("LOGNARA_ENVIRONMENT=", StringComparison.Ordinal) => "LOGNARA_ENVIRONMENT=",
                _ => line
            }).ToList();
            lines.Add("BUILDER_LOGNARA_APPLICATION_BEACON_SPOOL_PATH=\"./legacy/spool\"");
            lines.Add("BEACON_LOGNARA_SPOOL_PATH=\"./legacy/spool\"");
            File.WriteAllLines(environmentPath, lines);

            var loaded = BuilderConfigLoader.Load(output);
            Assert.Equal("manual-core-token", loaded.LognaraCoreToken);
            Assert.Equal("manual-beacon-token", loaded.Application("BEACON").RelayToken);
            Assert.Equal("p#ss$x", loaded.PostgresPassword);
            Assert.Equal("rabbit#pass$word", loaded.RabbitPassword);
            Assert.Equal("padded value", loaded.SeqApiKey);
            Assert.Equal("from-shell", loaded.SeqAdminPassword);
            Assert.Equal("# comment", loaded.MinioRootPassword);
            Assert.Equal("https://fallback.example.com", loaded.S3Endpoint);
            Assert.Equal(string.Empty, loaded.LognaraEnvironment);

            BuilderOutputWriter.Write(loaded);
            var canonical = File.ReadAllText(environmentPath);
            Assert.Contains("LOGNARA_RELAY_CORE_TOKEN=\"manual-core-token\"", canonical);
            Assert.Contains("BEACON_LOGNARA_RELAY_TOKEN=\"manual-beacon-token\"", canonical);
            Assert.Contains("POSTGRES_PASSWORD=\"p#ss\\$x\"", canonical);
            Assert.DoesNotContain("SPOOL_PATH", canonical);
        }
        finally
        {
            Environment.SetEnvironmentVariable(shellVariable, priorShellValue);
            Environment.SetEnvironmentVariable(missingVariable, priorMissingValue);
            Directory.Delete(temporaryRoot, recursive: true);
        }
    }

    [Fact]
    public void MalformedLiveKitConfigHasReadableError()
    {
        var temporaryRoot = CreateTemporaryDirectory();
        try
        {
            var output = Path.Combine(temporaryRoot, "node");
            BuilderOutputWriter.Write(CreateModel(output, "Nightly", NginxMode.None, false, false, false));
            File.WriteAllText(Path.Combine(output, "livekit", "livekit.yaml"), "keys: {}\n");

            var error = Assert.Throws<InvalidDataException>(() => BuilderConfigLoader.Load(output));
            Assert.Contains("livekit/livekit.yaml", error.Message);
        }
        finally
        {
            Directory.Delete(temporaryRoot, recursive: true);
        }
    }

    [Fact]
    public void MissingNginxAndLognaraBuildFilesDoNotPreventLoading()
    {
        var temporaryRoot = CreateTemporaryDirectory();
        try
        {
            var output = Path.Combine(temporaryRoot, "node");
            var model = CreateModel(output, "Nightly", NginxMode.Compose, true, true, true);
            model.NginxCertificateSourcePath = Path.Combine(temporaryRoot, "source-certificate.pem");
            model.NginxKeySourcePath = Path.Combine(temporaryRoot, "source-key.pem");
            File.WriteAllText(model.NginxCertificateSourcePath, "synthetic certificate");
            File.WriteAllText(model.NginxKeySourcePath, "synthetic key");
            BuilderOutputWriter.Write(model);

            Directory.Delete(Path.Combine(output, "nginx"), recursive: true);
            File.Delete(Path.Combine(output, "lognara", "rabbitmq.Dockerfile"));

            var loaded = BuilderConfigLoader.Load(output);
            Assert.Equal(NginxMode.Compose, loaded.NginxMode);
            Assert.True(loaded.IncludeLognara);
        }
        finally
        {
            Directory.Delete(temporaryRoot, recursive: true);
        }
    }

    [Fact]
    public void MalformedEnvironmentFileReportsItsLine()
    {
        var temporaryRoot = CreateTemporaryDirectory();
        try
        {
            var output = Path.Combine(temporaryRoot, "node");
            BuilderOutputWriter.Write(CreateModel(output, "Nightly", NginxMode.None, false, false, false));
            File.WriteAllText(Path.Combine(output, ".env"), "BROKEN=\"unclosed\n");

            var error = Assert.Throws<FormatException>(() => BuilderConfigLoader.Load(output));
            Assert.Contains(".env", error.Message);
            Assert.Contains("строка 1", error.Message);
        }
        finally
        {
            Directory.Delete(temporaryRoot, recursive: true);
        }
    }

    private static BuilderModel CreateModel(
        string output,
        string channel,
        NginxMode nginx,
        bool includeSeq,
        bool includeMinio,
        bool includeLognara)
    {
        var model = BuilderTestSupport.ValidModel();
        model.OutputPath = output;
        model.ImageChannel = channel;
        model.DockerNetworkName = "node-test-1";
        model.AspNetCoreEnvironment = "Staging";
        model.PublicDomain = "node.example.com";
        model.NavigatorUrl = "https://nav.example.com";
        model.PostgresUser = "postgres-user";
        model.PostgresPassword = "postgres-$#\\password";
        model.PostgresHostPort = "5442";
        model.RabbitUser = "rabbit-user";
        model.RabbitPassword = "rabbit-$\"#\\password";
        model.IncludeSeq = includeSeq;
        model.SeqAdminPassword = "seq-$\"#\\password";
        model.SeqHostPort = "8181";
        model.SeqApiKey = "seq-api-$\"#\\key";
        model.IncludeMinio = includeMinio;
        model.MinioRootUser = "minio-user";
        model.MinioRootPassword = "minio-$\"#\\password";
        model.MinioApiHostPort = "9100";
        model.MinioConsoleHostPort = "9101";
        model.S3Endpoint = "https://objects.example.com:9443";
        model.FirebaseProjectId = "firebase-project";
        model.FirebasePrivateKeyId = "firebase-key-id";
        model.FirebasePrivateKey = "-----BEGIN PRIVATE KEY-----\nline with $ and \"quotes\" # hash and \\slash\n-----END PRIVATE KEY-----\n";
        model.FirebaseClientEmail = "firebase@example.com";
        model.FirebaseClientId = "firebase-client-id";
        model.WebPushVapidKey = "vapid-$\"#\\key";
        model.WebPushFirebaseApiKey = "push-api-key";
        model.WebPushFirebaseAuthDomain = "push.example.com";
        model.WebPushFirebaseProjectId = "push-project";
        model.WebPushFirebaseStorageBucket = "push-bucket";
        model.WebPushFirebaseMessagingSenderId = "123456789";
        model.WebPushFirebaseAppId = "push-app";
        model.TelegramBotToken = "123456:$bot-\"#\\token";
        model.TelegramOwnerId = "123456789:owner";
        model.TelegramProxyUrl = "socks5://proxy.example.com:1080";
        model.TelegramProxyUsername = "proxy-$\"#\\user";
        model.TelegramProxyPassword = "proxy-$\"#\\password";
        model.SmtpHost = "smtp.example.com";
        model.SmtpPort = "2525";
        model.SmtpSecurity = "StartTls";
        model.ImapHost = "imap.example.com";
        model.ImapPort = "993";
        model.ImapSecurity = "SslOnConnect";
        model.MailAcceptInvalidCertificates = true;
        model.MailSupportPassword = "support-$\"#\\password";
        model.MailHelpPassword = "help-$\"#\\password";
        model.MailNoreplyPassword = "noreply-$\"#\\password";
        model.MailPrivacyPassword = "privacy-$\"#\\password";
        model.MailSecurityPassword = "security-$\"#\\password";
        model.LiveKitApiKey = "LK$\"#\\key";
        model.LiveKitApiSecret = "livekit-$\"#\\secret";
        model.SetupHostPort = "7033";
        model.SetupPublicOrigin = "https://setup.example.com";
        model.SetupToken = "setup-$\"#\\token";
        model.NginxMode = nginx;
        model.NginxCertificatePath = "/etc/nginx/certs/node.pem";
        model.NginxKeyPath = "/etc/nginx/certs/node.key";
        model.BehindCloudflare = true;
        model.IncludeLognara = includeLognara;
        model.LognaraCoreUrl = "https://lognara.example.com/v1/batches";
        model.LognaraCoreToken = "lognara-core-$\"#\\token";
        model.LognaraServer = "builder-test-node";
        model.LognaraBackend = "custom-backend";
        model.LognaraEnvironment = "testing";

        var targetIndex = 0;
        foreach (var target in model.AllLognaraTargets())
        {
            target.ConsoleOutput = targetIndex % 2 == 0;
            target.SeqEnable = targetIndex % 3 == 0;
            target.LognaraEnable = false;
            target.RelayToken = $"relay-{target.Prefix}-$\"#\\token";
            targetIndex++;
        }
        model.Application("BEACON").LognaraEnable = true;
        model.Infrastructure("RABBITMQ").LognaraEnable = true;

        return model;
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "barkfluff-builder-loader-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
