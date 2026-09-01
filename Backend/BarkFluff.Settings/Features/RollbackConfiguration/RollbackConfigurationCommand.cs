using BarkFluff.Proto.Configuration;
using MediatR;

namespace BarkFluff.Settings.Features.RollbackConfiguration;

public sealed record RollbackConfigurationCommand(long RevisionId, string EditedBy, string EditedFrom)
    : IRequest<RollbackConfigurationResponse>;
