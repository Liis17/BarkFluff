using BarkFluff.Proto.Developers;
using MediatR;

namespace Barkfluff.Developers.Features.GetErrorCodes;

public class GetErrorCodesQuery : IRequest<GetErrorCodesResponse> { }
