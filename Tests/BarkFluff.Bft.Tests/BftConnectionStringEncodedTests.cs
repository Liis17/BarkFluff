using BarkFluff.Bft;
using Xunit;

namespace BarkFluff.Bft.Tests;

public sealed class BftConnectionStringEncodedTests
{
    private const string Classic = "bft://B6N8vBQgk8i3VdwbEOhstCY3StFqqFPtC9_AsrhtHHw.ICEiIyQlJicoKSorLC0uLzAxMjM0NTY3ODk6Ozw9Pj8@proxy.example.org:8443?token=t0ken%2F%2B%201&name=%D0%9F%D1%91%D1%81%20%F0%9F%8C%90";
    private const string Encoded = "bft://QjZOOHZCUWdrOGkzVmR3YkVPaHN0Q1kzU3RGcXFGUHRDOV9Bc3JodEhIdy5JQ0VpSXlRbEppY29LU29yTEMwdUx6QXhNak0wTlRZM09EazZPenc5UGo4QHByb3h5LmV4YW1wbGUub3JnOjg0NDM_dG9rZW49dDBrZW4lMkYlMkIlMjAxJm5hbWU9JUQwJTlGJUQxJTkxJUQxJTgxJTIwJUYwJTlGJThDJTkw";

    [Fact]
    public void EncodedLink_RoundTripsWithClassicForm()
    {
        var parsed = BftConnectionString.Parse(Encoded);
        Assert.Equal("proxy.example.org", parsed.Host);
        Assert.Equal(8443, parsed.Port);
        Assert.Equal("t0ken/+ 1", parsed.Token);
        Assert.Equal("Пёс 🌐", parsed.Name);
        Assert.Equal(Classic, BftConnectionString.Format(parsed));
        Assert.Equal(Encoded, BftConnectionString.FormatEncoded(BftConnectionString.Parse(Classic)));
    }

    [Theory]
    [InlineData("bft://")]
    [InlineData("bft://QUJD")] // "ABC": no key pair or authority
    [InlineData("bft://YmZ0Oi8vQjZOOHZCUWdrOGkzVmR3YkVPaHN0Q1kzU3RGcXFGUHRDOV9Bc3JodEhIdy5JQ0VpSXlRbEppY29LU29yTEMwdUx6QXhNak0wTlRZM09EazZPenc5UGo4QHByb3h5LmV4YW1wbGUub3JnOjg0NDM_dG9rZW49dDBrZW4lMkYlMkIlMjAxJm5hbWU9JUQwJTlGJUQxJTkxJUQxJTgxJTIwJUYwJTlGJThDJTkw")] // nested bft://
    [InlineData("bft://A")] // invalid base64 length
    [InlineData("bft://_w")] // 0xFF is not UTF-8
    public void InvalidEncodedLinks_AreRejected(string link)
    {
        Assert.Throws<FormatException>(() => BftConnectionString.Parse(link));
    }

    [Fact]
    public void OverlongEncodedLink_IsRejected()
    {
        Assert.Throws<FormatException>(() => BftConnectionString.Parse("bft://" + new string('A', 4100)));
    }
}
