using BarkFluff.Identity.Services;

using Xunit;

namespace BarkFluff.Identity.Tests.Services;

public class PasswordHasherTests
{
    [Fact]
    public void HashPassword_ReturnsBCryptHash()
    {
        var hash = PasswordHasher.HashPassword("testpassword");

        Assert.True(hash.StartsWith("$2"));
        Assert.True(hash.Length >= 4);
    }

    [Fact]
    public void HashPassword_DifferentPasswords_DifferentHashes()
    {
        var hash1 = PasswordHasher.HashPassword("password1");
        var hash2 = PasswordHasher.HashPassword("password2");

        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void HashPassword_SamePassword_DifferentHashes_SaltsDiffer()
    {
        var hash1 = PasswordHasher.HashPassword("samepassword");
        var hash2 = PasswordHasher.HashPassword("samepassword");

        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void VerifyPassword_BCryptHash_CorrectPassword_ReturnsTrue()
    {
        var hash = PasswordHasher.HashPassword("mypassword");

        var result = PasswordHasher.VerifyPassword("mypassword", hash);

        Assert.True(result);
    }

    [Fact]
    public void VerifyPassword_BCryptHash_WrongPassword_ReturnsFalse()
    {
        var hash = PasswordHasher.HashPassword("mypassword");

        var result = PasswordHasher.VerifyPassword("wrongpassword", hash);

        Assert.False(result);
    }

    [Fact]
    public void VerifyPassword_NullHash_ReturnsFalse()
    {
        var result = PasswordHasher.VerifyPassword("password", null);

        Assert.False(result);
    }

    [Fact]
    public void VerifyPassword_EmptyHash_ReturnsFalse()
    {
        var result = PasswordHasher.VerifyPassword("password", "");

        Assert.False(result);
    }

    [Fact]
    public void VerifyPassword_LegacySha256Hash_CorrectPassword_ReturnsTrue()
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        var bytes = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes("testpassword"));
        var legacyHash = Convert.ToBase64String(bytes);

        var result = PasswordHasher.VerifyPassword("testpassword", legacyHash);

        Assert.True(result);
    }

    [Fact]
    public void VerifyPassword_LegacySha256Hash_WrongPassword_ReturnsFalse()
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        var bytes = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes("testpassword"));
        var legacyHash = Convert.ToBase64String(bytes);

        var result = PasswordHasher.VerifyPassword("wrongpassword", legacyHash);

        Assert.False(result);
    }
}
