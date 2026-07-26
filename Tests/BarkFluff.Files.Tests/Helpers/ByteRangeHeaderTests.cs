using BarkFluff.Files.Helpers;

namespace BarkFluff.Files.Tests.Helpers;

/// <summary>
/// Разбор заголовка Range для fed-скачивания (этап 3.3). Контракт результата:
/// From inclusive, To exclusive.
/// </summary>
public class ByteRangeHeaderTests
{
    private const long TotalSize = 1000;

    [Fact]
    public void NoHeader_MeansWholeFile()
    {
        ByteRangeHeader.TryParse(null, TotalSize, out _).Should().Be(ByteRangeHeader.Status.NoRange);
        ByteRangeHeader.TryParse("", TotalSize, out _).Should().Be(ByteRangeHeader.Status.NoRange);
    }

    [Fact]
    public void ClosedRange_IsConvertedToExclusiveUpperBound()
    {
        var status = ByteRangeHeader.TryParse("bytes=100-199", TotalSize, out var range);

        status.Should().Be(ByteRangeHeader.Status.Satisfiable);
        range.From.Should().Be(100);
        range.To.Should().Be(200);
        range.Length.Should().Be(100);
    }

    [Fact]
    public void OpenEndedRange_RunsToEndOfFile()
    {
        var status = ByteRangeHeader.TryParse("bytes=900-", TotalSize, out var range);

        status.Should().Be(ByteRangeHeader.Status.Satisfiable);
        range.From.Should().Be(900);
        range.To.Should().Be(TotalSize);
    }

    [Fact]
    public void SuffixRange_TakesLastBytes()
    {
        var status = ByteRangeHeader.TryParse("bytes=-100", TotalSize, out var range);

        status.Should().Be(ByteRangeHeader.Status.Satisfiable);
        range.From.Should().Be(900);
        range.To.Should().Be(TotalSize);
    }

    [Fact]
    public void SuffixLongerThanFile_ClampsToStart()
    {
        var status = ByteRangeHeader.TryParse("bytes=-5000", TotalSize, out var range);

        status.Should().Be(ByteRangeHeader.Status.Satisfiable);
        range.From.Should().Be(0);
        range.To.Should().Be(TotalSize);
    }

    [Fact]
    public void UpperBoundBeyondFile_IsClamped()
    {
        var status = ByteRangeHeader.TryParse("bytes=900-99999", TotalSize, out var range);

        status.Should().Be(ByteRangeHeader.Status.Satisfiable);
        range.To.Should().Be(TotalSize);
    }

    [Fact]
    public void StartBeyondFile_IsUnsatisfiable()
    {
        ByteRangeHeader.TryParse("bytes=1000-1100", TotalSize, out _)
            .Should().Be(ByteRangeHeader.Status.Unsatisfiable);
    }

    [Theory]
    [InlineData("items=0-100")]   // не bytes
    [InlineData("bytes=abc-def")] // не числа
    [InlineData("bytes=100")]     // нет дефиса
    [InlineData("bytes=200-100")] // конец раньше начала
    [InlineData("bytes=0-50,60-90")] // множественные диапазоны не поддерживаем
    public void MalformedOrUnsupported_FallsBackToWholeFile(string header)
    {
        // RFC 9110 разрешает сервер игнорировать некорректный Range — это лучше, чем 416
        // на каждую опечатку клиента.
        ByteRangeHeader.TryParse(header, TotalSize, out _).Should().Be(ByteRangeHeader.Status.NoRange);
    }

    [Fact]
    public void UnknownTotalSize_MeansWholeFile()
    {
        // Снапшота размера нет — диапазон посчитать не от чего.
        ByteRangeHeader.TryParse("bytes=0-100", 0, out _).Should().Be(ByteRangeHeader.Status.NoRange);
    }

    [Fact]
    public void SingleByteRange_IsSatisfiable()
    {
        var status = ByteRangeHeader.TryParse("bytes=0-0", TotalSize, out var range);

        status.Should().Be(ByteRangeHeader.Status.Satisfiable);
        range.From.Should().Be(0);
        range.To.Should().Be(1);
        range.Length.Should().Be(1);
    }
}
