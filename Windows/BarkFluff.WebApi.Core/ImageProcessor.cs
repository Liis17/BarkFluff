using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

using System.Drawing.Imaging;
using RotateFlipType = System.Drawing.RotateFlipType;

namespace BarkFluff.WebApi.Core
{
    public static class ImageProcessor
    {
        private const int JPEG_QUALITY = 90;
        private const int MAX_DIMENSION = 2500;
        private const int ATTACHMENT_TARGET_BYTES = 2 * 1024 * 1024;
        private const long MAX_IMAGE_SIZE_BYTES = 50 * 1024 * 1024; // 50 MB limit

        private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".bmp", ".webp"
        };
        private static readonly HashSet<string> GifExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".gif"
        };

        /// <summary>
        /// Checks if the file should be processed (converted to JPEG)
        /// </summary>
        /// <param name="filePath">Path to the file</param>
        /// <returns>True if file should be converted, false otherwise</returns>
        public static bool ShouldConvertToJpeg(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                return false;

            var extension = Path.GetExtension(filePath);

            // Don't convert GIF files to preserve animation
            if (GifExtensions.Contains(extension))
                return false;

            // Convert other image formats
            return ImageExtensions.Contains(extension);
        }

        /// <summary>
        /// Converts an image to JPEG format with specified quality using ImageSharp
        /// </summary>
        /// <param name="sourcePath">Path to source image</param>
        /// <param name="outputPath">Path where converted image will be saved</param>
        /// <param name="quality">JPEG quality (0-100), default is 85</param>
        /// <returns>True if conversion succeeded, false otherwise</returns>
        public static async Task<bool> ConvertToJpegAsync(string sourcePath, string outputPath, int quality = JPEG_QUALITY)
        {
            try
            {
                // Check file size before loading
                var fileInfo = new FileInfo(sourcePath);
                if (fileInfo.Length > MAX_IMAGE_SIZE_BYTES)
                {
                    System.Diagnostics.Debug.WriteLine($"ImageProcessor: Image too large ({fileInfo.Length} bytes), skipping conversion");
                    return false;
                }

                var extension = Path.GetExtension(sourcePath).ToLowerInvariant();

                // Use ImageSharp for WebP and other formats
                if (extension == ".webp" || extension == ".png" || extension == ".bmp")
                {
                    return await ConvertToJpegWithImageSharpAsync(sourcePath, outputPath, quality);
                }

                // Use System.Drawing.Common for JPEG (already in correct format, just optimize)
                return await ConvertToJpegWithSystemDrawingAsync(sourcePath, outputPath, quality);
            }
            catch (Exception ex)
            {
                // Log error but don't throw - caller will handle by using original file
                System.Diagnostics.Debug.WriteLine($"ImageProcessor: Failed to convert image: {ex.GetType().Name}");
                return false;
            }
        }

        /// <summary>
        /// Converts an image to JPEG using ImageSharp library (supports WebP and modern formats)
        /// </summary>
        private static async Task<bool> ConvertToJpegWithImageSharpAsync(string sourcePath, string outputPath, int quality)
        {
            return await Task.Run(() =>
            {
                try
                {
                    using (var image = SixLabors.ImageSharp.Image.Load(sourcePath))
                    {
                        image.Mutate(context => context.AutoOrient());
                        ResizeIfNeeded(image);

                        var encoder = new JpegEncoder
                        {
                            Quality = quality,
                            ColorType = JpegEncodingColor.YCbCrRatio420,
                            Interleaved = true
                        };

                        image.Save(outputPath, encoder);
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"ImageProcessor: ImageSharp conversion failed: {ex.GetType().Name}");
                    return false;
                }
            });
        }

        /// <summary>
        /// Converts an image to JPEG using System.Drawing.Common (fallback for legacy formats)
        /// </summary>
        private static async Task<bool> ConvertToJpegWithSystemDrawingAsync(string sourcePath, string outputPath, int quality)
        {
            return await Task.Run(() =>
            {
                try
                {
                    using (var original = System.Drawing.Image.FromFile(sourcePath))
                    {
                        ApplyExifOrientation(original);
                        var imageToSave = original;
                        bool resized = false;

                        if (original.Width > MAX_DIMENSION || original.Height > MAX_DIMENSION)
                        {
                            double scale = Math.Min((double)MAX_DIMENSION / original.Width, (double)MAX_DIMENSION / original.Height);
                            int newWidth = (int)Math.Round(original.Width * scale);
                            int newHeight = (int)Math.Round(original.Height * scale);

                            var bitmap = new System.Drawing.Bitmap(newWidth, newHeight);
                            using (var g = System.Drawing.Graphics.FromImage(bitmap))
                            {
                                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                                g.DrawImage(original, 0, 0, newWidth, newHeight);
                            }
                            imageToSave = bitmap;
                            resized = true;
                        }

                        // Get JPEG codec
                        var jpegCodec = GetEncoderInfo("image/jpeg");
                        if (jpegCodec == null)
                        {
                            if (resized) imageToSave.Dispose();
                            return false;
                        }

                        // Set quality parameter
                        var encoderParameters = new EncoderParameters(1);
                        encoderParameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, quality);

                        // Save as JPEG
                        imageToSave.Save(outputPath, jpegCodec, encoderParameters);
                        if (resized) imageToSave.Dispose();
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"ImageProcessor: System.Drawing conversion failed: {ex.GetType().Name}");
                    return false;
                }
            });
        }

        private static void ApplyExifOrientation(System.Drawing.Image image)
        {
            const int orientationId = 0x0112;
            if (!image.PropertyIdList.Contains(orientationId))
                return;

            if (image.GetPropertyItem(orientationId)?.Value is not { Length: >= 2 } value)
                return;

            var transform = value[0] switch
            {
                2 => RotateFlipType.RotateNoneFlipX,
                3 => RotateFlipType.Rotate180FlipNone,
                4 => RotateFlipType.Rotate180FlipX,
                5 => RotateFlipType.Rotate90FlipX,
                6 => RotateFlipType.Rotate90FlipNone,
                7 => RotateFlipType.Rotate270FlipX,
                8 => RotateFlipType.Rotate270FlipNone,
                _ => RotateFlipType.RotateNoneFlipNone
            };

            image.RemovePropertyItem(orientationId);
            if (transform != RotateFlipType.RotateNoneFlipNone)
                image.RotateFlip(transform);
        }

        /// <summary>
        /// Processes an image file for upload: converts to JPEG with compression if needed
        /// </summary>
        /// <param name="filePath">Path to the file to process</param>
        /// <returns>Path to processed file (may be same as input if no conversion needed)</returns>
        public static Task<string> ProcessImageForUploadAsync(string filePath) =>
            ProcessImageForUploadAsync(filePath, Path.GetTempPath());

        internal static async Task<string> ProcessImageForUploadAsync(string filePath, string temporaryDirectory,
            bool optimizeAttachment = false, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentNullException(nameof(filePath), "File path cannot be null or empty");

            if (!File.Exists(filePath))
                throw new FileNotFoundException("Source file not found", filePath);

            // Check if conversion is needed
            if (!ShouldConvertToJpeg(filePath))
                return filePath;

            // Create temp file for converted image
            var tempPath = Path.Combine(temporaryDirectory, $"converted_{Guid.NewGuid()}.jpg");

            var success = false;
            try
            {
                success = optimizeAttachment
                    ? await Task.Run(() => OptimizeAttachmentAsync(filePath, tempPath, cancellationToken), cancellationToken)
                        .ConfigureAwait(false)
                    : await ConvertToJpegAsync(filePath, tempPath);
                return success ? tempPath : filePath;
            }
            finally
            {
                if (!success && File.Exists(tempPath))
                {
                    try
                    {
                        File.Delete(tempPath);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"ImageProcessor: Failed to delete temp file: {ex.GetType().Name}");
                    }
                }
            }
        }

        private static async Task<bool> OptimizeAttachmentAsync(string sourcePath, string outputPath,
            CancellationToken cancellationToken)
        {
            try
            {
                if (new FileInfo(sourcePath).Length > MAX_IMAGE_SIZE_BYTES)
                    return false;

                // ImageSharp 3.1.12 прекращает чтение APNG перед кадром с номером MaxFrames.
                // Лимит 3 позволяет увидеть второй кадр и сохранить анимацию.
                using var image = await Image.LoadAsync(new DecoderOptions { MaxFrames = 3 }, sourcePath, cancellationToken)
                    .ConfigureAwait(false);
                if (image.Frames.Count > 1 || image.Metadata.DecodedImageFormat == GifFormat.Instance)
                    return false;

                image.Mutate(context => context.AutoOrient());
                ResizeIfNeeded(image);
                image.Mutate(context => context.BackgroundColor(Color.White));

                await using var output = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write);
                for (var quality = JPEG_QUALITY; quality >= 50; quality -= 10)
                {
                    output.SetLength(0);
                    output.Position = 0;
                    await image.SaveAsync(output, new JpegEncoder
                    {
                        Quality = quality,
                        ColorType = JpegEncodingColor.YCbCrRatio420,
                        Interleaved = true
                    }, cancellationToken).ConfigureAwait(false);

                    if (output.Length <= ATTACHMENT_TARGET_BYTES)
                        break;
                }

                // Как на macOS, после последней попытки оставляем JPEG с качеством 50.
                cancellationToken.ThrowIfCancellationRequested();
                return true;
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                System.Diagnostics.Debug.WriteLine($"ImageProcessor: JPEG attachment conversion failed: {ex.GetType().Name}");
                return false;
            }
        }

        /// <summary>
        /// Resizes an ImageSharp image so its longest side does not exceed MAX_DIMENSION, preserving aspect ratio.
        /// </summary>
        private static void ResizeIfNeeded(SixLabors.ImageSharp.Image image)
        {
            if (image.Width <= MAX_DIMENSION && image.Height <= MAX_DIMENSION)
                return;

            double scale = Math.Min((double)MAX_DIMENSION / image.Width, (double)MAX_DIMENSION / image.Height);
            int newWidth = (int)Math.Round(image.Width * scale);
            int newHeight = (int)Math.Round(image.Height * scale);

            image.Mutate(x => x.Resize(newWidth, newHeight));
        }

        /// <summary>
        /// Gets the ImageCodecInfo for a specific MIME type
        /// </summary>
        private static ImageCodecInfo? GetEncoderInfo(string mimeType)
        {
            var encoders = ImageCodecInfo.GetImageEncoders();
            return encoders.FirstOrDefault(encoder => encoder.MimeType == mimeType);
        }
    }
}
