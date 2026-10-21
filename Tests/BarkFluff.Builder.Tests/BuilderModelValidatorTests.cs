using BarkFluff.Builder;
using Xunit;

namespace BarkFluff.Builder.Tests;

public sealed class BuilderModelValidatorTests
{
    [Fact]
    public void CompleteSyntheticConfigurationIsValid()
    {
        var errors = BuilderModelValidator.Validate(BuilderTestSupport.ValidModel());

        Assert.Empty(errors);
    }

    [Fact]
    public void RequiredSecretsTelegramOwnerAndUrlsAreValidated()
    {
        var model = BuilderTestSupport.ValidModel();
        model.PostgresPassword = "";
        model.RabbitPassword = "";
        model.SetupToken = "";
        model.LiveKitApiKey = "";
        model.LiveKitApiSecret = "";
        model.TelegramBotToken = "";
        model.TelegramOwnerId = "123:one,456:two";
        model.PublicDomain = "bad domain";
        model.NavigatorUrl = "not-a-url";

        var errors = BuilderModelValidator.Validate(model);

        Assert.Contains(errors, error => error.Contains("PostgreSQL", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("RabbitMQ", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("setup", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(errors, error => error.Contains("LiveKit", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("Telegram bot", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("ровно одного владельца", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("DNS-именем", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("Navigator", StringComparison.Ordinal));
    }

    [Fact]
    public void HostPortConflictsAreProtocolAware()
    {
        var model = BuilderTestSupport.ValidModel();
        model.PostgresHostPort = "7032";
        model.MinioApiHostPort = "50000";
        var errors = BuilderModelValidator.Validate(model);

        Assert.Contains(errors, error => error.Contains("Setup", StringComparison.Ordinal)
            && error.Contains("PostgreSQL", StringComparison.Ordinal));
        Assert.DoesNotContain(errors, error => error.Contains("LiveKit UDP", StringComparison.Ordinal));
    }

    [Fact]
    public void MailPortsAreNotHostPortReservationsAndNginxReservesHttpPorts()
    {
        var model = BuilderTestSupport.ValidModel();
        model.SetupHostPort = "587";
        model.SmtpPort = "587";
        Assert.Empty(BuilderModelValidator.Validate(model));

        model.NginxMode = NginxMode.ExternalContainer;
        model.SetupHostPort = "80";
        Assert.Contains(BuilderModelValidator.Validate(model), error => error.Contains("nginx HTTP", StringComparison.Ordinal));
    }

    [Fact]
    public void ActiveLognaraAgentsNeedUniqueTokensAndUseNamedSpoolVolumes()
    {
        var model = BuilderTestSupport.ValidModel();
        model.IncludeLognara = true;
        model.LognaraCoreUrl = "https://core.example.com/v1/batches";
        model.LognaraCoreToken = "LOGNARA_AGENT_TOKEN=synthetic-core-token";
        model.LognaraServer = "node-1";
        var application = model.Application("SETTINGS");
        var postgres = model.Infrastructure("POSTGRES");
        application.LognaraEnable = true;
        postgres.LognaraEnable = true;
        application.RelayToken = "LOGNARA_AGENT_TOKEN=synthetic-relay-token";
        postgres.RelayToken = "synthetic-relay-token";

        var duplicateTokenErrors = BuilderModelValidator.Validate(model);
        Assert.Contains(duplicateTokenErrors, error => error.Contains("уникальными", StringComparison.Ordinal));

        postgres.RelayToken = "another-synthetic-relay-token";
        Assert.Empty(BuilderModelValidator.Validate(model));
        Assert.Contains("SETTINGS_LOGNARA_RELAY_TOKEN=\"synthetic-relay-token\"", ComposeGenerator.BuildEnv(model));
        Assert.DoesNotContain("SPOOL_PATH", ComposeGenerator.BuildEnv(model));
        Assert.Contains("lognara-agent-spool-settings:/var/lib/lognara-agent/spool", BuilderTestSupport.Block(ComposeGenerator.BuildCompose(model), "settings"));

        model.LognaraCoreToken = "LOGNARA_AGENT_TOKEN=";
        Assert.Contains(BuilderModelValidator.Validate(model), error => error.Contains("Core", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsValuesThatCannotBeWrittenToDotenv()
    {
        var model = BuilderTestSupport.ValidModel();
        model.TelegramBotToken = "synthetic\0token";

        Assert.Contains(BuilderModelValidator.Validate(model), error => error.Contains(".env", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("bad;user")]
    [InlineData("bad=user")]
    [InlineData("bad user")]
    [InlineData("bad\"user")]
    [InlineData("bad'user")]
    public void RejectsPostgresConnectionStringDelimiters(string value)
    {
        var userModel = BuilderTestSupport.ValidModel();
        userModel.PostgresUser = value;
        Assert.Contains(BuilderModelValidator.Validate(userModel), error => error.Contains("Пользователь PostgreSQL", StringComparison.Ordinal));

        var passwordModel = BuilderTestSupport.ValidModel();
        passwordModel.PostgresPassword = value;
        Assert.Contains(BuilderModelValidator.Validate(passwordModel), error => error.Contains("Пароль PostgreSQL", StringComparison.Ordinal));
    }

    [Fact]
    public void RabbitCredentialsRemainSeparateFromPostgresConnectionStringRules()
    {
        var model = BuilderTestSupport.ValidModel();
        model.RabbitUser = "rabbit user";
        model.RabbitPassword = "rabbit;=password";

        Assert.DoesNotContain(BuilderModelValidator.Validate(model), error => error.Contains("RabbitMQ", StringComparison.Ordinal));
    }

    [Fact]
    public void IDNHostsMustBeEnteredAsPunycode()
    {
        var model = BuilderTestSupport.ValidModel();
        model.PublicDomain = "пример.рф";
        Assert.Contains(BuilderModelValidator.Validate(model), error => error.Contains("punycode", StringComparison.OrdinalIgnoreCase));

        model.PublicDomain = "xn--e1afmkfd.xn--p1ai";
        model.NavigatorUrl = "https://пример.рф";
        Assert.Contains(BuilderModelValidator.Validate(model), error => error.Contains("punycode", StringComparison.OrdinalIgnoreCase));

        model.NavigatorUrl = "https://nav.example.com";
        model.IncludeMinio = false;
        model.S3Endpoint = "https://пример.рф";
        Assert.Contains(BuilderModelValidator.Validate(model), error => error.Contains("punycode", StringComparison.OrdinalIgnoreCase));

        model.S3Endpoint = "https://storage.example.com";
        model.SetupPublicOrigin = "https://пример.рф";
        Assert.Contains(BuilderModelValidator.Validate(model), error => error.Contains("punycode", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("\t")]
    [InlineData("\r")]
    [InlineData("\n")]
    public void RejectsTabsAndControlCharactersExceptFirebaseNewlines(string control)
    {
        var model = BuilderTestSupport.ValidModel();
        model.TelegramBotToken = $"synthetic{control}token";
        Assert.Contains(BuilderModelValidator.Validate(model), error => error.Contains(".env", StringComparison.Ordinal));

        model.TelegramBotToken = "synthetic-token";
        model.FirebasePrivateKey = $"line-one{control}line-two";
        var errors = BuilderModelValidator.Validate(model);
        if (control == "\n")
            Assert.DoesNotContain(errors, error => error.Contains("FirebasePrivateKey", StringComparison.Ordinal));
        else
            Assert.Contains(errors, error => error.Contains("FirebasePrivateKey", StringComparison.Ordinal));
    }
}
