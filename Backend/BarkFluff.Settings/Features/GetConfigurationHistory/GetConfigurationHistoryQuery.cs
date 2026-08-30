using BarkFluff.Proto.Configuration;
using MediatR;

namespace BarkFluff.Settings.Features.GetConfigurationHistory;

public sealed record GetConfigurationHistoryQuery(string Section, string Key, int ServiceId, int Count)
    : IRequest<GetConfigurationHistoryResponse>;
