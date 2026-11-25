using BarkFluff.Proto.Files;
using BarkFluff.WebApi.Core;
using BarkFluff.WebApi.Core.Managers;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace BarkFluff.Client.WinUI.Tests;

public sealed class AttachmentImageTests
{
    [Theory]
    [InlineData(3000, false)]
    [InlineData(2500, true)]
    public async Task LargePhotoUsesUploadTargetOrRetainsJpegAtMinimumQuality(int dimension, bool exceedsTarget)
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var source = Path.Combine(directory.FullName, "photo.png");
            using var image = new Image<Rgb24>(dimension, dimension);
            var random = new Random(22);
            image.ProcessPixelRows(rows =>
            {
                for (var y = 0; y < rows.Height; y++)
                {
                    var row = rows.GetRowSpan(y);
                    if (exceedsTarget)
                    {
                        for (var x = 0; x < row.Length; x++)
                        {
                            var luminance = (byte)random.Next(256);
                            row[x] = new Rgb24(luminance, luminance, luminance);
                        }
                    }
                    else
                    {
                        random.NextBytes(System.Runtime.InteropServices.MemoryMarshal.AsBytes(row));
                    }
                }
            });
            image.SaveAsPng(source);

            var output = await WebApiFileManager.PrepareAttachmentAsync(source, UploadFileType.MessageAttachmentImage,
                directory.FullName, CancellationToken.None);

            Assert.NotEqual(source, output);
            var result = Image.Identify(output);
            Assert.Equal(new Size(2500, 2500), result.Size);
            var length = new FileInfo(output).Length;
            if (exceedsTarget)
            {
                Assert.True(length > 2 * 1024 * 1024);
                Assert.Equal(50, result.Metadata.GetJpegMetadata().Quality);

                var legacyOutput = await ImageProcessor.ProcessImageForUploadAsync(source, directory.FullName);
                Assert.NotEqual(source, legacyOutput);
                Assert.Equal(90, Image.Identify(legacyOutput).Metadata.GetJpegMetadata().Quality);
            }
            else
            {
                Assert.InRange(length, 1, 2 * 1024 * 1024);
                Assert.InRange(result.Metadata.GetJpegMetadata().Quality, 50, 80);
            }
        }
        finally
        {
            foreach (var file in directory.EnumerateFiles())
                file.Delete();
            directory.Delete();
        }
    }

    [Fact]
    public async Task TransparentPngHasWhiteBackgroundInJpeg()
    {
        var source = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.png");
        var output = source;
        try
        {
            using var image = new Image<Rgba32>(64, 32);
            image.ProcessPixelRows(rows =>
            {
                for (var y = 0; y < rows.Height; y++)
                    rows.GetRowSpan(y)[32..].Fill(new Rgba32(0, 0, 0, 255));
            });
            image.SaveAsPng(source);

            output = await WebApiFileManager.PrepareAttachmentAsync(source, UploadFileType.MessageAttachmentImage,
                Path.GetTempPath(), CancellationToken.None);

            Assert.NotEqual(source, output);
            using var jpeg = Image.Load<Rgb24>(output);
            Assert.Equal(90, jpeg.Metadata.GetJpegMetadata().Quality);
            Assert.Equal(new Rgb24(255, 255, 255), jpeg[8, 8]);
            Assert.Equal(new Rgb24(0, 0, 0), jpeg[56, 8]);
        }
        finally
        {
            File.Delete(output);
            File.Delete(source);
        }
    }

    [Theory]
    [InlineData(".gif", UploadFileType.MessageAttachmentGif, true)]
    [InlineData(".gif", UploadFileType.MessageAttachmentImage, false)]
    [InlineData(".webp", UploadFileType.MessageAttachmentImage, true)]
    [InlineData(".png", UploadFileType.MessageAttachmentImage, true)]
    [InlineData(".png", UploadFileType.MessageAttachmentDocument, false)]
    public async Task AnimationsAndDocumentsKeepOriginalBytes(string extension, UploadFileType type, bool animated)
    {
        var source = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}{extension}");
        var output = source;
        try
        {
            using var image = new Image<Rgba32>(20, 20, Color.White);
            if (animated)
            {
                using var nextFrame = new Image<Rgba32>(20, 20, Color.Black);
                image.Frames.AddFrame(nextFrame.Frames.RootFrame);
                if (extension == ".png")
                    image.Metadata.GetPngMetadata().AnimateRootFrame = true;
            }
            image.Save(source);
            using var decoded = Image.Load(source);
            Assert.Equal(animated ? 2 : 1, decoded.Frames.Count);
            var original = File.ReadAllBytes(source);

            output = await WebApiFileManager.PrepareAttachmentAsync(source, type, Path.GetTempPath(),
                CancellationToken.None);

            Assert.Equal(source, output);
            Assert.Equal(original, File.ReadAllBytes(output));
        }
        finally
        {
            File.Delete(output);
            File.Delete(source);
        }
    }

    [Fact]
    public async Task GifWithPngExtensionKeepsOriginalBytes()
    {
        var source = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.png");
        var output = source;
        try
        {
            using var image = new Image<Rgba32>(20, 20, Color.White);
            image.Save(source, new GifEncoder());
            var original = File.ReadAllBytes(source);

            output = await WebApiFileManager.PrepareAttachmentAsync(source, UploadFileType.MessageAttachmentImage,
                Path.GetTempPath(), CancellationToken.None);

            Assert.Equal(source, output);
            Assert.Equal(original, File.ReadAllBytes(output));
        }
        finally
        {
            File.Delete(output);
            File.Delete(source);
        }
    }

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
        var attachment = source;
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
            attachment = await WebApiFileManager.PrepareAttachmentAsync(source, UploadFileType.MessageAttachmentImage,
                Path.GetTempPath(), CancellationToken.None);
            Assert.NotEqual(source, attachment);
            foreach (var converted in new[] { output, attachment })
            {
                using var result = Image.Load<Rgb24>(converted);
                Assert.Equal(expectedWidth, result.Width);
                Assert.Equal(expectedHeight, result.Height);
                var x = result.Width / 4;
                var y = result.Height / 4;
                Assert.True(result[right ? 3 * x : x, bottom ? 3 * y : y].R > 240);
                Assert.True(result[right ? x : 3 * x, bottom ? y : 3 * y].R < 15);
                Assert.False(result.Metadata.ExifProfile?.TryGetValue(ExifTag.Orientation, out var tag) == true && tag.Value != 1);
            }
        }
        finally
        {
            File.Delete(source);
            File.Delete(output);
            File.Delete(attachment);
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
