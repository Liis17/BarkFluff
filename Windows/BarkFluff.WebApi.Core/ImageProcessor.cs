using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

using System.Drawing.Imaging;

namespace BarkFluff.WebApi.Core
{
    public static class ImageProcessor
    {
        private const int JPEG_QUALITY = 90;
        private const int MAX_DIMENSION = 2500;
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
                System.Diagnostics.Debug.WriteLine($"ImageProcessor: Failed to convert image: {ex.Message}");
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
                    System.Diagnostics.Debug.WriteLine($"ImageProcessor: ImageSharp conversion failed: {ex.Message}");
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
                    System.Diagnostics.Debug.WriteLine($"ImageProcessor: System.Drawing conversion failed: {ex.Message}");
                    return false;
                }
            });
        }

        /// <summary>
        /// Processes an image file for upload: converts to JPEG with compression if needed
        /// </summary>
        /// <param name="filePath">Path to the file to process</param>
        /// <returns>Path to processed file (may be same as input if no conversion needed)</returns>
        public static async Task<string> ProcessImageForUploadAsync(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentNullException(nameof(filePath), "File path cannot be null or empty");

            if (!File.Exists(filePath))
                throw new FileNotFoundException("Source file not found", filePath);

            // Check if conversion is needed
            if (!ShouldConvertToJpeg(filePath))
                return filePath;

            // Create temp file for converted image
            var tempPath = Path.Combine(Path.GetTempPath(), $"converted_{Guid.NewGuid()}.jpg");

            // Convert to JPEG
            var success = await ConvertToJpegAsync(filePath, tempPath);

            if (!success)
            {
                // If conversion failed, return original file
                if (File.Exists(tempPath))
                {
                    try
                    {
                        File.Delete(tempPath);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"ImageProcessor: Failed to delete temp file: {ex.Message}");
                    }
                }
                return filePath;
            }

            return tempPath;
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
