using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using Wpf.Ui.Controls;
using Button = Wpf.Ui.Controls.Button;
using TextBox = Wpf.Ui.Controls.TextBox;

namespace BarkFluff.Builder;

public partial class MainWindow : FluentWindow
{
    private BuilderModel _model = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _model;
    }

    private void OnIncludeSeqClick(object sender, RoutedEventArgs e) =>
        _model.SetGlobalSeq(SeqToggle.IsChecked == true);

    private void OnIncludeLognaraClick(object sender, RoutedEventArgs e) =>
        _model.SetGlobalLognara(LognaraToggle.IsChecked == true);

    private void OnBrowse(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new OpenFolderDialog
            {
                Title = "Папка для сгенерированной конфигурации",
                Multiselect = false
            };
            if (Directory.Exists(_model.OutputPath))
                dialog.InitialDirectory = _model.OutputPath;

            if (dialog.ShowDialog(this) == true)
                _model.OutputPath = dialog.FolderName;
        }
        catch (Exception exception)
        {
            SetStatus($"Не удалось выбрать папку: {exception.Message}", error: true);
        }
    }

    private void OnOpen(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new OpenFolderDialog
            {
                Title = "Выберите папку с конфигурацией BarkFluff Builder",
                Multiselect = false
            };
            if (Directory.Exists(_model.OutputPath))
                dialog.InitialDirectory = _model.OutputPath;

            if (dialog.ShowDialog(this) != true)
                return;

            _model = BuilderConfigLoader.Load(dialog.FolderName);
            DataContext = _model;
            var nextStepsPath = Path.Combine(_model.OutputPath, "NEXT_STEPS.md");
            SetStatus(
                $"Конфигурация загружена из {dialog.FolderName}.",
                error: false,
                showNextSteps: File.Exists(nextStepsPath));
        }
        catch (Exception exception)
        {
            SetStatus($"Не удалось открыть конфигурацию: {exception.Message}", error: true);
        }
    }

    private void OnPickCertificate(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { CommandParameter: TextBox textBox })
            return;

        try
        {
            var dialog = new OpenFileDialog
            {
                Title = "Выберите файл сертификата или ключа",
                Filter = "Сертификаты и ключи (*.pem;*.crt;*.key)|*.pem;*.crt;*.key|Все файлы (*.*)|*.*",
                CheckFileExists = true,
                Multiselect = false
            };
            var currentDirectory = Path.GetDirectoryName(textBox.Text);
            if (!string.IsNullOrWhiteSpace(currentDirectory) && Directory.Exists(currentDirectory))
                dialog.InitialDirectory = currentDirectory;

            if (dialog.ShowDialog(this) == true)
                textBox.Text = dialog.FileName;
        }
        catch (Exception exception)
        {
            SetStatus($"Не удалось выбрать файл: {exception.Message}", error: true);
        }
    }

    private void OnRandom(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { CommandParameter: TextBox textBox })
            return;

        try
        {
            switch (textBox.Name)
            {
                case "SetupTokenBox":
                    textBox.Text = ComposeGenerator.GenerateSetupToken();
                    break;
                case "LiveKitApiKeyBox":
                case "LiveKitApiSecretBox":
                    var credentials = ComposeGenerator.GenerateLiveKitCredentials();
                    _model.LiveKitApiKey = credentials.ApiKey;
                    _model.LiveKitApiSecret = credentials.ApiSecret;
                    break;
                case "PostgresPasswordBox":
                case "RabbitPasswordBox":
                case "SeqAdminPasswordBox":
                case "MinioRootPasswordBox":
                    var length = int.TryParse((sender as Button)?.Tag?.ToString(), out var configuredLength)
                        ? configuredLength
                        : 40;
                    textBox.Text = RandomNumberGenerator.GetString(
                        "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789",
                        length);
                    break;
            }
        }
        catch (Exception exception)
        {
            SetStatus($"Не удалось создать случайное значение: {exception.Message}", error: true);
        }
    }

    private void OnGenerate(object sender, RoutedEventArgs e)
    {
        var validationErrors = BuilderModelValidator.Validate(_model);
        if (validationErrors.Count > 0)
        {
            ShowValidationErrors(validationErrors);
            return;
        }

        if (string.IsNullOrWhiteSpace(_model.OutputPath))
        {
            SetStatus("Выберите папку для сгенерированной конфигурации.", error: true);
            return;
        }

        try
        {
            var files = BuilderOutputWriter.Write(_model);
            var outputPath = Path.GetFullPath(_model.OutputPath);
            SetStatus(
                $"Готово: создано файлов — {files.Count}. Папка: {outputPath}. ",
                error: false,
                showNextSteps: true);
        }
        catch (BuilderValidationException exception)
        {
            ShowValidationErrors(exception.Errors);
        }
        catch (Exception exception)
        {
            SetStatus($"Не удалось сгенерировать конфигурацию: {exception.Message}", error: true);
        }
    }

    private void OnOpenNextSteps(object sender, RoutedEventArgs e)
    {
        var path = Path.Combine(_model.OutputPath, "NEXT_STEPS.md");
        try
        {
            if (!File.Exists(path))
            {
                SetStatus("Файл NEXT_STEPS.md не найден. Сгенерируйте конфигурацию ещё раз.", error: true);
                return;
            }

            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            SetStatus($"Не удалось открыть NEXT_STEPS.md: {exception.Message}", error: true);
        }
    }

    private void ShowValidationErrors(IReadOnlyList<string> errors)
    {
        SetStatus($"Не удалось сгенерировать конфигурацию: ошибок — {errors.Count}.", error: true);
        var message = "Исправьте следующие поля и попробуйте снова:"
            + Environment.NewLine
            + Environment.NewLine
            + string.Join(Environment.NewLine, errors.Select(error => $"• {error}"));
        System.Windows.MessageBox.Show(
            this,
            message,
            "Проверка конфигурации",
            System.Windows.MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private void SetStatus(string message, bool error, bool showNextSteps = false)
    {
        StatusText.Text = message;
        StatusText.Foreground = (Brush)FindResource(
            error ? "SystemFillColorCriticalBrush" : "SystemFillColorSuccessBrush");
        NextStepsLink.Visibility = showNextSteps ? Visibility.Visible : Visibility.Collapsed;
    }
}
