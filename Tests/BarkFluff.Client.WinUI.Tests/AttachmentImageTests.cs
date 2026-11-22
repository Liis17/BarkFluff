using BarkFluff.WebApi.Core;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace BarkFluff.Client.WinUI.Tests;

public sealed class AttachmentImageTests
{
    [Theory]
    [InlineData(".jpg", 3, 3000, 1500, 2500, 1250, true, true)]
    [InlineData(".jpg", 8, 3000, 1500, 1250, 2500, false, true)]
    [InlineData(".jpg", 8, 120, 60, 60, 120, false, true)]
    [InlineData(".jpg", null, 120, 60, 120, 60, false, false)]
    [InlineData(".png", 8, 120, 60, 60, 120, false, true)]
    [InlineData(".webp", 3, 120, 60, 120, 60, true, true)]
    [InlineData(".bmp", null, 120, 60, 120, 60, false, false)]
    public async Task ConversionPreservesExpectedPixelOrientation(string extension, int? orientation,
        int width, int height, int expectedWidth, int expectedHeight, bool right, bool bottom)
    {
        var source = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}{extension}");
        var output = Path.ChangeExtension(source, ".converted.jpg");
        try
        {
            using var image = new Image<Rgb24>(width, height);
            image.ProcessPixelRows(rows =>
            {
                for (var y = 0; y < height / 2; y++)
                    rows.GetRowSpan(y)[..(width / 2)].Fill(new Rgb24(255, 255, 255));
            });
            if (orientation is { } value)
            {
                image.Metadata.ExifProfile = new ExifProfile();
                image.Metadata.ExifProfile.SetValue(ExifTag.Orientation, (ushort)value);
            }
            image.Save(source);

            Assert.True(await ImageProcessor.ConvertToJpegAsync(source, output));
            using var result = Image.Load<Rgb24>(output);
            Assert.Equal(expectedWidth, result.Width);
            Assert.Equal(expectedHeight, result.Height);
            var x = result.Width / 4;
            var y = result.Height / 4;
            Assert.True(result[right ? 3 * x : x, bottom ? 3 * y : y].R > 240);
            Assert.True(result[right ? x : 3 * x, bottom ? y : 3 * y].R < 15);
            Assert.False(result.Metadata.ExifProfile?.TryGetValue(ExifTag.Orientation, out var tag) == true && tag.Value != 1);
        }
        finally
        {
            File.Delete(source);
            File.Delete(output);
        }
    }

    [Fact]
    public async Task ResizedPhoneJpegAppliesExifOrientation()
    {
        var source = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.jpg");
        var output = Path.ChangeExtension(source, ".converted.jpg");
        try
        {
            using var image = new Image<Rgb24>(3000, 1500);
            image.Metadata.ExifProfile = new ExifProfile();
            image.Metadata.ExifProfile.SetValue(ExifTag.Orientation, (ushort)6);
            image.SaveAsJpeg(source);

            Assert.True(await ImageProcessor.ConvertToJpegAsync(source, output));
            var result = Image.Identify(output);
            Assert.Equal(1250, result.Width);
            Assert.Equal(2500, result.Height);
        }
        finally
        {
            File.Delete(source);
            File.Delete(output);
        }
    }
}
