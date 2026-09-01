using Barkfluff.Developers.Infrastructure;

namespace Barkfluff.Developers.Tests;

public class PublishedProtoCatalogTests
{
    [Fact]
    public void Exposes_only_the_explicit_published_manifest()
    {
        var source = new TestProtoFileSource(new Dictionary<string, string>
        {
            ["shared.proto"] = "public",
            ["configuration_api.proto"] = "internal"
        });
        var catalog = new PublishedProtoCatalog(source);

        catalog.PublishedFileNames.Should().HaveCount(10);
        catalog.PublishedFileNames.Should().Contain(PublishedProtoManifest.FileNames);
        catalog.IsPublished("configuration_api.proto").Should().BeFalse();
        catalog.IsPublished("federation_internal_api.proto").Should().BeFalse();
        catalog.GetContent("configuration_api.proto").Should().BeNull();
        catalog.GetContent("shared.proto").Should().Be("public");
    }

    [Fact]
    public void Reports_a_published_file_missing_from_the_physical_source()
    {
        var source = new TestProtoFileSource(new Dictionary<string, string>
        {
            ["shared.proto"] = "public"
        });
        var catalog = new PublishedProtoCatalog(source);

        catalog.GetMissingFiles().Should().Contain("identity_api.proto");
        catalog.GetMissingFiles().Should().NotContain("configuration_api.proto");
    }

    [Fact]
    public void Provider_ignores_internal_files_even_if_they_exist_in_its_directory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"barkfluff-developers-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        try
        {
            File.WriteAllText(Path.Combine(directory, "shared.proto"), "public");
            File.WriteAllText(Path.Combine(directory, "configuration_api.proto"), "internal");

            var provider = new ProtoFileProvider(directory);

            provider.GetContent("shared.proto").Should().NotBeNull();
            provider.GetContent("configuration_api.proto").Should().BeNull();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class TestProtoFileSource : IProtoFileSource
    {
        private readonly IReadOnlyDictionary<string, string> _files;

        public TestProtoFileSource(IReadOnlyDictionary<string, string> files)
        {
            _files = files;
        }

        public string? GetContent(string fileName)
        {
            return _files.TryGetValue(fileName, out var content) ? content : null;
        }
    }
}
