using BarkFluff.Proto.Configuration;
using MediatR;

namespace BarkFluff.Settings.Features.GetAllConfigurations;

public sealed record GetAllConfigurationsQuery : IRequest<GetAllConfigurationsResponse>;
