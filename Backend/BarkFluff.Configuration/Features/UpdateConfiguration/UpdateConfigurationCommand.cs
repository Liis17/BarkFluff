using BarkFluff.Proto.Configuration;

using MediatR;

namespace BarkFluff.Configuration.Features.UpdateConfiguration;

public class UpdateConfigurationCommand : IRequest<UpdateConfigurationResponse>
{
    public string Section { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public int ServiceId { get; set; }
    public string EditedBy { get; set; } = string.Empty;
    public string EditedFrom { get; set; } = string.Empty;
}
