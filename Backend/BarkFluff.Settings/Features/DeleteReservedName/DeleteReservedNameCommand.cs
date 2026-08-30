using BarkFluff.Proto.Configuration;
using MediatR;

namespace BarkFluff.Settings.Features.DeleteReservedName;

public sealed record DeleteReservedNameCommand(string Name) : IRequest<DeleteReservedNameResponse>;
