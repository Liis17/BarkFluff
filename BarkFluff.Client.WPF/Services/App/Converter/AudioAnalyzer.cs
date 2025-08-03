using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace BarkFluff.Client.WPF.Services.App.Converter
{
    public class AudioAnalyzer
    {
        public static List<int> AnalyzeLoudness(string oggFilePath)
        {
            var result = new List<int>();

            double duration = GetAudioDuration(oggFilePath);
            if (duration <= 0)
                throw new Exception("Не удалось получить длительность аудио");

            double segmentDuration = duration / 40.0;

            var loudnessValues = new List<double>();

            for (int i = 0; i < 40; i++)
            {
                double start = i * segmentDuration;
                double end = segmentDuration;
                string args = $"-ss {start.ToString(CultureInfo.InvariantCulture)} -t {end.ToString(CultureInfo.InvariantCulture)} " +
                              $"-i \"{oggFilePath}\" -af volumedetect -f null NUL";

                var output = RunFfmpeg(args);

                double rms = ParseRmsFromOutput(output);
                loudnessValues.Add(rms);
            }

            double min = double.MaxValue;
            double max = double.MinValue;
            foreach (var val in loudnessValues)
            {
                if (val < min) min = val;
                if (val > max) max = val;
            }

            foreach (var val in loudnessValues)
            {
                int normalized = (int)Math.Round(Normalize(val, min, max, 1, 100));
                result.Add(normalized);
            }

            return result;
        }

        private static double Normalize(double value, double min, double max, int newMin, int newMax)
        {
            if (max - min == 0) return newMin;
            return newMin + ((value - min) / (max - min)) * (newMax - newMin);
        }

        private static string RunFfmpeg(string arguments)
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    Arguments = arguments,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            string stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();

            return stderr;
        }

        private static double GetAudioDuration(string filePath)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "ffprobe",
                Arguments = $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 \"{filePath}\"",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            var process = Process.Start(psi);
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            if (double.TryParse(output, NumberStyles.Any, CultureInfo.InvariantCulture, out double duration))
                return duration;

            return 0;
        }

        private static double ParseRmsFromOutput(string ffmpegOutput)
        {
            // Пример строки: [Parsed_volumedetect_0 @ 0000029a68...] mean_volume: -23.4 dB
            using (var reader = new StringReader(ffmpegOutput))
            {
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (line.Contains("mean_volume"))
                    {
                        var parts = line.Split(':');
                        if (parts.Length >= 2 && double.TryParse(parts[1].Replace("dB", "").Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out double mean))
                            return mean;
                    }
                }
            }

            return -100.0; // если не найдено — очень тихий
        }
    }
}
