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
    public void ActiveLognaraAgentsNeedUniqueNormalizedTokensAndSpoolPaths()
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
        postgres.SpoolPath = application.SpoolPath;
        var duplicatePathErrors = BuilderModelValidator.Validate(model);
        Assert.Contains(duplicatePathErrors, error => error.Contains("Пути spool", StringComparison.Ordinal));

        postgres.SpoolPath = "./data/lognara/spool/postgres";
        Assert.Empty(BuilderModelValidator.Validate(model));
        Assert.Contains("SETTINGS_LOGNARA_RELAY_TOKEN=\"synthetic-relay-token\"", ComposeGenerator.BuildEnv(model));
        Assert.Contains("BUILDER_LOGNARA_APPLICATION_SETTINGS_SPOOL_PATH=\"./data/lognara/spool/settings\"", ComposeGenerator.BuildEnv(model));

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
}
