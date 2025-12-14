using BarkFluff.Files.Persistence;
using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Proto.Files;
using MediatR;

namespace BarkFluff.Files.Features.CheckFileHash;

public class CheckFileHashCommandHandler : IRequestHandler<CheckFileHashCommand, CheckFileHashResponse>
{
    private readonly FileHashesStorage _hashesStorage;
    private readonly UploadedFilesStorage _filesStorage;
    private readonly UserContext _userContext;
    private readonly ILogger<CheckFileHashCommandHandler> _logger;

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
