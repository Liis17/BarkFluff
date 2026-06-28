using BarkFluff.Users.Services;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Moq;

namespace BarkFluff.Users.Tests.Services;

public class UsernameFormatValidatorTests
{
    [Theory]
    [InlineData("abc", true)]
    [InlineData("testuser123", true)]
    [InlineData("user_name", true)]
    [InlineData("UserName", true)]
    [InlineData("a1b2c3", true)]
    [InlineData("ab", false)]
    [InlineData("a", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("user name", false)]
    [InlineData("user-name", false)]
    [InlineData("user@name", false)]
    [InlineData("thisusernameiswaytoolongforthe32charlimitx", false)]
    public void IsValid_ReturnsCorrectResult(string? username, bool expected)
    {
        UsernameFormatValidator.IsValid(username).Should().Be(expected);
    }

    [Fact]
    public void IsValid_Exactly3Chars_ReturnsTrue()
    {
        UsernameFormatValidator.IsValid("abc").Should().BeTrue();
    }

    [Fact]
    public void IsValid_Exactly32Chars_ReturnsTrue()
    {
        var username = new string('a', 32);
        UsernameFormatValidator.IsValid(username).Should().BeTrue();
    }

    [Fact]
    public void IsValid_Exactly33Chars_ReturnsFalse()
    {
        var username = new string('a', 33);
        UsernameFormatValidator.IsValid(username).Should().BeFalse();
    }

    [Fact]
    public void IsValid_UnderscoreOnly_ReturnsTrue()
    {
        UsernameFormatValidator.IsValid("___").Should().BeTrue();
    }

    [Fact]
    public void IsValid_NumbersOnly_ReturnsTrue()
    {
        UsernameFormatValidator.IsValid("123").Should().BeTrue();
    }

    [Fact]
    public void IsValid_SpecialCharacters_ReturnsFalse()
    {
        UsernameFormatValidator.IsValid("user!").Should().BeFalse();
        UsernameFormatValidator.IsValid("user#").Should().BeFalse();
        UsernameFormatValidator.IsValid("user$").Should().BeFalse();
    }

    [Fact]
    public void IsValid_UnicodeCharacters_ReturnsFalse()
    {
        UsernameFormatValidator.IsValid("пользователь").Should().BeFalse();
    }

    [Fact]
    public void IsValid_Whitespace_ReturnsFalse()
    {
        UsernameFormatValidator.IsValid("   ").Should().BeFalse();
    }
}
