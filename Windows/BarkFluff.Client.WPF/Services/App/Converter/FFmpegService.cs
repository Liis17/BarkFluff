using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace BarkFluff.Client.WPF.Services.App.Converter
{
    /// <summary>
    /// Сервис для работы с FFmpeg и FFprobe
    /// </summary>
    public class FFmpegService
    {
        private const string FFMPEG_PATH = @"C:\ProgramData\ffmpeg\ffmpeg.exe";
        private const string FFPROBE_PATH = @"C:\ProgramData\ffmpeg\ffprobe.exe";

        /// <summary>
        /// Событие для отслеживания прогресса обработки видео
        /// </summary>
        public event EventHandler<ProgressEventArgs>? ProgressChanged;

        /// <summary>
        /// Получает метаданные видео через ffprobe
        /// </summary>
        /// <param name="videoPath">Путь к видео файлу</param>
        /// <returns>Метаданные видео</returns>
        public async Task<VideoMetadata> GetVideoMetadataAsync(string videoPath)
        {
            if (!File.Exists(videoPath))
                throw new FileNotFoundException("Видео файл не найден", videoPath);

            if (!File.Exists(FFPROBE_PATH))
                throw new FileNotFoundException("FFprobe не найден", FFPROBE_PATH);

            var metadata = new VideoMetadata { FilePath = videoPath };

            // Получить длительность
            metadata.Duration = await GetVideoDurationAsync(videoPath);

            // Получить разрешение
            var (width, height) = await GetVideoResolutionAsync(videoPath);
            metadata.Width = width;
            metadata.Height = height;

            // Получить битрейт
            metadata.Bitrate = await GetVideoBitrateAsync(videoPath);

            return metadata;
        }

        /// <summary>
        /// Получает длительность видео в секундах
        /// </summary>
        private async Task<double> GetVideoDurationAsync(string videoPath)
        {
            var args = $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 \"{videoPath}\"";
            var output = await RunFFProbeAsync(args);

            if (double.TryParse(output.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out double duration))
                return duration;

            throw new Exception("Не удалось получить длительность видео");
        }

        /// <summary>
        /// Получает разрешение видео
        /// </summary>
        private async Task<(int width, int height)> GetVideoResolutionAsync(string videoPath)
        {
            var args = $"-v error -select_streams v:0 -show_entries stream=width,height -of csv=s=x:p=0 \"{videoPath}\"";
            var output = await RunFFProbeAsync(args);

            var parts = output.Trim().Split('x');
            if (parts.Length == 2 &&
                int.TryParse(parts[0], out int width) &&
                int.TryParse(parts[1], out int height))
            {
                return (width, height);
            }

            throw new Exception("Не удалось получить разрешение видео");
        }

        /// <summary>
        /// Получает битрейт видео в kbps
        /// </summary>
        private async Task<int> GetVideoBitrateAsync(string videoPath)
        {
            var args = $"-v error -select_streams v:0 -show_entries stream=bit_rate -of default=noprint_wrappers=1:nokey=1 \"{videoPath}\"";
            var output = await RunFFProbeAsync(args);

            if (int.TryParse(output.Trim(), out int bitrate) && bitrate > 0)
                return bitrate / 1000; // конвертировать в kbps

            // Если битрейт не доступен, оценить по размеру файла
            var fileInfo = new FileInfo(videoPath);
            var duration = await GetVideoDurationAsync(videoPath);
            if (duration > 0)
                return (int)((fileInfo.Length * 8) / (duration * 1000));

            throw new Exception("Не удалось получить битрейт видео");
        }

        /// <summary>
        /// Обрабатывает видео с заданными параметрами
        /// </summary>
        /// <param name="inputPath">Путь к входному видео файлу</param>
        /// <param name="outputPath">Путь к выходному видео файлу</param>
        /// <param name="settings">Настройки обработки</param>
        /// <param name="cancellationToken">Токен отмены</param>
        /// <returns>Путь к обработанному файлу</returns>
        public async Task<string> ProcessVideoAsync(
            string inputPath,
            string outputPath,
            VideoEditSettings settings,
            CancellationToken cancellationToken = default)
        {
            if (!File.Exists(inputPath))
                throw new FileNotFoundException("Входной файл не найден", inputPath);

            if (!File.Exists(FFMPEG_PATH))
                throw new FileNotFoundException("FFmpeg не найден", FFMPEG_PATH);

            // Убедиться что директория существует
            var outputDir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
                Directory.CreateDirectory(outputDir);

            // Построить команду ffmpeg
            var args = BuildFFmpegCommand(inputPath, outputPath, settings);

            await RunFFmpegWithProgressAsync(args, settings.EndTime - settings.StartTime, cancellationToken);

            if (!File.Exists(outputPath))
                throw new Exception("FFmpeg не создал выходной файл");

            return outputPath;
        }

        /// <summary>
        /// Строит команду ffmpeg
        /// </summary>
        private string BuildFFmpegCommand(string inputPath, string outputPath, VideoEditSettings settings)
        {
            // Входной файл
            var args = $"-i \"{inputPath}\"";

            // Обрезка по времени
            args += $" -ss {settings.StartTime.ToString("F3", CultureInfo.InvariantCulture)}";
            args += $" -to {settings.EndTime.ToString("F3", CultureInfo.InvariantCulture)}";

            // Видео кодек и битрейт
            args += " -c:v libx264";
            args += $" -b:v {settings.TargetBitrate}k";

            // Разрешение
            args += $" -vf scale={settings.TargetWidth}:{settings.TargetHeight}";

            // Аудио кодек
            args += " -c:a aac";
            args += " -b:a 128k";

            // Настройки качества
            args += " -preset fast";
            args += " -crf 23";

            // Прогресс
            args += " -progress pipe:2";

            // Перезаписать выходной файл
            args += " -y";

            // Выходной файл
            args += $" \"{outputPath}\"";

            return args;
        }

        /// <summary>
        /// Запускает ffmpeg с отслеживанием прогресса
        /// </summary>
        private async Task RunFFmpegWithProgressAsync(
            string arguments,
            double totalDuration,
            CancellationToken cancellationToken)
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = FFMPEG_PATH,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                }
            };

            process.Start();

            // Читать stderr для прогресса
            var progressTask = Task.Run(async () =>
            {
                using var reader = process.StandardError;
                string? line;
                while ((line = await reader.ReadLineAsync()) != null)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        try
                        {
                            process.Kill();
                        }
                        catch { }
                        break;
                    }

                    ParseAndReportProgress(line, totalDuration);
                }
            }, cancellationToken);

            await process.WaitForExitAsync(cancellationToken);
            await progressTask;

            if (process.ExitCode != 0 && !cancellationToken.IsCancellationRequested)
            {
                throw new Exception($"FFmpeg завершился с ошибкой (код: {process.ExitCode})");
            }
        }

        /// <summary>
        /// Парсит вывод ffmpeg и сообщает о прогрессе
        /// </summary>
        private void ParseAndReportProgress(string line, double totalDuration)
        {
            // Пример вывода ffmpeg:
            // out_time_ms=1234567
            // progress=continue

            if (line.StartsWith("out_time_ms="))
            {
                var timeStr = line.Substring("out_time_ms=".Length);
                if (long.TryParse(timeStr, out long timeMicroseconds))
                {
                    double currentTime = timeMicroseconds / 1_000_000.0;
                    double progress = totalDuration > 0 ? (currentTime / totalDuration) * 100.0 : 0;
                    progress = Math.Min(Math.Max(progress, 0), 100.0);

                    ProgressChanged?.Invoke(this, new ProgressEventArgs
                    {
                        Progress = progress,
                        CurrentTime = currentTime,
                        TotalTime = totalDuration
                    });
                }
            }
        }

        /// <summary>
        /// Запускает ffprobe
        /// </summary>
        private async Task<string> RunFFProbeAsync(string arguments)
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = FFPROBE_PATH,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                var error = await process.StandardError.ReadToEndAsync();
                throw new Exception($"FFProbe error: {error}");
            }

            return output;
        }
    }

    /// <summary>
    /// Аргументы события прогресса обработки
    /// </summary>
    public class ProgressEventArgs : EventArgs
    {
        public double Progress { get; set; } // 0-100
        public double CurrentTime { get; set; }
        public double TotalTime { get; set; }
    }

    /// <summary>
    /// Метаданные видео файла
    /// </summary>
    public class VideoMetadata
    {
        public string FilePath { get; set; } = string.Empty;
        public double Duration { get; set; } // в секундах
        public int Width { get; set; }
        public int Height { get; set; }
        public int Bitrate { get; set; } // в kbps
    }

    /// <summary>
    /// Настройки редактирования видео
    /// </summary>
    public class VideoEditSettings
    {
        public double StartTime { get; set; } // в секундах
        public double EndTime { get; set; }   // в секундах
        public int TargetBitrate { get; set; } // в kbps
        public int TargetWidth { get; set; }
        public int TargetHeight { get; set; }
    }
}
