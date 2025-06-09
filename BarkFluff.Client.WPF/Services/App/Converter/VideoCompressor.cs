using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

using Windows.Media.Transcoding;
using Windows.Storage;

namespace BarkFluff.Client.WPF.Services.App.Converter
{
    public class VideoCompressor
    {
        public static async Task CompressAsync(string inputPath, string outputPath)
        {
            var ffmpegPath = @"C:\ProgramData\ffmpeg\ffmpeg.exe"; // путь до ffmpeg.exe

            var args = $"-i \"{inputPath}\" -vcodec libx264 -crf 28 \"{outputPath}\"";

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = args,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                }
            };

            process.Start();

            // можешь читать вывод, если нужно:
            string output = await process.StandardError.ReadToEndAsync();

            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                throw new Exception("FFmpeg failed:\n" + output);
            }
        }
    }
}
