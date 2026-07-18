using BarkFluff.Users.Services;
using FluentAssertions;

namespace BarkFluff.Users.Tests.Services;

public class FidParserTests
{
    [Theory]
    [InlineData("@bob:node2.test", "bob", "node2.test", false)]
    [InlineData("bob:node2.test", "bob", "node2.test", false)]
    [InlineData("alice:NODE2.TEST", "alice", "node2.test", false)]         // servername lowercase canonicalisation
    [InlineData("ALICE:node2.test", "ALICE", "node2.test", false)]         // username case preserved
    [InlineData("bob", "bob", null, true)]                                 // FID без servername → локальный
    [InlineData("@bob", "bob", null, true)]                                // @ без servername → локальный
    [InlineData("ab:node2.test", null, null, false)]                       // короткий username (<3)
    [InlineData("in.valid:node2.test", null, null, false)]                 // точка не входит в regex username
    [InlineData("bob:192.168.1.1", null, null, false)]                     // IP-литерал как servername
    [InlineData("bob:localhost", null, null, false)]                       // localhost запрещён
    [InlineData("bob:", null, null, false)]                                // пустой servername
    [InlineData(":node2.test", null, null, false)]                         // пустой username
    [InlineData("", null, null, false)]
    [InlineData("   ", null, null, false)]
    [InlineData(null, null, null, false)]
    public void TryParse_Table(string? input, string? expectedUsername, string? expectedServer, bool expectLocal)
    {
        var ok = FidParser.TryParse(input, ownServerName: "node1.test", out var fid);

        if (expectedUsername is null)
        {
            ok.Should().BeFalse();
            fid.Should().BeNull();
            return;
        }

        ok.Should().BeTrue();
        fid.Should().NotBeNull();
        fid!.Username.Should().Be(expectedUsername);
        fid.IsLocal.Should().Be(expectLocal);
        if (expectLocal)
            fid.ServerName.Should().BeNull();
        else
            fid.ServerName.Should().Be(expectedServer);
    }

    [Fact]
    public void TryParse_OwnServerName_IsLocal()
    {
        var ok = FidParser.TryParse("@bob:node1.test", ownServerName: "node1.test", out var fid);

        ok.Should().BeTrue();
        fid!.IsLocal.Should().BeTrue();
        fid.Username.Should().Be("bob");
        fid.ServerName.Should().BeNull();
    }

    [Fact]
    public void TryParse_OwnServerName_CanonicalisedBeforeComparison()
    {
        // NODE1.TEST → node1.test сравнивается с ownServerName после нормализации обоих.
        var ok = FidParser.TryParse("@bob:NODE1.TEST", ownServerName: "node1.test", out var fid);

        ok.Should().BeTrue();
        fid!.IsLocal.Should().BeTrue();
    }

    [Fact]
    public void TryParse_PunycodeHomograph_Normalises()
    {
        // Кириллическая «а» в домене превращается в xn-- A-label — homograph-спуфинг ловится на уровне хранения.
        var ok = FidParser.TryParse("bob:bаrkfluff.com", ownServerName: "node1.test", out var fid);

        ok.Should().BeTrue();
        fid!.IsLocal.Should().BeFalse();
        fid.ServerName.Should().StartWith("xn--");
        fid.ServerName.Should().NotContain("а");
    }

    [Theory]
    [InlineData("@bob:node2.test", true)]
    [InlineData("bob:node2.test", true)]
    [InlineData("bob", false)]
    [InlineData("@bob", false)]
    [InlineData("bob:", false)]
    [InlineData(":node2.test", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("alice", false)]
    public void LooksLikeFid_Table(string? query, bool expected)
    {
        FidParser.LooksLikeFid(query).Should().Be(expected);
    }
}
