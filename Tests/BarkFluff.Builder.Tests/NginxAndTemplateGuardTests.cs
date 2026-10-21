using System.Text.RegularExpressions;
using BarkFluff.Builder;
using BarkFluff.Node;
using Xunit;

namespace BarkFluff.Builder.Tests;

public sealed class NginxAndTemplateGuardTests
{
    private const string Domain = "node.example.com";

    [Fact]
    public void NginxServerNamesComeFromSharedSubdomainCatalog()
    {
        var model = BuilderTestSupport.ValidModel();
        model.NginxMode = NginxMode.Compose;
        var files = NginxGenerator.BuildFiles(model);
        var expected = NodeSubdomains.All.Values.Select(subdomain => $"{subdomain}.{Domain}")
            .ToHashSet(StringComparer.Ordinal);
        var actual = files.Where(file => file.Key.StartsWith("nginx/sites/", StringComparison.Ordinal))
            .SelectMany(file => Regex.Matches(file.Value, @"(?m)^\s*server_name\s+([^;]+);")
                .SelectMany(match => match.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)))
            .Where(host => host != "_")
            .ToHashSet(StringComparer.Ordinal);

        Assert.True(expected.SetEquals(actual), $"Expected [{string.Join(", ", expected)}], got [{string.Join(", ", actual)}].");
        Assert.Contains("nginx/sites/files-media.conf", files.Keys);
        Assert.Contains("files2.node.example.com", actual);
        Assert.Contains("panel.node.example.com", actual);
    }

    [Fact]
    public void ComposeNginxRoutesAndUpstreamsTrackNightlySites()
    {
        var root = BuilderTestSupport.FindRepositoryRoot();
        var baselineDirectory = Path.Combine(root, "Docker", "nightly", "nginx", "sites");
        var model = BuilderTestSupport.ValidModel();
        model.NginxMode = NginxMode.Compose;
        var generated = NginxGenerator.BuildFiles(model);
        var baselineNames = new[]
        {
            "identity", "users", "messages", "files", "files-media", "updates", "onliner", "fast-auth",
            "federation", "beacon", "bots", "calls", "livekit", "web", "developers", "admin-panel"
        };

        foreach (var name in baselineNames)
        {
            var baseline = File.ReadAllText(Path.Combine(baselineDirectory, name + ".conf"));
            var current = generated[$"nginx/sites/{name}.conf"];
            Assert.True(LocationSet(baseline).SetEquals(LocationSet(current)), $"Location drift: {name}");
            Assert.True(UpstreamSet(baseline).SetEquals(UpstreamSet(current)), $"Upstream drift: {name}");
        }

        // The setup site is intentionally new; certificates are builder-owned, and resolver/upstream values vary by nginx mode.
        Assert.Contains("nginx/sites/setup.conf", generated.Keys);
        Assert.Contains("resolver 127.0.0.11", generated["nginx/sites/identity.conf"]);
        Assert.Contains("/etc/nginx/certs/barkfluff-crt.pem", generated["nginx/sites/01-ssl-params.conf"]);
        Assert.Contains("/etc/nginx/certs/barkfluff-key.pem", generated["nginx/sites/01-ssl-params.conf"]);
    }

    [Fact]
    public void NginxModesSelectResolverAndOnlyCloudflareCidrs()
    {
        var model = BuilderTestSupport.ValidModel();
        model.NginxMode = NginxMode.ExternalContainer;
        model.NginxCertificatePath = "/container/certs/node.pem";
        model.NginxKeyPath = "/container/certs/node.key";
        var external = NginxGenerator.BuildFiles(model);
        Assert.Contains("resolver 127.0.0.11", external["nginx/sites/setup.conf"]);
        Assert.Contains("/container/certs/node.pem", external["nginx/sites/01-ssl-params.conf"]);

        model.NginxMode = NginxMode.Host;
        var host = NginxGenerator.BuildFiles(model);
        Assert.DoesNotContain("resolver 127.0.0.11", host["nginx/sites/setup.conf"]);
        Assert.Contains("http://127.0.0.1:7032", host["nginx/sites/setup.conf"]);

        model.BehindCloudflare = true;
        var cloudflare = NginxGenerator.BuildFiles(model)["nginx/sites/00-real-ip.conf"];
        // Unlike the nightly catch-all file, the builder trusts only Cloudflare's published IPv4 and IPv6 ranges.
        Assert.Contains("set_real_ip_from 103.21.244.0/22;", cloudflare);
        Assert.Contains("set_real_ip_from 2c0f:f248::/32;", cloudflare);
        Assert.DoesNotContain("0.0.0.0/0", cloudflare);
        Assert.Equal(22, Regex.Matches(cloudflare, @"(?m)^set_real_ip_from ").Count);

        model.NginxMode = NginxMode.None;
        Assert.Empty(NginxGenerator.BuildFiles(model));
    }

    [Fact]
    public void ComposeEnvironmentMountsAndDependenciesTrackNightlyWithDocumentedDifferences()
    {
        var root = BuilderTestSupport.FindRepositoryRoot();
        var baseline = File.ReadAllText(Path.Combine(root, "Docker", "nightly", "barkfluff", "docker-compose.yml"));
        var model = BuilderTestSupport.ValidModel();
        model.IncludeSeq = true;
        model.IncludeMinio = true;
        var generated = ComposeGenerator.BuildCompose(model);
        var names = BuilderTestSupport.ServiceNames(baseline).Intersect(BuilderTestSupport.ServiceNames(generated), StringComparer.Ordinal);

        foreach (var service in names)
        {
            var baselineBlock = BuilderTestSupport.Block(baseline, service);
            var generatedBlock = BuilderTestSupport.Block(generated, service);
            var expectedEnvironment = BuilderTestSupport.SectionItems(baselineBlock, "environment", keyItems: true);
            var actualEnvironment = BuilderTestSupport.SectionItems(generatedBlock, "environment", keyItems: true);
            if (service == "settings")
                expectedEnvironment.UnionWith(new[] { "NODE_PUBLIC_DOMAIN", "NODE_NAVIGATOR_URL", "NODE_S3_ENDPOINT" });
            if (service == "admin-panel")
            {
                // Stage 1 removed AdminPanel JWT overrides; Settings owns those JWTs. Seq can now be disabled.
                expectedEnvironment.ExceptWith(new[] { "UsersService__Token", "FilesService__Token", "MessagesService__Token", "IdentityService__Token", "BotsService__Token" });
                expectedEnvironment.Add("Seq__Enabled");
            }
            Assert.True(expectedEnvironment.SetEquals(actualEnvironment), $"Environment key drift: {service}. Expected [{string.Join(", ", expectedEnvironment)}], got [{string.Join(", ", actualEnvironment)}].");

            // New optional MinIO, nginx, Lognara and setup-only services have no nightly main-stack baseline.
            Assert.True(BuilderTestSupport.SectionItems(baselineBlock, "volumes").SetEquals(BuilderTestSupport.SectionItems(generatedBlock, "volumes")), $"Mount drift: {service}");
            Assert.True(BuilderTestSupport.DependencyItems(baselineBlock).SetEquals(BuilderTestSupport.DependencyItems(generatedBlock)), $"depends_on drift: {service}");
        }

        Assert.Contains("Seq__Enabled: \"true\"", BuilderTestSupport.Block(generated, "admin-panel"));
        Assert.DoesNotContain("UsersService__Token", BuilderTestSupport.Block(generated, "admin-panel"));
    }

    [Fact]
    public void GeneratedComposeVariableReferencesHaveDotenvValuesOrDocumentedDefaults()
    {
        var model = BuilderTestSupport.ValidModel();
        model.IncludeSeq = true;
        var compose = ComposeGenerator.BuildCompose(model) + ComposeGenerator.BuildSetupCompose(model);
        var composeReferences = Regex.Matches(compose, @"\$\{([A-Z][A-Z0-9_]*)")
            .Select(match => match.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);
        var dotenvKeys = Regex.Matches(ComposeGenerator.BuildEnv(model), "(?m)^([A-Z][A-Z0-9_]*)=\\\"")
            .Select(match => match.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);
        var allowedComposeDefaults = new HashSet<string>(["SETTINGS_SERVICE_URL", "SETTINGS_DATABASE"], StringComparer.Ordinal);

        composeReferences.ExceptWith(dotenvKeys);
        composeReferences.ExceptWith(allowedComposeDefaults);
        Assert.Empty(composeReferences);
    }

    [Fact]
    public void SetupComposeTracksNightlyWithExplicitBootstrapAndBuilderAdditions()
    {
        var root = BuilderTestSupport.FindRepositoryRoot();
        var baseline = File.ReadAllText(Path.Combine(root, "Docker", "nightly", "barkfluff", "docker-compose.setup.yml"));
        var model = BuilderTestSupport.ValidModel();
        model.IncludeSeq = true;
        model.NginxMode = NginxMode.Compose;
        var generated = ComposeGenerator.BuildSetupCompose(model);
        var expectedServices = BuilderTestSupport.ServiceNames(baseline);
        expectedServices.UnionWith(new[] { "seq", "nginx" });
        Assert.True(expectedServices.SetEquals(BuilderTestSupport.ServiceNames(generated)), $"Expected [{string.Join(", ", expectedServices)}], got [{string.Join(", ", BuilderTestSupport.ServiceNames(generated))}].");

        foreach (var service in BuilderTestSupport.ServiceNames(baseline))
        {
            var baselineBlock = BuilderTestSupport.Block(baseline, service);
            var generatedBlock = BuilderTestSupport.Block(generated, service);
            var expectedEnvironment = BuilderTestSupport.SectionItems(baselineBlock, "environment", keyItems: true);
            var actualEnvironment = BuilderTestSupport.SectionItems(generatedBlock, "environment", keyItems: true);
            // Generated setup services inherit ASPNETCORE_ENVIRONMENT through the common anchor; setup settings also receive node defaults.
            if (service is "setup" or "settings")
                actualEnvironment.Add("ASPNETCORE_ENVIRONMENT");
            if (service == "settings")
                expectedEnvironment.UnionWith(new[] { "NODE_PUBLIC_DOMAIN", "NODE_NAVIGATOR_URL", "NODE_S3_ENDPOINT" });
            Assert.True(expectedEnvironment.SetEquals(actualEnvironment), $"Setup environment drift: {service}. Expected [{string.Join(", ", expectedEnvironment)}], got [{string.Join(", ", actualEnvironment)}].");

            var expectedMounts = BuilderTestSupport.SectionItems(baselineBlock, "volumes");
            var actualMounts = BuilderTestSupport.SectionItems(generatedBlock, "volumes");
            if (service == "postgres")
                // The setup stack also mounts ./backups:/backup for the generated PostgreSQL backup directory.
                expectedMounts.Add("./backups:/backup");
            Assert.True(expectedMounts.SetEquals(actualMounts), $"Setup mount drift: {service}");
            Assert.True(BuilderTestSupport.DependencyItems(baselineBlock).SetEquals(BuilderTestSupport.DependencyItems(generatedBlock)), $"Setup depends_on drift: {service}");
        }

        // PostgreSQL createdb has no --if-not-exists option; the generated bootstrap queries pg_database first.
        Assert.DoesNotContain("createdb --if-not-exists", generated);
        Assert.Contains("127.0.0.1:${SETUP_HOST_PORT}:7032", BuilderTestSupport.Block(generated, "setup"));
    }
    public static HashSet<string> LocationSet(string text) => Regex.Matches(text, @"(?m)^\s*location\s+([^{}]+?)\s*\{")
        .Select(match => match.Groups[1].Value.Trim())
        .ToHashSet(StringComparer.Ordinal);

    public static HashSet<string> UpstreamSet(string text) => Regex.Matches(text, @"\b(?:grpc|http)://[A-Za-z0-9._-]+:\d+")
        .Select(match => match.Value)
        .ToHashSet(StringComparer.Ordinal);
}
