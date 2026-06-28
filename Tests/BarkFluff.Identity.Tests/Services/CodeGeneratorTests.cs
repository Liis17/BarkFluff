using BarkFluff.Identity.Services;

using Xunit;

namespace BarkFluff.Identity.Tests.Services;

public class CodeGeneratorTests
{
    [Fact]
    public void GenerateDigitalCode_ReturnsCorrectLength()
    {
        var code = CodeGenerator.GenerateDigitalCode(6);

        Assert.Equal(6, code.Length);
    }

    [Fact]
    public void GenerateDigitalCode_AllDigits()
    {
        var code = CodeGenerator.GenerateDigitalCode(10);

        Assert.True(code.All(char.IsDigit));
    }

    [Fact]
    public void GenerateDigitalCode_Length1_Works()
    {
        var code = CodeGenerator.GenerateDigitalCode(1);

        Assert.Equal(1, code.Length);
        Assert.True(char.IsDigit(code[0]));
    }

    [Fact]
    public void GenerateDigitalCode_Length20_Works()
    {
        var code = CodeGenerator.GenerateDigitalCode(20);

        Assert.Equal(20, code.Length);
    }

    [Fact]
    public void GenerateDigitalCode_ZeroLength_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => CodeGenerator.GenerateDigitalCode(0));
    }

    [Fact]
    public void GenerateDigitalCode_NegativeLength_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => CodeGenerator.GenerateDigitalCode(-1));
    }

    [Fact]
    public void GenerateDigitalCode_MultipleCalls_MostlyUnique()
    {
        var codes = new HashSet<string>();
        for (int i = 0; i < 100; i++)
        {
            codes.Add(CodeGenerator.GenerateDigitalCode(6));
        }

        Assert.True(codes.Count > 90);
    }
}
