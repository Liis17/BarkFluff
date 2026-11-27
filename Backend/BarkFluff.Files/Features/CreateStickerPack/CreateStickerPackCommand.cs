using BarkFluff.Proto.Files;

using MediatR;

namespace BarkFluff.Files.Features.CreateStickerPack;

public class CreateStickerPackCommand : IRequest<CreateStickerPackResponse>
{
    public long CreatorUserId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    /// <summary>Пусто — имя генерируется из id набора.</summary>
    public string ShortName { get; set; } = string.Empty;
}
