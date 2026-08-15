using BarkFluff.Client.Core.Services;

namespace BarkFluff.Client.WinUI.Tests;

public sealed class ClientMetadataTests
{
    [Fact]
    public void OperatingSystem_IsSentAsFriendlyWindowsName()
    {
        var operatingSystem = ClientMetadata.OperatingSystem;

        Assert.StartsWith("Windows", operatingSystem);
        Assert.DoesNotContain("Microsoft Windows NT", operatingSystem);
    }
}
