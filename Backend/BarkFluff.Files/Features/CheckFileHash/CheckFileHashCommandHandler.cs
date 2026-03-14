using BarkFluff.Files.Persistence;
using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Proto.Files;

using MediatR;

using System.Text.RegularExpressions;

namespace BarkFluff.Files.Features.CheckFileHash;

public partial class CheckFileHashCommandHandler : IRequestHandler<CheckFileHashCommand, CheckFileHashResponse>
{
    private readonly FileHashesStorage _hashesStorage;
    private readonly UploadedFilesStorage _filesStorage;
    private readonly UserContext _userContext;
    private readonly ILogger<CheckFileHashCommandHandler> _logger;

    // Regex for validating SHA256 hash format (64 hex characters)
    [GeneratedRegex("^[a-fA-F0-9]{64}$", RegexOptions.Compiled)]
    private static partial Regex Sha256HashRegex();

    public CheckFileHashCommandHandler(
        FileHashesStorage hashesStorage,
        UploadedFilesStorage filesStorage,
        UserContext userContext,
        ILogger<CheckFileHashCommandHandler> logger)
    {
        _hashesStorage = hashesStorage;
        _filesStorage = filesStorage;
        _userContext = userContext;
        _logger = logger;
    }

    public async Task<CheckFileHashResponse> Handle(CheckFileHashCommand request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Проверка хеша файла: {FileHash}", request.FileHash);

        // Validate hash format (must be 64 hex characters for SHA256)
        if (string.IsNullOrEmpty(request.FileHash) || !Sha256HashRegex().IsMatch(request.FileHash))
        {
            _logger.LogWarning("Неверный формат хеша: {FileHash}", request.FileHash);
            return new CheckFileHashResponse
            {
                FileId = string.Empty
            };
        }

        // Normalize hash to lowercase
        var normalizedHash = request.FileHash.ToLowerInvariant();

        var fileId = await _hashesStorage.GetFileIdByHash(normalizedHash);

        if (fileId.HasValue)
        {
            _logger.LogInformation("Файл с хешем {FileHash} найден, FileId: {FileId}", normalizedHash, fileId.Value);

            // Add current user to uploaders list (for deduplication tracking)
            await _filesStorage.AddUploaderToFile(fileId.Value, _userContext.UserId);

            return new CheckFileHashResponse
            {
                FileId = fileId.Value.ToString()
            };
        }

        _logger.LogInformation("Файл с хешем {FileHash} не найден", normalizedHash);

        return new CheckFileHashResponse
        {
            FileId = string.Empty
        };
    }
}
