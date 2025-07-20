using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;

namespace BarkFluff.Client.WPF.UserControls
{
    /// <summary>
    /// Логика взаимодействия для RecordButton.xaml
    /// </summary>
    public partial class RecordButton : UserControl
    {
        private Process ffmpegProcess;
        private string outputFile;

        private readonly Storyboard glowStoryboard;
        private bool isRecording;

        public RecordButton()
        {
            InitializeComponent();

            glowStoryboard = CreateGlowAnimation();
            GlowEllipse.MouseLeftButtonDown += OnMouseDown;
            GlowEllipse.MouseLeftButtonUp += OnMouseUp;
            GlowEllipse.MouseLeave += OnMouseUp; // На случай выхода мыши
        }

        private async void OnMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (isRecording) return;

            isRecording = true;

            AnimateScale(1.0, 1.2);
            glowStoryboard.Begin(GlowEllipse, true);
            await StartRecordingAsync();
        }

        private void OnMouseUp(object sender, MouseEventArgs e)
        {
            if (!isRecording) return;

            isRecording = false;

            AnimateScale(1.2, 1.0);
            glowStoryboard.Stop(GlowEllipse);
            StopRecording();
        }

        private void AnimateScale(double from, double to)
        {
            var scaleX = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(150)) { EasingFunction = new SineEase() };
            var scaleY = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(150)) { EasingFunction = new SineEase() };

            ButtonScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleX);
            ButtonScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleY);
        }

        private Storyboard CreateGlowAnimation()
        {
            var sb = new Storyboard { RepeatBehavior = RepeatBehavior.Forever };

            var gradientBrush = new LinearGradientBrush();
            gradientBrush.GradientStops.Add(new GradientStop(Colors.DeepSkyBlue, 0));
            gradientBrush.GradientStops.Add(new GradientStop(Colors.DodgerBlue, 1));
            GlowEllipse.Fill = gradientBrush;

            var offsetAnim = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(2))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever
            };

            Storyboard.SetTarget(offsetAnim, GlowEllipse);
            Storyboard.SetTargetProperty(offsetAnim, new PropertyPath("(Shape.Fill).(LinearGradientBrush.GradientStops)[0].Offset"));
            sb.Children.Add(offsetAnim);

            return sb;
        }

        private async Task StartRecordingAsync()
        {
            outputFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), $"record_{DateTime.Now:yyyyMMdd_HHmmss}.ogg");

            ffmpegProcess = new Process();
            ffmpegProcess.StartInfo.FileName = "ffmpeg";

            ffmpegProcess.StartInfo.Arguments = $"-f dshow -i audio=\"MicrophoneUSB (Zet Koradji Pro)\" -c:a libvorbis \"{outputFile}\"";
            ffmpegProcess.StartInfo.UseShellExecute = false;
            ffmpegProcess.StartInfo.CreateNoWindow = true;
            ffmpegProcess.StartInfo.RedirectStandardError = true;
            ffmpegProcess.StartInfo.RedirectStandardInput = false; 

            try
            {
                ffmpegProcess.Start();
                await Task.Delay(500); // подождать немного, чтобы точно стартанул
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка запуска ffmpeg: {ex.Message}");
            }
        }

        private void StopRecording()
        {
            try
            {
                if (ffmpegProcess != null && !ffmpegProcess.HasExited)
                {
                    AttachConsole((uint)ffmpegProcess.Id);
                    SetConsoleCtrlHandler(null, true); // Игнорируем Ctrl+C в текущем процессе
                    GenerateConsoleCtrlEvent(CtrlTypes.CTRL_C_EVENT, 0);
                    ffmpegProcess.WaitForExit(2000);

                    ffmpegProcess.Kill(); // если не успел завершиться
                    ffmpegProcess.Dispose();
                    ffmpegProcess = null;

                    FreeConsole(); // Отвязать консоль
                    SetConsoleCtrlHandler(null, false);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка остановки записи: {ex.Message}");
            }
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool GenerateConsoleCtrlEvent(CtrlTypes dwCtrlEvent, uint dwProcessGroupId);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool AttachConsole(uint dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
        static extern bool FreeConsole();

        [DllImport("kernel32.dll")]
        static extern bool SetConsoleCtrlHandler(ConsoleCtrlDelegate handlerRoutine, bool add);

        delegate bool ConsoleCtrlDelegate(CtrlTypes ctrlType);

        enum CtrlTypes : uint
        {
            CTRL_C_EVENT = 0,
            CTRL_BREAK_EVENT = 1
        }
    }
}
