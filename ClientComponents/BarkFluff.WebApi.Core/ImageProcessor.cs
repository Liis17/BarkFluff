using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;

namespace BarkFluff.WebApi.Core
{
    /// <summary>
    /// Provides image processing utilities for optimizing images before upload.
    /// 
    /// NOTE: This currently uses System.Drawing.Common which has limitations:
    /// - Not recommended for server-side applications
    /// - May have thread safety issues
    /// - Only supported on Windows (with limited Linux support)
    /// 
    /// For future improvements, consider migrating to:
    /// - ImageSharp (cross-platform, modern API)
    /// - SkiaSharp (cross-platform, performant)
    /// 
    /// However, for a WPF client application, System.Drawing.Common is acceptable.
    /// </summary>
    public static class ImageProcessor
    {
        private const int JPEG_QUALITY = 85;
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
                // Check file size before loading
                var fileInfo = new FileInfo(sourcePath);
                if (fileInfo.Length > MAX_IMAGE_SIZE_BYTES)
                {
                    System.Diagnostics.Debug.WriteLine($"ImageProcessor: Image too large ({fileInfo.Length} bytes), skipping conversion");
                    return false;
                }

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
            catch (Exception ex)
            {
                // Log error but don't throw - caller will handle by using original file
                System.Diagnostics.Debug.WriteLine($"ImageProcessor: Failed to convert image: {ex.Message}");
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
        /// Gets the ImageCodecInfo for a specific MIME type
        /// </summary>
        private static ImageCodecInfo? GetEncoderInfo(string mimeType)
        {
            var encoders = ImageCodecInfo.GetImageEncoders();
            return encoders.FirstOrDefault(encoder => encoder.MimeType == mimeType);
        }
    }
}
