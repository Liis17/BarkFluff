using BarkFluff.Proto.Configuration;
using MediatR;

namespace BarkFluff.Settings.Features.GetReservedNames;

public sealed record GetReservedNamesQuery : IRequest<GetReservedNamesResponse>;
