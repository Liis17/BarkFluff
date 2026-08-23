using System.Text.RegularExpressions;

namespace Barkfluff.AdminPanel.Services;

/// <summary>
/// Образ BarkFluff-сервиса, найденный в docker-compose.yml
/// </summary>
/// <param name="Service">Имя сервиса в compose</param>
/// <param name="BaseRepository">Репозиторий без суффикса ветки (barkfluff-identity)</param>
/// <param name="Branch">Ветка обновлений: master, nightly или dev</param>
/// <param name="Tag">Тег образа (latest или semver)</param>
/// <param name="LineIndex">Индекс строки image: в файле</param>
public record ComposeImageInfo(string Service, string BaseRepository, string Branch, string Tag, int LineIndex);

/// <summary>
/// Чтение и правка строк image: в docker-compose.yml — переключение сервиса на другую ветку обновлений.
/// Файл смонтирован в контейнер как bind mount одного файла, поэтому запись идёт в тот же inode
/// (перезапись по тому же пути, без rename) — иначе монтирование внутри контейнера оторвётся.
/// </summary>
public class ComposeImageService
{
    public const string DefaultComposeFilePath = "/docker-compose.yml";
    private const string DefaultBackupDirectory = "/app/db/compose-backups";
    private const string RegistryPrefix = "docker.barkfluff.com/";
    private const int BackupsToKeep = 20;

    /// <summary>Ветки, для которых CI собирает образы (.github/actions/docker-version)</summary>
    public static readonly string[] Branches = ["master", "nightly", "dev"];

    private static readonly Regex TopLevelSectionRegex = new(
        @"^(?<name>[A-Za-z0-9._-]+):",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ServiceHeaderRegex = new(
        @"^  (?<name>[A-Za-z0-9._-]+):\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ImageRegex = new(
        @"^\s+image:\s*[""']?docker\.barkfluff\.com/(?<base>barkfluff-[a-z0-9-]+?)(?<suffix>-nightly|-dev)?:(?<tag>[^\s""']+)[""']?\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly SemaphoreSlim WriteGate = new(1, 1);

    private readonly ILogger<ComposeImageService> _logger;
    private readonly string _composeFilePath;
    private readonly string _backupDirectory;

    public ComposeImageService(IConfiguration configuration, ILogger<ComposeImageService> logger)
    {
        _logger = logger;
        _composeFilePath = configuration["Docker:ComposeFile"] ?? DefaultComposeFilePath;
        _backupDirectory = configuration["Docker:ComposeBackupDirectory"] ?? DefaultBackupDirectory;
    }

    /// <summary>
    /// Разобрать compose и вернуть BarkFluff-образы по имени сервиса.
    /// Сервисы с чужим registry (seq, redis, postgres) в результат не попадают.
    /// </summary>
    public static IReadOnlyDictionary<string, ComposeImageInfo> Parse(string composeYaml)
    {
        var result = new Dictionary<string, ComposeImageInfo>(StringComparer.OrdinalIgnoreCase);
        var lines = SplitLines(composeYaml);

        var inServices = false;
        string? currentService = null;

        for (var i = 0; i < lines.Count; i++)
        {
            var text = lines[i].Text;

            if (TopLevelSectionRegex.IsMatch(text))
            {
                inServices = text.StartsWith("services:", StringComparison.Ordinal);
                currentService = null;
                continue;
            }

            if (!inServices)
                continue;

            var header = ServiceHeaderRegex.Match(text);
            if (header.Success)
            {
                currentService = header.Groups["name"].Value;
                continue;
            }

            if (currentService is null || result.ContainsKey(currentService))
                continue;

            var image = ImageRegex.Match(text);
            if (!image.Success)
                continue;

            result[currentService] = new ComposeImageInfo(
                currentService,
                image.Groups["base"].Value,
                BranchFromSuffix(image.Groups["suffix"].Value),
                image.Groups["tag"].Value,
                i);
        }

        return result;
    }

    /// <summary>
    /// Заменить суффикс ветки в строке image: указанного сервиса. Остальной файл не меняется.
    /// </summary>
    public static bool TryRewrite(string composeYaml, string service, string branch, out string result, out string? error)
    {
        result = composeYaml;

        if (!IsKnownBranch(branch))
        {
            error = $"Неизвестная ветка {branch}";
            return false;
        }

        var images = Parse(composeYaml);
        if (!images.TryGetValue(service, out var info))
        {
            error = $"Сервис {service} не найден в docker-compose.yml или его образ не из {RegistryPrefix}";
            return false;
        }

        error = null;
        if (string.Equals(info.Branch, branch, StringComparison.OrdinalIgnoreCase))
            return true;

        var lines = SplitLines(composeYaml);
        var oldReference = $"{RegistryPrefix}{Repository(info.BaseRepository, info.Branch)}:";
        var newReference = $"{RegistryPrefix}{Repository(info.BaseRepository, branch)}:";

        var line = lines[info.LineIndex];
        lines[info.LineIndex] = line with { Text = line.Text.Replace(oldReference, newReference, StringComparison.Ordinal) };

        result = string.Concat(lines.Select(l => l.Text + l.NewLine));
        return true;
    }

    /// <summary>Репозиторий образа для ветки: barkfluff-identity, barkfluff-identity-nightly, barkfluff-identity-dev</summary>
    public static string Repository(string baseRepository, string branch) => branch switch
    {
        "nightly" => $"{baseRepository}-nightly",
        "dev" => $"{baseRepository}-dev",
        _ => baseRepository
    };

    /// <summary>Ветка по имени образа запущенного контейнера (docker.barkfluff.com/barkfluff-users-dev:latest → dev)</summary>
    public static string? BranchFromImage(string? image)
    {
        if (string.IsNullOrWhiteSpace(image) || !image.StartsWith(RegistryPrefix, StringComparison.OrdinalIgnoreCase))
            return null;

        var reference = image[RegistryPrefix.Length..];
        var tagSeparatorIndex = reference.LastIndexOf(':');
        var repository = tagSeparatorIndex > 0 ? reference[..tagSeparatorIndex] : reference;
        if (!repository.StartsWith("barkfluff-", StringComparison.OrdinalIgnoreCase))
            return null;

        if (repository.EndsWith("-nightly", StringComparison.OrdinalIgnoreCase)) return "nightly";
        if (repository.EndsWith("-dev", StringComparison.OrdinalIgnoreCase)) return "dev";
        return "master";
    }

    public static bool IsKnownBranch(string? branch) =>
        branch is not null && Branches.Contains(branch, StringComparer.Ordinal);

    /// <summary>Прочитать и разобрать compose-файл</summary>
    public async Task<IReadOnlyDictionary<string, ComposeImageInfo>> GetImagesAsync()
    {
        var content = await File.ReadAllTextAsync(_composeFilePath);
        return Parse(content);
    }

    /// <summary>
    /// Переключить сервис на ветку. Возвращает прежнее содержимое файла — для отката, если pull не удался.
    /// </summary>
    public async Task<string> SetBranchAsync(string service, string branch)
    {
        await WriteGate.WaitAsync();
        try
        {
            var previous = await File.ReadAllTextAsync(_composeFilePath);
            if (!TryRewrite(previous, service, branch, out var updated, out var error))
                throw new InvalidOperationException(error);

            if (!string.Equals(previous, updated, StringComparison.Ordinal))
            {
                await BackupAsync(previous);
                await File.WriteAllTextAsync(_composeFilePath, updated);
                _logger.LogInformation("Сервис {Service} переключён на ветку {Branch} в {ComposeFile}", service, branch, _composeFilePath);
            }

            return previous;
        }
        finally
        {
            WriteGate.Release();
        }
    }

    /// <summary>Вернуть прежнее содержимое compose-файла</summary>
    public async Task RestoreAsync(string previousContent)
    {
        await WriteGate.WaitAsync();
        try
        {
            await File.WriteAllTextAsync(_composeFilePath, previousContent);
            _logger.LogWarning("Правка {ComposeFile} откачена", _composeFilePath);
        }
        finally
        {
            WriteGate.Release();
        }
    }

    private async Task BackupAsync(string content)
    {
        try
        {
            Directory.CreateDirectory(_backupDirectory);
            var backupPath = Path.Combine(_backupDirectory, $"docker-compose-{DateTime.UtcNow:yyyyMMdd-HHmmss}.yml");
            await File.WriteAllTextAsync(backupPath, content);

            var stale = Directory.GetFiles(_backupDirectory, "docker-compose-*.yml")
                .OrderByDescending(path => path, StringComparer.Ordinal)
                .Skip(BackupsToKeep);
            foreach (var path in stale)
                File.Delete(path);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Не удалось сохранить резервную копию compose-файла");
        }
    }

    private static string BranchFromSuffix(string suffix) => suffix switch
    {
        "-nightly" => "nightly",
        "-dev" => "dev",
        _ => "master"
    };

    private record Line(string Text, string NewLine);

    /// <summary>Разбить текст на строки с сохранением их переводов строки, чтобы склейка вернула исходный файл байт-в-байт</summary>
    private static List<Line> SplitLines(string content)
    {
        var lines = new List<Line>();
        var start = 0;

        for (var i = 0; i < content.Length; i++)
        {
            if (content[i] != '\n' && content[i] != '\r')
                continue;

            var newLine = content[i] == '\r' && i + 1 < content.Length && content[i + 1] == '\n' ? "\r\n" : content[i].ToString();
            lines.Add(new Line(content[start..i], newLine));
            i += newLine.Length - 1;
            start = i + 1;
        }

        if (start < content.Length)
            lines.Add(new Line(content[start..], string.Empty));

        return lines;
    }
}
