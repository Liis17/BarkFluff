using BarkFluff.Proto.Configuration;

using MediatR;

namespace BarkFluff.Configuration.Features.GetConfigurationHistory;

public class GetConfigurationHistoryCommand : IRequest<GetConfigurationHistoryResponse>
{
    public string Section { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public int ServiceId { get; set; }
    public int Count { get; set; } = 30;
}
