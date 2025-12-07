using System.Drawing;
using System.Drawing.Imaging;

namespace BarkFluff.WebApi.Core
{
    /// <summary>
    /// Provides image processing utilities for optimizing images before upload
    /// </summary>
    public static class ImageProcessor
    {
        private const int JPEG_QUALITY = 85;
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
        /// Converts an image to JPEG format with specified quality
        /// </summary>
        /// <param name="sourcePath">Path to source image</param>
        /// <param name="outputPath">Path where converted image will be saved</param>
        /// <param name="quality">JPEG quality (0-100), default is 85</param>
        /// <returns>True if conversion succeeded, false otherwise</returns>
        public static async Task<bool> ConvertToJpegAsync(string sourcePath, string outputPath, int quality = JPEG_QUALITY)
        {
            try
            {
                return await Task.Run(() =>
                {
                    using (var image = Image.FromFile(sourcePath))
                    {
                        // Get JPEG codec
                        var jpegCodec = GetEncoderInfo("image/jpeg");
                        if (jpegCodec == null)
                            return false;

                        // Set quality parameter
                        var encoderParameters = new EncoderParameters(1);
                        encoderParameters.Param[0] = new EncoderParameter(Encoder.Quality, quality);

                        // Save as JPEG
                        image.Save(outputPath, jpegCodec, encoderParameters);
                        return true;
                    }
                });
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Processes an image file for upload: converts to JPEG with compression if needed
        /// </summary>
        /// <param name="filePath">Path to the file to process</param>
        /// <returns>Path to processed file (may be same as input if no conversion needed)</returns>
        public static async Task<string> ProcessImageForUploadAsync(string filePath)
        {
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
                    try { File.Delete(tempPath); } catch { }
                }
                return filePath;
            }

            return tempPath;
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
