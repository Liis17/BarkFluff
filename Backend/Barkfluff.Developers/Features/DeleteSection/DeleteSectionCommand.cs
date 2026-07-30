using MediatR;

namespace Barkfluff.Developers.Features.DeleteSection;

public class DeleteSectionCommand : IRequest<bool>
{
    public string Key { get; set; } = string.Empty;
}
