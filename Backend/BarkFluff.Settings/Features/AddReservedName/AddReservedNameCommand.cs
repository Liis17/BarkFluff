using BarkFluff.Proto.Configuration;
using MediatR;

namespace BarkFluff.Settings.Features.AddReservedName;

public sealed record AddReservedNameCommand(string Name) : IRequest<AddReservedNameResponse>;
