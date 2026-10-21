using BarkFluff.Builder;
using YamlDotNet.RepresentationModel;
using YamlDotNet.Serialization;
using Xunit;

namespace BarkFluff.Builder.Tests;

public sealed class BuilderComposeYamlTests
{
    [Fact]
    public void AllComposeCombinationsHaveStrictYamlAndUniqueServiceKeysAndMountTargets()
    {
        var strictDeserializer = new DeserializerBuilder()
            .WithDuplicateKeyChecking()
            .Build();

        foreach (var nginxMode in Enum.GetValues<NginxMode>())
        foreach (var includeSeq in new[] { false, true })
        foreach (var includeMinio in new[] { false, true })
        foreach (var includeLognara in new[] { false, true })
        {
            var model = BuilderTestSupport.ValidModel();
            model.NginxMode = nginxMode;
            model.IncludeSeq = includeSeq;
            model.IncludeMinio = includeMinio;
            model.IncludeLognara = includeLognara;
            if (includeLognara)
                foreach (var target in model.AllLognaraTargets())
                    target.LognaraEnable = true;

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

        foreach (var (serviceName, serviceNode) in services.Children)
        {
            var service = Assert.IsType<YamlMappingNode>(serviceNode);
            if (TryGet(service, "environment", out var environmentNode))
            {
                var environmentKeys = environmentNode switch
                {
                    YamlMappingNode environment => environment.Children.Keys
                        .Select(key => Assert.IsType<YamlScalarNode>(key).Value!),
                    YamlSequenceNode environment => environment.Children
                        .Select(item => Assert.IsType<YamlScalarNode>(item).Value!.Split('=', 2)[0]),
                    _ => throw new Xunit.Sdk.XunitException($"Unexpected environment format for {serviceName}.")
                };
                var keys = environmentKeys.ToArray();
                Assert.Equal(keys.Length, keys.Distinct(StringComparer.Ordinal).Count());
            }

            if (TryGet(service, "volumes", out var volumesNode))
            {
                var mounts = Assert.IsType<YamlSequenceNode>(volumesNode).Children
                    .Select(item => Assert.IsType<YamlScalarNode>(item).Value!)
                    .ToArray();
                var targets = mounts.Select(mount => mount.Split(':').ElementAtOrDefault(1) ?? "").ToArray();
                Assert.All(targets, target => Assert.False(string.IsNullOrWhiteSpace(target), $"Invalid volume mount for {serviceName}."));
                Assert.Equal(targets.Length, targets.Distinct(StringComparer.Ordinal).Count());
            }
        }
    }

    private static YamlNode Get(YamlMappingNode mapping, string key) =>
        mapping.Children[new YamlScalarNode(key)];

    private static bool TryGet(YamlMappingNode mapping, string key, out YamlNode node) =>
        mapping.Children.TryGetValue(new YamlScalarNode(key), out node!);
}
