using BarkFluff.Proto.Configuration;
using MediatR;

namespace BarkFluff.Settings.Features.UpdateConfiguration;

public sealed record UpdateConfigurationCommand(string Section, string Key, string Value, int ServiceId, string EditedBy, string EditedFrom)
    : IRequest<UpdateConfigurationResponse>;
