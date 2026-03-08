using BarkFluff.Proto.Users;

using MediatR;

namespace BarkFluff.Users.Features.ExportData;

public class ExportDataCommand : IRequest<ExportDataResponse>
{
    public long UserId { get; init; }
}
