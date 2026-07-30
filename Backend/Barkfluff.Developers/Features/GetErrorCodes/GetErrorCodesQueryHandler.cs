using BarkFluff.Proto.Developers;
using Barkfluff.Developers.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;
using MediatR;

namespace Barkfluff.Developers.Features.GetErrorCodes;

public class GetErrorCodesQueryHandler : IRequestHandler<GetErrorCodesQuery, GetErrorCodesResponse>
{
    private readonly DevelopersContext _context;

    public GetErrorCodesQueryHandler(DevelopersContext context)
    {
        _context = context;
    }

    public async Task<GetErrorCodesResponse> Handle(GetErrorCodesQuery request, CancellationToken cancellationToken)
    {
        var codes = await _context.ErrorCodes.ToListAsync(cancellationToken);

        var response = new GetErrorCodesResponse();
        foreach (var c in codes)
        {
            response.ErrorCodes.Add(new ErrorCodeEntry
            {
                Code = c.Code,
                ExceptionName = c.ExceptionName,
                Description = c.Description,
                Domain = c.Domain
            });
        }

        return response;
    }
}
