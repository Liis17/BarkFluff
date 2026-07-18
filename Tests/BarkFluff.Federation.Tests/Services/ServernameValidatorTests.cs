using System.Net;

using BarkFluff.Federation.Services;

using FluentAssertions;

namespace BarkFluff.Federation.Tests.Services;

// Таблица кейсов из docs/rearch/phase-1/step-1.4-discovery-knownservers.md, Изменение 1.
// Резолв DNS ("hostname → 10.x.x.x") в реальных unit-тестах не делаем (сеть/флаки) — вместо этого
// IsPrivateOrReserved проверяется напрямую на литеральных IP, представляющих результат резолва.
public class ServernameValidatorTests
{
    [Theory]
    [InlineData("192.168.1.1", false)] // IP-литерал — синтаксически невалиден как servername
    [InlineData("2001:db8::1", false)]
    public void TryNormalizeSyntax_RejectsIpLiterals(string input, bool expected)
    {
        ServernameValidator.TryNormalizeSyntax(input, out _).Should().Be(expected);
    }

    [Fact]
    public void TryNormalizeSyntax_RejectsLocalhost()
    {
        ServernameValidator.TryNormalizeSyntax("localhost", out _).Should().BeFalse();
    }

    [Fact]
    public void TryNormalizeSyntax_AcceptsValidPublicHostname()
    {
        ServernameValidator.TryNormalizeSyntax("chat.example.org", out var normalized).Should().BeTrue();
        normalized.Should().Be("chat.example.org");
    }

    [Fact]
    public void TryNormalizeSyntax_PunycodeHomograph_NormalizesToALabel()
    {
        // Кириллическая "а" (U+0430) вместо латинской в "example" — гомограф-атака.
        var homograph = "exаmple.org";

        ServernameValidator.TryNormalizeSyntax(homograph, out var normalized).Should().BeTrue();
        normalized.Should().StartWith("xn--"); // punycode A-label, не совпадает с "example.org"
        normalized.Should().NotBe("example.org");
    }

    [Fact]
    public void TryNormalizeSyntax_RejectsEmptyOrWhitespace()
    {
        ServernameValidator.TryNormalizeSyntax("", out _).Should().BeFalse();
        ServernameValidator.TryNormalizeSyntax("   ", out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("https", false, true)]
    [InlineData("grpc", false, true)]
    [InlineData("http", false, false)] // http:// для не-manual — запрещено
    [InlineData("http", true, true)]   // http:// для manual — допустимо
    public void IsSchemeAllowed_EnforcesManualException(string scheme, bool isManual, bool expected)
    {
        ServernameValidator.IsSchemeAllowed(scheme, isManual).Should().Be(expected);
    }

    [Theory]
    [InlineData("10.0.0.1", true)]
    [InlineData("10.255.255.255", true)]
    [InlineData("172.16.0.1", true)]
    [InlineData("172.31.255.255", true)]
    [InlineData("172.32.0.1", false)]
    [InlineData("192.168.0.1", true)]
    [InlineData("127.0.0.1", true)]
    [InlineData("169.254.1.1", true)]
    [InlineData("0.0.0.0", true)]
    [InlineData("224.0.0.1", true)]
    [InlineData("255.255.255.255", true)]
    [InlineData("8.8.8.8", false)]
    [InlineData("93.184.216.34", false)] // публичный (example.com)
    [InlineData("::1", true)]
    [InlineData("fc00::1", true)]
    [InlineData("fe80::1", true)]
    [InlineData("2001:4860:4860::8888", false)] // публичный (Google DNS v6)
    public void IsPrivateOrReserved_MatchesExpectedRanges(string ipLiteral, bool expected)
    {
        var ip = IPAddress.Parse(ipLiteral);
        ServernameValidator.IsPrivateOrReserved(ip).Should().Be(expected);
    }
}
