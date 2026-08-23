using Barkfluff.AdminPanel.Services;

using Xunit;

namespace Barkfluff.AdminPanel.Tests.Services;

public class SensitiveConfigMaskerTests
{
    [Theory]
    [InlineData("S3Buckets:profile-pictures", "AccessKey", true)]
    [InlineData("S3Buckets:profile-pictures", "SecretKey", true)]
    [InlineData("Minio", "AccessKey", true)]
    [InlineData("JwtSettings", "SecretKey", true)]
    [InlineData("Mail", "Password", true)]
    [InlineData("Identity", "ServiceToken", true)]
    [InlineData("S3Buckets:profile-pictures", "ServiceUrl", false)]
    [InlineData("S3Buckets:profile-pictures", "BucketName", false)]
    [InlineData("S3Buckets:profile-pictures", "Region", false)]
    public void IsSensitive_DetectsCredentialRows(string section, string key, bool expected)
    {
        Assert.Equal(expected, SensitiveConfigMasker.IsSensitive(section, key));
    }

    [Fact]
    public void MaskAccessKey_LongValue_ShowsHeadAndTailOnly()
    {
        Assert.Equal("AKI…LE", SensitiveConfigMasker.MaskAccessKey("AKIAIOSFODNN7EXAMPLE"));
        Assert.Equal("min…in", SensitiveConfigMasker.MaskAccessKey("minioadmin"));
    }

    [Fact]
    public void MaskAccessKey_ShortValue_ReturnsFullMask()
    {
        Assert.Equal(SensitiveConfigMasker.MaskedValue, SensitiveConfigMasker.MaskAccessKey("minio"));
    }

    [Fact]
    public void MaskAccessKey_EmptyValue_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, SensitiveConfigMasker.MaskAccessKey(""));
        Assert.Equal(string.Empty, SensitiveConfigMasker.MaskAccessKey(null!));
    }
}
