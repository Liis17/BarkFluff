using BarkFluff.Proto.Configuration;

using MediatR;

namespace BarkFluff.Configuration.Features.RollbackConfiguration;

public class RollbackConfigurationCommand : IRequest<RollbackConfigurationResponse>
{
    public long RevisionId { get; set; }
    public string EditedBy { get; set; } = string.Empty;
    public string EditedFrom { get; set; } = string.Empty;
}
