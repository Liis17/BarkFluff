using Xunit;

namespace BarkFluff.Settings.Tests.Compatibility;

public sealed class ConfigurationProtoContractTests
{
    [Fact]
    public void Existing_rpc_and_field_numbers_are_unchanged()
    {
        var proto = File.ReadAllText(FindProto());

        Assert.Contains("package barkfluff.configuration;", proto);
        Assert.Contains("service ConfigurationApi", proto);
        foreach (var rpc in new[] { "GetConfiguration", "GetAllConfigurations", "UpdateConfiguration", "GetConfigurationHistory", "RollbackConfiguration", "GetReservedNames", "AddReservedName", "UpdateReservedName", "DeleteReservedName" })
            Assert.Contains($"rpc {rpc}(", proto);
        Assert.Contains("int32 service_id = 1;", proto);
        Assert.Contains("string edited_from = 6;", proto);
        Assert.Contains("int32 service_id = 7;", proto);
        Assert.Contains("int64 source_revision_id = 11;", proto);
    }

    private static string FindProto()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "Shared", "BarkFluff.Proto", "configuration_api.proto");
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }
        throw new FileNotFoundException("configuration_api.proto was not found from the test output directory.");
    }
}
