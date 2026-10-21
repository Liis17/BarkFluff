using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace BarkFluff.Builder;

public static class ComposeGenerator
{
    private const string Network = "node";
    private const string LognaraRelayVolume = "lognara-relay-spool";

    public static string BuildCompose(BuilderModel model)
    {
        var suffix = ImageSuffix(model.ImageChannel);
        var services = new List<ComposeService>();

        services.Add(App(model, "beacon", AppImage("beacon", suffix), ["settings"]));
        services.Add(ComposeService.New("settings", AppImage("settings", suffix))
            .WithEnvironment(
                ("SETTINGS_HOST", "postgres"),
                ("SETTINGS_DBPORT", "5432"),
                ("SETTINGS_DATABASE", "settings"),
                ("SETTINGS_USERNAME", "${POSTGRES_USER}"),
                ("SETTINGS_PASSWORD", "${POSTGRES_PASSWORD:?set POSTGRES_PASSWORD in .env}"),
                ("SETTINGS_PORT", "7003"),
                ("SETTINGS_SETUP_MODE", "false"),
                ("RABBITMQ_DEFAULT_USER", "${RABBITMQ_DEFAULT_USER:?set RABBITMQ_DEFAULT_USER in .env}"),
                ("RABBITMQ_DEFAULT_PASS", "${RABBITMQ_DEFAULT_PASS:?set RABBITMQ_DEFAULT_PASS in .env}"),
                ("NODE_PUBLIC_DOMAIN", "${NODE_PUBLIC_DOMAIN}"),
                ("NODE_NAVIGATOR_URL", "${NODE_NAVIGATOR_URL}"),
                ("NODE_S3_ENDPOINT", "${NODE_S3_ENDPOINT}"))
            .WithDependencies(("postgres", "service_healthy"), ("postgres-bootstrap", "service_completed_successfully"))
            .WithAgent(model, model.Application("SETTINGS")));

        services.Add(App(model, "files", AppImage("files", suffix), ["settings"]));
        services.Add(App(model, "identity", AppImage("identity", suffix), ["settings"]));
        services.Add(App(model, "messages", AppImage("messages", suffix), ["settings"]));
        services.Add(App(model, "notification", AppImage("notification", suffix), ["settings"]));
        services.Add(App(model, "users", AppImage("users", suffix), ["settings"]));
        services.Add(App(model, "fast-auth", AppImage("fast-auth", suffix), ["settings"]));
        services.Add(App(model, "updates", AppImage("updates", suffix), ["settings"]));
        services.Add(App(model, "onliner", AppImage("onliner", suffix), ["settings"]));
        services.Add(App(model, "federation", AppImage("federation", suffix), ["settings"]));

        services.Add(App(model, "cloud-messaging", AppImage("cloud-messaging", suffix), ["settings"])
            .WithEnvironment(
                ("Firebase__ProjectId", "${FIREBASE_PROJECT_ID}"),
                ("Firebase__PrivateKeyId", "${FIREBASE_PRIVATE_KEY_ID}"),
                ("Firebase__PrivateKey", "${FIREBASE_PRIVATE_KEY}"),
                ("Firebase__ClientEmail", "${FIREBASE_CLIENT_EMAIL}"),
                ("Firebase__ClientId", "${FIREBASE_CLIENT_ID}")));

        services.Add(App(model, "web", AppImage("web", suffix),
                ["settings", "identity", "users", "messages", "files", "updates", "onliner"])
            .WithEnvironment(
                ("MessagesService__Host", "http://messages:7007"),
                ("UsersService__Host", "http://users:7001"),
                ("FilesService__Host", "http://files:7005"),
                ("UpdatesService__Host", "http://updates:7015"),
                ("OnlinerService__Host", "http://onliner:7009"),
                ("Web__Push__VapidKey", "${WEB_PUSH_VAPID_KEY}"),
                ("Web__Push__Firebase__ApiKey", "${WEB_PUSH_FIREBASE_API_KEY}"),
                ("Web__Push__Firebase__AuthDomain", "${WEB_PUSH_FIREBASE_AUTH_DOMAIN}"),
                ("Web__Push__Firebase__ProjectId", "${WEB_PUSH_FIREBASE_PROJECT_ID}"),
                ("Web__Push__Firebase__StorageBucket", "${WEB_PUSH_FIREBASE_STORAGE_BUCKET}"),
                ("Web__Push__Firebase__MessagingSenderId", "${WEB_PUSH_FIREBASE_MESSAGING_SENDER_ID}"),
                ("Web__Push__Firebase__AppId", "${WEB_PUSH_FIREBASE_APP_ID}")));

        services.Add(App(model, "developers", AppImage("developers", suffix), ["settings"]));
        services.Add(App(model, "calls", AppImage("calls", suffix), ["settings"]));
        services.Add(App(model, "bots", AppImage("bots", suffix), ["settings", "users", "messages", "files"]));

        var admin = App(model, "admin-panel", AppImage("admin-panel", suffix), ["settings", "messages"])
            .WithProperty("user", "root")
            .WithEnvironment(
                ("SettingsService__Host", "http://settings:7003"),
                ("Telegram__BotToken", "${TELEGRAM_BOT_TOKEN}"),
                ("Telegram__Admins", "${TELEGRAM_ADMINS}"),
                ("Telegram__Proxy__Url", "${TELEGRAM_PROXY_URL}"),
                ("Telegram__Proxy__Username", "${TELEGRAM_PROXY_USERNAME}"),
                ("Telegram__Proxy__Password", "${TELEGRAM_PROXY_PASSWORD}"),
                ("UsersService__Host", "http://users:7001"),
                ("FilesService__Host", "http://files:7005"),
                ("MessagesService__Host", "http://messages:7007"),
                ("IdentityService__Host", "http://identity:7000"),
                ("BotsService__Host", "http://bots:7027"),
                ("Mail__SmtpHost", "${MAIL_SMTP_HOST}"),
                ("Mail__SmtpPort", "${MAIL_SMTP_PORT}"),
                ("Mail__ImapHost", "${MAIL_IMAP_HOST}"),
                ("Mail__ImapPort", "${MAIL_IMAP_PORT}"),
                ("Mail__ImapSecurity", "${MAIL_IMAP_SECURITY}"),
                ("Mail__SmtpSecurity", "${MAIL_SMTP_SECURITY}"),
                ("Mail__AcceptInvalidCertificates", "${MAIL_ACCEPT_INVALID_CERTIFICATES}"),
                ("Mail__Accounts__0__Address", "support@barkfluff.com"),
                ("Mail__Accounts__0__Password", "${MAIL_SUPPORT_PASSWORD}"),
                ("Mail__Accounts__1__Address", "help@barkfluff.com"),
                ("Mail__Accounts__1__Password", "${MAIL_HELP_PASSWORD}"),
                ("Mail__Accounts__2__Address", "noreply@barkfluff.com"),
                ("Mail__Accounts__2__Password", "${MAIL_NOREPLY_PASSWORD}"),
                ("Mail__Accounts__3__Address", "privacy@barkfluff.com"),
                ("Mail__Accounts__3__Password", "${MAIL_PRIVACY_PASSWORD}"),
                ("Mail__Accounts__4__Address", "security@barkfluff.com"),
                ("Mail__Accounts__4__Password", "${MAIL_SECURITY_PASSWORD}"),
                ("Seq__Enabled", model.IncludeSeq ? "true" : "false"),
                ("Seq__ServerUrl", model.IncludeSeq ? "http://seq:80" : ""),
                ("Seq__ApiKey", "${SEQ_API_KEY}"))
            .WithVolumes(
                "/var/run/docker.sock:/var/run/docker.sock",
                "./admindata:/app/db",
                "./docker-compose.yml:/docker-compose.yml:rw",
                "./.env:/.env:ro");
        if (model.IncludeSeq)
            admin = admin.WithDependency("seq");
        services.Add(admin);

        if (model.IncludeSeq)
        {
            var seq = ComposeService.New("seq", "datalust/seq:latest")
                .WithPorts($"127.0.0.1:${{SEQ_HOST_PORT}}:80")
                .WithEnvironment(("ACCEPT_EULA", "Y"), ("SEQ_FIRSTRUN_ADMINPASSWORD", "${SEQ_ADMIN_PASSWORD}"))
                .WithVolumes("./data/seq:/data");
            services.Add(WithInfrastructureAgent(model, seq, "SEQ", "datalust/seq:latest", "/bin/seqentry", "[]"));
        }

        var rabbit = ComposeService.New("rabbitmq", "rabbitmq:latest")
            .WithVolumes("./data/rabbitmq:/var/lib/rabbitmq")
            .WithEnvironment(
                ("RABBITMQ_DEFAULT_USER", "${RABBITMQ_DEFAULT_USER}"),
                ("RABBITMQ_DEFAULT_PASS", "${RABBITMQ_DEFAULT_PASS}"));
        services.Add(WithInfrastructureAgent(model, rabbit, "RABBITMQ", "rabbitmq:latest", "/usr/local/bin/docker-entrypoint.sh", "[\"rabbitmq-server\"]"));

        var redis = ComposeService.New("redis", "redis:latest")
            .WithCommand(ComposeArray("redis-server", "--appendonly", "yes"))
            .WithVolumes("./data/redis:/data");
        services.Add(WithInfrastructureAgent(model, redis, "REDIS", "redis:latest", "/usr/local/bin/docker-entrypoint.sh", "redis-server --appendonly yes"));

        var livekit = ComposeService.New("livekit", "livekit/livekit-server:latest")
            .WithCommand(ComposeArray("--config", "/etc/livekit/livekit.yaml"))
            .WithVolumes("./livekit:/etc/livekit:ro")
            .WithPorts(
                model.NginxMode == NginxMode.Host ? "127.0.0.1:7880:7880" : "7880:7880",
                "7881:7881",
                "50000-50200:50000-50200/udp");
        services.Add(WithInfrastructureAgent(model, livekit, "LIVEKIT", "livekit/livekit-server:latest", "/livekit-server", "--config /etc/livekit/livekit.yaml"));

        var postgres = ComposeService.New("postgres", "postgres:18")
            .WithEnvironment(
                ("POSTGRES_USER", "${POSTGRES_USER}"),
                ("POSTGRES_PASSWORD", "${POSTGRES_PASSWORD}"),
                ("POSTGRES_DB", "postgres"),
                ("PGDATA", "/var/lib/postgresql/data/pgdata"))
            .WithVolumes("./data/postgres:/var/lib/postgresql", "./backups:/backup")
            .WithCommand(PostgresCommand())
            .WithPorts(HostPort(model.PostgresHostPort, "POSTGRES_HOST_PORT", "5432"))
            .WithHealthcheck("[\"CMD-SHELL\", \"pg_isready -U $$POSTGRES_USER -d postgres\"]");
        services.Add(WithInfrastructureAgent(model, postgres, "POSTGRES", "postgres:18", "/usr/local/bin/docker-entrypoint.sh", PostgresCommand()));

        var bootstrap = ComposeService.New("postgres-bootstrap", "postgres:18")
            .WithProperty("restart", "\"no\"")
            .WithProperty("entrypoint", "[\"/bin/sh\", \"-ec\"]")
            .WithCommand(BootstrapCommand())
            .WithEnvironment(
                ("PGHOST", "postgres"),
                ("PGPORT", "5432"),
                ("PGUSER", "${POSTGRES_USER}"),
                ("PGPASSWORD", "${POSTGRES_PASSWORD}"),
                ("SETTINGS_DATABASE", "settings"))
            .WithDependencies(("postgres", "service_healthy"));
        services.Add(WithInfrastructureAgent(model, bootstrap, "POSTGRES_BOOTSTRAP", "postgres:18", "/bin/sh", "-ec"));

        if (model.IncludeMinio)
        {
            const string baseImage = "quay.io/minio/minio:RELEASE.2025-04-22T22-12-26Z-cpuv1";
            var minio = ComposeService.New("minio", baseImage)
                .WithEnvironment(
                    ("MINIO_ROOT_USER", "${MINIO_ROOT_USER}"),
                    ("MINIO_ROOT_PASSWORD", "${MINIO_ROOT_PASSWORD}"))
                .WithVolumes("./data/minio:/data")
                .WithCommand(ComposeArray("server", "/data", "--console-address", ":9001"))
                .WithPorts("127.0.0.1:${MINIO_API_HOST_PORT}:9000", "127.0.0.1:${MINIO_CONSOLE_HOST_PORT}:9001");
            services.Add(WithInfrastructureAgent(model, minio, "MINIO", baseImage, "/usr/bin/docker-entrypoint.sh", "server /data --console-address \":9001\""));
        }

        if (model.NginxMode == NginxMode.Compose)
            services.Add(CreateNginxService(model));

        if (model.IncludeLognara)
            services.Add(LognaraRelay(model));

        return RenderCompose(services);
    }

    public static string BuildSetupCompose(BuilderModel model)
    {
        var suffix = ImageSuffix(model.ImageChannel);
        var setup = ComposeService.New("setup", AppImage("setup", suffix))
            .WithProperty("restart", "unless-stopped")
            .WithPorts($"127.0.0.1:${{SETUP_HOST_PORT}}:7032")
            .WithEnvironment(
                ("SETUP_PORT", "7032"),
                ("SETTINGS_URL", "http://settings:7003"),
                ("SETUP_PUBLIC_ORIGIN", "${SETUP_PUBLIC_ORIGIN}"),
                ("SETUP_SECRET_FILE", "/run/secrets/setup_token"))
            .WithSecret("setup_token")
            .WithDependencies(("settings", "service_started"))
            .WithAgent(model, model.Application("SETUP"));

        var settings = ComposeService.New("settings", AppImage("settings", suffix))
            .WithProperty("restart", "unless-stopped")
            .WithEnvironment(
                ("SETTINGS_HOST", "postgres"),
                ("SETTINGS_DBPORT", "5432"),
                ("SETTINGS_DATABASE", "settings"),
                ("SETTINGS_USERNAME", "${POSTGRES_USER}"),
                ("SETTINGS_PASSWORD", "${POSTGRES_PASSWORD:?set POSTGRES_PASSWORD in .env}"),
                ("SETTINGS_PORT", "7003"),
                ("SETTINGS_SETUP_MODE", "true"),
                ("SETTINGS_SETUP_SECRET_FILE", "/run/secrets/setup_token"),
                ("RABBITMQ_DEFAULT_USER", "${RABBITMQ_DEFAULT_USER:?set RABBITMQ_DEFAULT_USER in .env}"),
                ("RABBITMQ_DEFAULT_PASS", "${RABBITMQ_DEFAULT_PASS:?set RABBITMQ_DEFAULT_PASS in .env}"),
                ("NODE_PUBLIC_DOMAIN", "${NODE_PUBLIC_DOMAIN}"),
                ("NODE_NAVIGATOR_URL", "${NODE_NAVIGATOR_URL}"),
                ("NODE_S3_ENDPOINT", "${NODE_S3_ENDPOINT}"))
            .WithSecret("setup_token")
            .WithDependencies(("postgres", "service_healthy"), ("postgres-bootstrap", "service_completed_successfully"))
            .WithAgent(model, model.Application("SETUP_SETTINGS"));

        var postgres = ComposeService.New("postgres", "postgres:18")
            .WithProperty("restart", "unless-stopped")
            .WithEnvironment(
                ("POSTGRES_USER", "${POSTGRES_USER}"),
                ("POSTGRES_PASSWORD", "${POSTGRES_PASSWORD:?set POSTGRES_PASSWORD in .env}"),
                ("POSTGRES_DB", "postgres"),
                ("PGDATA", "/var/lib/postgresql/data/pgdata"))
            .WithVolumes("./data/postgres:/var/lib/postgresql", "./backups:/backup")
            .WithPorts(HostPort(model.PostgresHostPort, "POSTGRES_HOST_PORT", "5432"))
            .WithHealthcheck("[\"CMD-SHELL\", \"pg_isready -U $$POSTGRES_USER -d postgres\"]");
        postgres = WithInfrastructureAgent(model, postgres, "SETUP_POSTGRES", "postgres:18", "/usr/local/bin/docker-entrypoint.sh", "postgres");

        var bootstrap = ComposeService.New("postgres-bootstrap", "postgres:18")
            .WithProperty("restart", "\"no\"")
            .WithProperty("entrypoint", "[\"/bin/sh\", \"-ec\"]")
            .WithCommand(BootstrapCommand())
            .WithEnvironment(
                ("PGHOST", "postgres"),
                ("PGPORT", "5432"),
                ("PGUSER", "${POSTGRES_USER}"),
                ("PGPASSWORD", "${POSTGRES_PASSWORD:?set POSTGRES_PASSWORD in .env}"),
                ("SETTINGS_DATABASE", "${SETTINGS_DATABASE:-settings}"))
            .WithDependencies(("postgres", "service_healthy"));
        bootstrap = WithInfrastructureAgent(model, bootstrap, "SETUP_POSTGRES_BOOTSTRAP", "postgres:18", "/bin/sh", "-ec");

        var services = new List<ComposeService> { setup, settings, postgres, bootstrap };
        if (model.IncludeSeq)
        {
            var seq = ComposeService.New("seq", "datalust/seq:latest")
                .WithPorts("127.0.0.1:${SEQ_HOST_PORT}:80")
                .WithEnvironment(("ACCEPT_EULA", "Y"), ("SEQ_FIRSTRUN_ADMINPASSWORD", "${SEQ_ADMIN_PASSWORD}"))
                .WithVolumes("./data/seq:/data");
            services.Add(WithInfrastructureAgent(model, seq, "SEQ", "datalust/seq:latest", "/bin/seqentry", "[]"));
        }
        if (model.NginxMode == NginxMode.Compose)
            services.Add(CreateNginxService(model));
        if (model.IncludeLognara)
            services.Add(LognaraRelay(model));

        var b = new StringBuilder(RenderCompose(services));
        b.AppendLine("secrets:");
        b.AppendLine("  setup_token:");
        b.AppendLine("    file: ./secrets/setup_token");
        return b.ToString();
    }

    public static string BuildEnv(BuilderModel model)
    {
        var b = new StringBuilder("# Generated by BarkFluff Builder. Keep this file private.\n");
        void Section(string title) => b.Append("\n# ").Append(title).Append('\n');
        void Add(string key, string? value) => b.Append(key).Append("=\"").Append(EscapeEnv(value ?? "")).Append("\"\n");

        Section("Node and Docker");
        Add("DOCKER_NETWORK_NAME", model.DockerNetworkName);
        Add("ASPNETCORE_ENVIRONMENT", model.AspNetCoreEnvironment);
        Add("NODE_PUBLIC_DOMAIN", model.PublicDomain);
        Add("NODE_NAVIGATOR_URL", model.NavigatorUrl);
        Add("NODE_S3_ENDPOINT", model.IncludeMinio ? "http://minio:9000" : model.S3Endpoint);

        Section("PostgreSQL");
        Add("POSTGRES_USER", model.PostgresUser);
        Add("POSTGRES_PASSWORD", model.PostgresPassword);
        Add("POSTGRES_HOST_PORT", model.PostgresHostPort);

        Section("RabbitMQ");
        Add("RABBITMQ_DEFAULT_USER", model.RabbitUser);
        Add("RABBITMQ_DEFAULT_PASS", model.RabbitPassword);

        Section("Seq");
        Add("SEQ_ENABLED", Bool(model.IncludeSeq));
        Add("SEQ_ADMIN_PASSWORD", model.SeqAdminPassword);
        Add("SEQ_HOST_PORT", model.SeqHostPort);
        Add("SEQ_API_KEY", model.SeqApiKey);

        Section("MinIO and S3");
        Add("MINIO_ROOT_USER", model.MinioRootUser);
        Add("MINIO_ROOT_PASSWORD", model.MinioRootPassword);
        Add("MINIO_API_HOST_PORT", model.MinioApiHostPort);
        Add("MINIO_CONSOLE_HOST_PORT", model.MinioConsoleHostPort);
        Add("S3_ENDPOINT", model.S3Endpoint);

        Section("Firebase service credentials");
        Add("FIREBASE_PROJECT_ID", model.FirebaseProjectId);
        Add("FIREBASE_PRIVATE_KEY_ID", model.FirebasePrivateKeyId);
        Add("FIREBASE_PRIVATE_KEY", model.FirebasePrivateKey);
        Add("FIREBASE_CLIENT_EMAIL", model.FirebaseClientEmail);
        Add("FIREBASE_CLIENT_ID", model.FirebaseClientId);

        Section("Web push");
        Add("WEB_PUSH_VAPID_KEY", model.WebPushVapidKey);
        Add("WEB_PUSH_FIREBASE_API_KEY", model.WebPushFirebaseApiKey);
        Add("WEB_PUSH_FIREBASE_AUTH_DOMAIN", model.WebPushFirebaseAuthDomain);
        Add("WEB_PUSH_FIREBASE_PROJECT_ID", model.WebPushFirebaseProjectId);
        Add("WEB_PUSH_FIREBASE_STORAGE_BUCKET", model.WebPushFirebaseStorageBucket);
        Add("WEB_PUSH_FIREBASE_MESSAGING_SENDER_ID", model.WebPushFirebaseMessagingSenderId);
        Add("WEB_PUSH_FIREBASE_APP_ID", model.WebPushFirebaseAppId);

        Section("AdminPanel Telegram");
        Add("TELEGRAM_BOT_TOKEN", model.TelegramBotToken);
        Add("TELEGRAM_ADMINS", model.TelegramOwnerId);
        Add("TELEGRAM_PROXY_URL", model.TelegramProxyUrl);
        Add("TELEGRAM_PROXY_USERNAME", model.TelegramProxyUsername);
        Add("TELEGRAM_PROXY_PASSWORD", model.TelegramProxyPassword);

        Section("AdminPanel mail");
        Add("MAIL_SMTP_HOST", model.SmtpHost);
        Add("MAIL_SMTP_PORT", model.SmtpPort);
        Add("MAIL_SMTP_SECURITY", model.SmtpSecurity);
        Add("MAIL_IMAP_HOST", model.ImapHost);
        Add("MAIL_IMAP_PORT", model.ImapPort);
        Add("MAIL_IMAP_SECURITY", model.ImapSecurity);
        Add("MAIL_ACCEPT_INVALID_CERTIFICATES", Bool(model.MailAcceptInvalidCertificates));
        Add("MAIL_SUPPORT_PASSWORD", model.MailSupportPassword);
        Add("MAIL_HELP_PASSWORD", model.MailHelpPassword);
        Add("MAIL_NOREPLY_PASSWORD", model.MailNoreplyPassword);
        Add("MAIL_PRIVACY_PASSWORD", model.MailPrivacyPassword);
        Add("MAIL_SECURITY_PASSWORD", model.MailSecurityPassword);

        Section("LiveKit");
        Add("LIVEKIT_API_KEY", model.LiveKitApiKey);
        Add("LIVEKIT_API_SECRET", model.LiveKitApiSecret);

        Section("Setup bootstrap");
        Add("SETUP_HOST_PORT", model.SetupHostPort);
        Add("SETUP_PUBLIC_ORIGIN", model.SetupPublicOrigin);
        Add("SETUP_TOKEN", model.SetupToken);

        Section("Lognara relay");
        Add("LOGNARA_CORE_URL", model.LognaraCoreUrl);
        Add("LOGNARA_RELAY_CORE_TOKEN", LognaraAgentToken.Normalize(model.LognaraCoreToken));
        Add("LOGNARA_SERVER", model.LognaraServer);
        Add("LOGNARA_BACKEND", model.LognaraBackend);
        Add("LOGNARA_ENVIRONMENT", model.LognaraEnvironment);

        Section("Builder settings (non-secret; used when reopening this configuration)");
        Add("BUILDER_IMAGE_CHANNEL", model.ImageChannel);
        Add("BUILDER_INCLUDE_SEQ", Bool(model.IncludeSeq));
        Add("BUILDER_INCLUDE_MINIO", Bool(model.IncludeMinio));
        Add("BUILDER_INCLUDE_LOGNARA", Bool(model.IncludeLognara));
        Add("BUILDER_NGINX_MODE", model.NginxMode.ToString());
        Add("BUILDER_NGINX_BEHIND_CLOUDFLARE", Bool(model.BehindCloudflare));
        Add("BUILDER_NGINX_CERTIFICATE_SOURCE_PATH", PersistedCertificatePath(model, model.NginxCertificateSourcePath, "barkfluff-crt.pem"));
        Add("BUILDER_NGINX_KEY_SOURCE_PATH", PersistedCertificatePath(model, model.NginxKeySourcePath, "barkfluff-key.pem"));
        Add("BUILDER_NGINX_CERTIFICATE_PATH", model.NginxCertificatePath);
        Add("BUILDER_NGINX_KEY_PATH", model.NginxKeyPath);
        foreach (var target in model.AllLognaraTargets())
        {
            var kind = model.LognaraApplications.Contains(target) ? "APPLICATION" : "INFRASTRUCTURE";
            Add($"{target.Prefix}_CONSOLE_OUTPUT", Bool(target.ConsoleOutput));
            Add($"{target.Prefix}_SEQ_ENABLE", Bool(model.IncludeSeq && target.SeqEnable));
            Add($"{target.Prefix}_LOGNARA_ENABLE", Bool(model.IncludeLognara && target.LognaraEnable));
            Add($"BUILDER_LOGNARA_{kind}_{target.Prefix}_CONSOLE_OUTPUT", Bool(target.ConsoleOutput));
            Add($"BUILDER_LOGNARA_{kind}_{target.Prefix}_SEQ_ENABLE", Bool(target.SeqEnable));
            Add($"BUILDER_LOGNARA_{kind}_{target.Prefix}_LOGNARA_ENABLE", Bool(target.LognaraEnable));
            Add($"{target.RelayTokenVariable}", LognaraAgentToken.Normalize(target.RelayToken));
        }

        return b.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    public static string BuildLiveKitConfig(BuilderModel model) => $$"""
port: 7880

rtc:
  tcp_port: 7881
  port_range_start: 50000
  port_range_end: 50200
  use_external_ip: true

keys:
  {{YamlQuote(model.LiveKitApiKey)}}: {{YamlQuote(model.LiveKitApiSecret)}}

webhook:
  api_key: {{YamlQuote(model.LiveKitApiKey)}}
  urls:
    - http://calls:7026/livekit/webhook
""";

    public static IReadOnlyDictionary<string, string> BuildLognaraDockerfiles(BuilderModel model)
    {
        if (!model.IncludeLognara)
            return new Dictionary<string, string>();

        var definitions = new List<(string File, string Base)>() ;
        if (model.LognaraInfrastructure.Any(x => x.LognaraEnable && x.Prefix is "POSTGRES" or "POSTGRES_BOOTSTRAP" or "SETUP_POSTGRES" or "SETUP_POSTGRES_BOOTSTRAP"))
            definitions.Add(("postgres.Dockerfile", "postgres:18"));
        if (model.Infrastructure("RABBITMQ").LognaraEnable)
            definitions.Add(("rabbitmq.Dockerfile", "rabbitmq:latest"));
        if (model.Infrastructure("REDIS").LognaraEnable)
            definitions.Add(("redis.Dockerfile", "redis:latest"));
        if (model.Infrastructure("LIVEKIT").LognaraEnable)
            definitions.Add(("livekit.Dockerfile", "livekit/livekit-server:latest"));
        if (model.IncludeSeq && model.Infrastructure("SEQ").LognaraEnable)
            definitions.Add(("seq.Dockerfile", "datalust/seq:latest"));
        if (model.IncludeMinio && model.Infrastructure("MINIO").LognaraEnable)
            definitions.Add(("minio.Dockerfile", "quay.io/minio/minio:RELEASE.2025-04-22T22-12-26Z-cpuv1"));
        if (model.NginxMode == NginxMode.Compose && model.Infrastructure("NGINX").LognaraEnable)
            definitions.Add(("nginx.Dockerfile", "nginx:alpine"));

        return definitions.ToDictionary(
            item => item.File,
            item => BuildInfrastructureDockerfile(item.Base),
            StringComparer.Ordinal);
    }

    internal static string EscapeEnv(string value) => value
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("\"", "\\\"", StringComparison.Ordinal)
        .Replace("$", "\\$", StringComparison.Ordinal)
        .Replace("\r", "\\r", StringComparison.Ordinal)
        .Replace("\n", "\\n", StringComparison.Ordinal)
        .Replace("\t", "\\t", StringComparison.Ordinal);

    private static string PersistedCertificatePath(BuilderModel model, string sourcePath, string fileName) =>
        model.NginxMode == NginxMode.Compose && !string.IsNullOrWhiteSpace(model.OutputPath)
            ? Path.Combine(Path.GetFullPath(model.OutputPath), "certs", fileName)
            : sourcePath;

    private static string BuildInfrastructureDockerfile(string baseImage) => $$"""
FROM --platform=$BUILDPLATFORM alpine:3.22 AS lognara-agent
ARG TARGETARCH
RUN apk add --no-cache ca-certificates coreutils curl tar
ARG LOGNARA_AGENT_RELEASE={{LognaraAgentRelease}}
RUN set -eu; \
    case "$TARGETARCH" in amd64) checksum="{{LognaraAmd64Sha256}}" ;; arm64) checksum="{{LognaraArm64Sha256}}" ;; *) echo "unsupported TARGETARCH: $TARGETARCH" >&2; exit 1 ;; esac; \
    archive="lognara-agent-linux-${TARGETARCH}.tar.gz"; \
    curl --fail --location --silent --show-error "https://github.com/Liis17/Lognara/releases/download/${LOGNARA_AGENT_RELEASE}/${archive}" --output "/tmp/${archive}"; \
    printf '%s  %s\n' "$checksum" "/tmp/${archive}" | sha256sum --check --strict -; \
    tar -xzf "/tmp/${archive}" -C /usr/local/bin lognara-agent; \
    chmod 0755 /usr/local/bin/lognara-agent; \
    rm "/tmp/${archive}"

FROM {{baseImage}}
COPY --from=lognara-agent --chmod=0755 /usr/local/bin/lognara-agent /usr/local/bin/lognara-agent
""";

    internal const string LognaraAgentRelease = "agent-20261018-041923-6db0c80dd298";
    internal const string LognaraAmd64Sha256 = "97f0fd07b9e61500a5d0084616e5a285e3132bc756f12f6aca12bcc4c78492ff";
    internal const string LognaraArm64Sha256 = "8539aadd71cdd7978687ec8a6c3cb106c82e9efe54693b80b735e6f6110bff83";

    public static (string ApiKey, string ApiSecret) GenerateLiveKitCredentials()
    {
        var key = Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();
        var secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=')
            .Replace('+', 'a')
            .Replace('/', 'b');
        return ($"LK{key}", secret);
    }

    public static string GenerateSetupToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();

    private static ComposeService App(BuilderModel model, string service, string image, string[] dependencies)
    {
        var prefix = service.ToUpperInvariant().Replace('-', '_');
        var target = model.Application(prefix);
        return ComposeService.New(service, image)
            .WithDependencies(dependencies.Select(x => (x, (string?)null)).ToArray())
            .WithPorts(HostPorts(model, service))
            .WithAgent(model, target);
    }

    private static IEnumerable<(string Key, string Value)> AppLogging(string prefix)
    {
        return
        [
            ("ConsoleOutput", $"${{{prefix}_CONSOLE_OUTPUT:-true}}"),
            ("SeqEnable", $"${{{prefix}_SEQ_ENABLE:-false}}"),
            ("LognaraEnable", $"${{{prefix}_LOGNARA_ENABLE:-false}}")
        ];
    }

    private static ComposeService WithInfrastructureAgent(
        BuilderModel model,
        ComposeService service,
        string prefix,
        string baseImage,
        string entrypoint,
        string? command)
    {
        var target = model.Infrastructure(prefix);
        if (!model.IncludeLognara || !target.LognaraEnable)
            return service;

        var imageKey = prefix switch
        {
            "POSTGRES" or "POSTGRES_BOOTSTRAP" or "SETUP_POSTGRES" or "SETUP_POSTGRES_BOOTSTRAP" => "postgres",
            _ => target.ServiceName
        };
        var tag = imageKey switch
        {
            "postgres" => "18",
            "minio" => "RELEASE.2025-04-22T22-12-26Z-cpuv1",
            _ => "latest"
        };
        var originalEntrypoint = prefix.EndsWith("_BOOTSTRAP", StringComparison.Ordinal)
            ? new[] { "/bin/sh", "-ec" }
            : new[] { entrypoint };
        return service
            .WithImage($"barkfluff/lognara-{imageKey}:{tag}")
            .WithBuild(imageKey + ".Dockerfile")
            .WithProperty("entrypoint", ComposeArray(new[] { "/usr/local/bin/lognara-agent", "run", "--" }.Concat(originalEntrypoint).ToArray()))
            .WithProperty("init", "true")
            .WithProperty("stop_grace_period", "60s")
            .WithEnvironment(LognaraInfrastructureEnvironment(target))
            .WithCommandIfMissing(command)
            .WithVolumes($"{LognaraSpoolVolume(target)}:/var/lib/lognara-agent/spool");
    }

    private static IEnumerable<(string Key, string Value)> LognaraInfrastructureEnvironment(BuilderLognaraTarget target) =>
    [
        ("ConsoleOutput", $"${{{target.Prefix}_CONSOLE_OUTPUT:-true}}"),
        ("SeqEnable", $"${{{target.Prefix}_SEQ_ENABLE:-false}}"),
        ("LognaraEnable", $"${{{target.Prefix}_LOGNARA_ENABLE:-false}}"),
        ("LOGNARA_SERVER", "${LOGNARA_SERVER:?set LOGNARA_SERVER in .env}"),
        ("LOGNARA_BACKEND", "${LOGNARA_BACKEND:-barkfluff}"),
        ("LOGNARA_ENVIRONMENT", "${LOGNARA_ENVIRONMENT}"),
        ("LOGNARA_RELAY_URL", "http://lognara-relay:7401/v1/batches"),
        ("LOGNARA_SPOOL_DIR", "/var/lib/lognara-agent/spool"),
        ("LOGNARA_SERVICE", target.ServiceName),
        ("LOGNARA_RELAY_TOKEN", $"${{{target.RelayTokenVariable}:?set {target.RelayTokenVariable} in .env}}")
    ];

    private static IEnumerable<(string Key, string Value)> LognaraApplicationEnvironment(BuilderLognaraTarget target) =>
    [
        ("Lognara__AgentExecutable", "/usr/local/bin/lognara-agent"),
        ("Lognara__AgentUrl", "http://127.0.0.1:7400/v1/logs"),
        ("LOGNARA_LISTEN_ADDR", "127.0.0.1:7400"),
        ("LOGNARA_SPOOL_DIR", "/var/lib/lognara-agent/spool"),
        ("LOGNARA_SERVICE", target.ServiceName),
        ("LOGNARA_SERVER", "${LOGNARA_SERVER:?set LOGNARA_SERVER in .env}"),
        ("LOGNARA_BACKEND", "${LOGNARA_BACKEND:-barkfluff}"),
        ("LOGNARA_ENVIRONMENT", "${LOGNARA_ENVIRONMENT}"),
        ("LOGNARA_RELAY_URL", "http://lognara-relay:7401/v1/batches"),
        ("LOGNARA_RELAY_TOKEN", $"${{{target.RelayTokenVariable}:?set {target.RelayTokenVariable} in .env}}")
    ];

    private static ComposeService LognaraRelay(BuilderModel model) => ComposeService.New("lognara-relay", "docker.barkfluff.com/lognara-relay:master")
        .WithProperty("user", "1000:1000")
        .WithProperty("init", "true")
        .WithProperty("stop_grace_period", "60s")
        .WithEnvironment(
            ("LOGNARA_LISTEN_ADDR", "0.0.0.0:7401"),
            ("LOGNARA_CORE_URL", "${LOGNARA_CORE_URL:?set LOGNARA_CORE_URL in .env}"),
            ("LOGNARA_CORE_TOKEN", "${LOGNARA_RELAY_CORE_TOKEN:?set LOGNARA_RELAY_CORE_TOKEN in .env}"),
            ("LOGNARA_SPOOL_DIR", "/var/lib/lognara-relay/spool"))
        .WithVolumes($"{LognaraRelayVolume}:/var/lib/lognara-relay/spool");

    private static string BuildVolumeDefinitions(IReadOnlyList<ComposeService> services)
    {
        return string.Concat(services
            .SelectMany(service => service.NamedVolumes)
            .Distinct(StringComparer.Ordinal)
            .Select(name => $"  {name}:\n    name: barkfluff-{name}\n"));
    }

    private static string RenderCompose(IReadOnlyList<ComposeService> services)
    {
        var b = new StringBuilder("name: barkfluff\n\n");
        b.AppendLine("x-common-variables: &common-variables");
        b.AppendLine("  SETTINGS_SERVICE_URL: \"${SETTINGS_SERVICE_URL:-http://settings:7003}\"");
        b.AppendLine("  CONFIGURATION_SERVICE_URL: \"${SETTINGS_SERVICE_URL:-http://settings:7003}\"");
        b.AppendLine("  ASPNETCORE_ENVIRONMENT: \"${ASPNETCORE_ENVIRONMENT:-Production}\"");
        b.AppendLine("\nservices:");
        foreach (var service in services)
            service.AppendTo(b);

        b.AppendLine("\nnetworks:");
        b.AppendLine($"  {Network}:");
        b.AppendLine("    external: true");
        b.AppendLine("    name: ${DOCKER_NETWORK_NAME}");
        var volumeDefinitions = BuildVolumeDefinitions(services);
        if (!string.IsNullOrEmpty(volumeDefinitions))
        {
            b.AppendLine("\nvolumes:");
            b.Append(volumeDefinitions);
        }
        return b.ToString();
    }

    private static string BootstrapCommand() => """
for database in settings identity users messages files onliner calls bots federation developers; do
  echo "Ensuring PostgreSQL database exists: $$database"
  exists="$$(psql -tAc "SELECT 1 FROM pg_database WHERE datname = '$$database'" -d postgres)"
  if [ "$$exists" != "1" ]; then
    createdb --maintenance-db=postgres "$$database"
  fi
done
""";

    private static string PostgresCommand() => """
["postgres", "-c", "max_connections=150", "-c", "shared_buffers=768MB", "-c", "effective_cache_size=2GB", "-c", "maintenance_work_mem=192MB", "-c", "work_mem=12MB", "-c", "random_page_cost=1.1", "-c", "effective_io_concurrency=200", "-c", "max_wal_size=2GB", "-c", "min_wal_size=512MB", "-c", "checkpoint_completion_target=0.9", "-c", "wal_compression=on", "-c", "jit=off"]
""";

    private static string ImageSuffix(string channel) => channel switch
    {
        "Nightly" => "-nightly",
        "Dev" => "-dev",
        "Release" => "",
        _ => "-nightly"
    };

    private static string AppImage(string service, string suffix) =>
        $"docker.barkfluff.com/barkfluff-{service}{suffix}:latest";

    private static string[] HostPorts(BuilderModel model, string service)
    {
        if (model.NginxMode != NginxMode.Host)
            return [];

        string[] ports = service switch
        {
            "identity" => ["7000"],
            "users" => ["7001"],
            "beacon" => ["7002"],
            "files" => ["7005", "7006"],
            "messages" => ["7007"],
            "fast-auth" => ["7008"],
            "onliner" => ["7009"],
            "updates" => ["7015"],
            "web" => ["7016"],
            "developers" => ["7020", "7021"],
            "calls" => ["7025"],
            "bots" => ["7027", "7028"],
            "federation" => ["7030"],
            "admin-panel" => ["51888"],
            _ => []
        };

        return ports.Select(port => $"127.0.0.1:{port}:{port}").ToArray();
    }

    private static ComposeService CreateNginxService(BuilderModel model)
    {
        var nginx = ComposeService.New("nginx", "nginx:alpine")
            .WithPorts("80:80", "443:443")
            .WithVolumes(
                "./nginx/nginx.conf:/etc/nginx/nginx.conf:ro",
                "./nginx/sites:/etc/nginx/conf.d:ro",
                "./certs:/etc/nginx/certs:ro")
            .WithCommand(ComposeArray("nginx", "-g", "daemon off;"));
        return WithInfrastructureAgent(model, nginx, "NGINX", "nginx:alpine", "/docker-entrypoint.sh", ComposeArray("nginx", "-g", "daemon off;"));
    }

    private static string HostPort(string hostPort, string environmentKey, string containerPort) =>
        string.IsNullOrWhiteSpace(hostPort) ? "" : $"127.0.0.1:${{{environmentKey}}}:{containerPort}";

    private static string Bool(bool value) => value ? "true" : "false";

    private static string ComposeArray(params string[] values) =>
        "[" + string.Join(", ", values.Select(x => $"\"{x.Replace("\"", "\\\"", StringComparison.Ordinal)}\"")) + "]";

    private static string YamlQuote(string value) =>
        "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal) + "\"";

    private static string VolumeKey(BuilderLognaraTarget target) => target.ServiceName;

    private static string LognaraSpoolVolume(BuilderLognaraTarget target) =>
        $"lognara-agent-spool-{VolumeKey(target)}";

    private sealed class ComposeService(string key, string image)
    {
        private readonly List<(string Key, string Value)> _environment = [];
        private readonly List<string> _volumes = [];
        private readonly List<string> _ports = [];
        private readonly List<(string Name, string? Condition)> _dependencies = [];
        private readonly List<string> _secrets = [];
        private readonly Dictionary<string, string> _properties = new(StringComparer.Ordinal);
        private string _image = image;
        private string? _command;
        private string? _healthcheck;
        private string? _buildFile;
        private bool _commonEnvironment;

        public static ComposeService New(string key, string image) => new(key, image);

        public IEnumerable<string> NamedVolumes => _volumes
            .Select(volume => volume.Split(':', 2)[0])
            .Where(source => source.Length > 0 && source[0] is not ('/' or '.' or '$'));

        public ComposeService WithImage(string value) { _image = value; return this; }
        public ComposeService WithProperty(string key, string value) { _properties[key] = value; return this; }
        public ComposeService WithCommand(string? command) { _command = command; return this; }
        public ComposeService WithCommandIfMissing(string? command) { _command ??= command; return this; }
        public ComposeService WithHealthcheck(string value) { _healthcheck = value; return this; }
        public ComposeService WithBuild(string dockerfile) { _buildFile = dockerfile; return this; }
        public ComposeService WithEnvironment(params (string Key, string Value)[] values) { _environment.AddRange(values); return this; }
        public ComposeService WithEnvironment(IEnumerable<(string Key, string Value)> values) { _environment.AddRange(values); return this; }
        public ComposeService WithVolumes(params string[] values) { _volumes.AddRange(values.Where(x => !string.IsNullOrWhiteSpace(x))); return this; }
        public ComposeService WithPorts(params string[] values) { _ports.AddRange(values.Where(x => !string.IsNullOrWhiteSpace(x))); return this; }
        public ComposeService WithSecret(string value) { _secrets.Add(value); return this; }
        public ComposeService WithDependency(string value) { _dependencies.Add((value, null)); return this; }
        public ComposeService WithDependencies(params (string Name, string? Condition)[] values) { _dependencies.AddRange(values); return this; }

        public ComposeService WithAgent(BuilderModel builderModel, BuilderLognaraTarget target)
        {
            _commonEnvironment = true;
            _environment.AddRange(AppLogging(target.Prefix));
            var isActive = builderModel.IncludeLognara && target.LognaraEnable;
            if (!isActive)
                return this;

            _properties["init"] = "true";
            _properties["stop_grace_period"] = "90s";
            _environment.AddRange(LognaraApplicationEnvironment(target));
            _volumes.Add($"lognara-buffer-{VolumeKey(target)}:/app/logs");
            _volumes.Add($"{LognaraSpoolVolume(target)}:/var/lib/lognara-agent/spool");
            return this;
        }

        public void AppendTo(StringBuilder b)
        {
            b.AppendLine($"  {key}:");
            b.AppendLine($"    image: {_image}");
            b.AppendLine($"    container_name: barkfluff-{key}");
            if (_buildFile is not null)
            {
                b.AppendLine("    build:");
                b.AppendLine("      context: .");
                b.AppendLine($"      dockerfile: lognara/{_buildFile}");
            }

            b.AppendLine($"    restart: {_properties.GetValueOrDefault("restart", "always")}");
            foreach (var (name, value) in _properties.Where(x => x.Key is not "restart" and not "entrypoint"))
                b.AppendLine($"    {name}: {value}");
            if (_properties.TryGetValue("entrypoint", out var entrypoint))
                b.AppendLine($"    entrypoint: {entrypoint}");
            if (_command is not null)
                AppendBlock(b, "command", _command);
            if (_healthcheck is not null)
            {
                b.AppendLine("    healthcheck:");
                b.AppendLine($"      test: {_healthcheck}");
                b.AppendLine("      interval: 5s");
                b.AppendLine("      timeout: 5s");
                b.AppendLine("      retries: 12");
            }
            if (_ports.Count > 0)
            {
                b.AppendLine("    ports:");
                foreach (var port in _ports)
                    b.AppendLine($"      - \"{port}\"");
            }
            if (_environment.Count > 0 || _commonEnvironment)
            {
                b.AppendLine("    environment:");
                if (_commonEnvironment)
                    b.AppendLine("      <<: *common-variables");
                foreach (var (envKey, value) in _environment)
                    b.AppendLine($"      {envKey}: {YamlQuote(value)}");
            }
            if (_volumes.Count > 0)
            {
                b.AppendLine("    volumes:");
                foreach (var volume in _volumes)
                    b.AppendLine($"      - {volume}");
            }
            if (_secrets.Count > 0)
            {
                b.AppendLine("    secrets:");
                foreach (var secret in _secrets)
                    b.AppendLine($"      - {secret}");
            }
            b.AppendLine("    networks:");
            b.AppendLine($"      - {Network}");
            if (_dependencies.Count > 0)
            {
                if (_dependencies.All(x => x.Condition is null))
                {
                    b.AppendLine("    depends_on:");
                    foreach (var dependency in _dependencies)
                        b.AppendLine($"      - {dependency.Name}");
                }
                else
                {
                    b.AppendLine("    depends_on:");
                    foreach (var (name, condition) in _dependencies)
                    {
                        b.AppendLine($"      {name}:");
                        b.AppendLine($"        condition: {condition ?? "service_started"}");
                    }
                }
            }
            b.AppendLine();
        }

        private static void AppendBlock(StringBuilder b, string key, string value)
        {
            var lines = value.Trim('\n', '\r').Split('\n');
            if (value.TrimStart().StartsWith("[", StringComparison.Ordinal))
            {
                b.AppendLine($"    {key}: {value.Trim()}");
                return;
            }
            b.AppendLine($"    {key}:");
            b.AppendLine("      - |");
            foreach (var line in lines)
                b.AppendLine("        " + line.TrimEnd('\r'));
        }
    }

}
