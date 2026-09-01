using BarkFluff.Proto.Configuration;
using BarkFluff.Shared.Identity;
using MediatR;

namespace BarkFluff.Settings.Features.GetConfiguration;

public sealed record GetConfigurationQuery(ServiceId ServiceId) : IRequest<GetConfigurationResponse>;
