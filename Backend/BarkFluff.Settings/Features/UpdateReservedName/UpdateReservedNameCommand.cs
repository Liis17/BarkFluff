using BarkFluff.Proto.Configuration;
using MediatR;

namespace BarkFluff.Settings.Features.UpdateReservedName;

public sealed record UpdateReservedNameCommand(string OldName, string NewName) : IRequest<UpdateReservedNameResponse>;
