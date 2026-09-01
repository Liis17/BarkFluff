using BarkFluff.Settings.Catalog;
using BarkFluff.Shared.Identity;

using Xunit;

namespace BarkFluff.Settings.Tests.Catalog;

public sealed class SettingsSetupValidationTests
{
    [Fact]
    public void Spki_hex_is_normalized_to_base64()
    {
        var entry = SettingsCatalog.Resolve(ServiceId.Federation, "Federation:TlsSpkiSha256");
        var hex = new string('a', 64);

        var result = SettingsSetupValidation.Validate(entry, hex);

        Assert.True(result.IsValid);
        Assert.Equal(Convert.ToBase64String(new byte[32].Select(_ => (byte)0xaa).ToArray()), result.Value);
    }

    [Theory]
    [InlineData("AccessKey")]
    [InlineData("SecretKey")]
    public void Storage_credentials_reject_empty_values(string key)
    {
        var entry = SettingsCatalog.Resolve(ServiceId.Files, $"S3Buckets:barkfluff-uploads:{key}");

        var result = SettingsSetupValidation.Validate(entry, " ");

        Assert.False(result.IsValid);
    }
}
