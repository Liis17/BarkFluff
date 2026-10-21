using BarkFluff.Builder;
using YamlDotNet.RepresentationModel;
using YamlDotNet.Serialization;
using Xunit;

namespace BarkFluff.Builder.Tests;

public sealed class BuilderComposeYamlTests
{
    [Fact]
    public void AllComposeCombinationsHaveValidReferencesAndExactNamedVolumes()
    {
        var strictDeserializer = new DeserializerBuilder()
            .WithDuplicateKeyChecking()
            .Build();
        var targetCount = BuilderTestSupport.ValidModel().AllLognaraTargets().Count();

        foreach (var nginxMode in Enum.GetValues<NginxMode>())
        foreach (var includeSeq in new[] { false, true })
        foreach (var includeMinio in new[] { false, true })
        foreach (var includeLognara in new[] { false, true })
        foreach (var targetIndex in includeLognara ? Enumerable.Range(-1, targetCount + 1) : new[] { -2 })
        {
            var model = BuilderTestSupport.ValidModel();
            model.NginxMode = nginxMode;
            model.IncludeSeq = includeSeq;
            model.IncludeMinio = includeMinio;
            model.IncludeLognara = includeLognara;
            if (targetIndex == -1)
            {
                foreach (var target in model.AllLognaraTargets())
                    target.LognaraEnable = true;
            }
            else if (targetIndex >= 0)
            {
                model.AllLognaraTargets().ElementAt(targetIndex).LognaraEnable = true;
            }

            AssertCompose(ComposeGenerator.BuildCompose(model), strictDeserializer);
            AssertCompose(ComposeGenerator.BuildSetupCompose(model), strictDeserializer);
        }
    }

    private static void AssertCompose(string content, IDeserializer strictDeserializer)
    {
        strictDeserializer.Deserialize<object>(content);

        var yaml = new YamlStream();
        yaml.Load(new StringReader(content));
        var root = Assert.IsType<YamlMappingNode>(Assert.Single(yaml.Documents).RootNode);
        var services = Assert.IsType<YamlMappingNode>(Get(root, "services"));
        var serviceNames = services.Children.Keys.Select(Scalar).ToHashSet(StringComparer.Ordinal);
        var declaredVolumes = GetKeys(root, "volumes");
        var declaredNetworks = GetKeys(root, "networks");
        var declaredSecrets = GetKeys(root, "secrets");
        var usedVolumes = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (serviceNameNode, serviceNode) in services.Children)
        {
            var serviceName = Scalar(serviceNameNode);
            var service = Assert.IsType<YamlMappingNode>(serviceNode);
            if (TryGet(service, "environment", out var environmentNode))
            {
                var environmentKeys = environmentNode switch
                {
                    YamlMappingNode environment => environment.Children.Keys.Select(Scalar),
                    YamlSequenceNode environment => environment.Children
                        .Select(item => Scalar(item).Split('=', 2)[0]),
                    _ => throw new Xunit.Sdk.XunitException($"Unexpected environment format for {serviceName}.")
                };
                var keys = environmentKeys.ToArray();
                Assert.Equal(keys.Length, keys.Distinct(StringComparer.Ordinal).Count());
            }

            if (TryGet(service, "volumes", out var volumesNode))
            {
                var mounts = Assert.IsType<YamlSequenceNode>(volumesNode).Children
                    .Select(item => Scalar(item))
                    .ToArray();
                var targets = mounts.Select(mount => mount.Split(':').ElementAtOrDefault(1) ?? "").ToArray();
                Assert.All(targets, target => Assert.False(string.IsNullOrWhiteSpace(target), $"Invalid volume mount for {serviceName}."));
                Assert.Equal(targets.Length, targets.Distinct(StringComparer.Ordinal).Count());

                foreach (var mount in mounts)
                {
                    var source = mount.Split(':', 2)[0];
                    if (source.Length > 0 && source[0] is not ('/' or '.' or '$'))
                        usedVolumes.Add(source);
                }
            }

            AssertReferencesExist(service, "depends_on", serviceNames, serviceName);
            AssertReferencesExist(service, "secrets", declaredSecrets, serviceName);
            AssertReferencesExist(service, "networks", declaredNetworks, serviceName);
        }

        Assert.True(usedVolumes.SetEquals(declaredVolumes),
            $"Named volumes must be declared exactly when used. Used [{string.Join(", ", usedVolumes)}], declared [{string.Join(", ", declaredVolumes)}].");
    }

    private static void AssertReferencesExist(
        YamlMappingNode service,
        string propertyName,
        IReadOnlySet<string> declarations,
        string serviceName)
    {
        if (!TryGet(service, propertyName, out var referencesNode))
            return;

        var references = referencesNode switch
        {
            YamlMappingNode mapping => mapping.Children.Keys.Select(Scalar),
            YamlSequenceNode sequence => sequence.Children.Select(Scalar),
            _ => throw new Xunit.Sdk.XunitException($"Unexpected {propertyName} format for {serviceName}.")
        };

        foreach (var reference in references)
            Assert.Contains(reference, declarations);
    }

    private static HashSet<string> GetKeys(YamlMappingNode mapping, string key) =>
        TryGet(mapping, key, out var node)
            ? Assert.IsType<YamlMappingNode>(node).Children.Keys.Select(Scalar).ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);

    private static string Scalar(YamlNode node) => Assert.IsType<YamlScalarNode>(node).Value!;

    private static YamlNode Get(YamlMappingNode mapping, string key) =>
        mapping.Children[new YamlScalarNode(key)];

    private static bool TryGet(YamlMappingNode mapping, string key, out YamlNode node) =>
        mapping.Children.TryGetValue(new YamlScalarNode(key), out node!);
}
