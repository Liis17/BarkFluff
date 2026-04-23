using BarkFluff.Proto.Developers;
using BarkFluff.Shared.Identity;
using Barkfluff.Developers.Features.CreateSection;
using Barkfluff.Developers.Features.DeleteSection;
using Barkfluff.Developers.Features.GetErrorCodes;
using Barkfluff.Developers.Features.GetProtoFileContent;
using Barkfluff.Developers.Features.GetProtoFiles;
using Barkfluff.Developers.Features.GetSectionByKey;
using Barkfluff.Developers.Features.GetSections;
using Barkfluff.Developers.Features.UpdateSection;
using Grpc.Core;
using MediatR;
using Microsoft.AspNetCore.Authorization;

namespace Barkfluff.Developers.Host;

[Authorize(Policy = nameof(TokenType.User))]
public class DevelopersApiService : DevelopersApi.DevelopersApiBase
{
    private readonly IMediator _mediator;

    public DevelopersApiService(IMediator mediator)
    {
        _mediator = mediator;
    }

    public override async Task<GetDocumentationSectionsResponse> GetDocumentationSections(GetDocumentationSectionsRequest request, ServerCallContext context)
    {
        return await _mediator.Send(new GetDocumentationSectionsQuery(), context.CancellationToken);
    }

    public override async Task<DocumentationSection> GetDocumentationSection(GetDocumentationSectionRequest request, ServerCallContext context)
    {
        return await _mediator.Send(new GetDocumentationSectionQuery { Key = request.Key }, context.CancellationToken);
    }

    public override async Task<GetProtoFilesResponse> GetProtoFiles(GetProtoFilesRequest request, ServerCallContext context)
    {
        return await _mediator.Send(new GetProtoFilesQuery(), context.CancellationToken);
    }

    public override async Task<GetProtoFileContentResponse> GetProtoFileContent(GetProtoFileContentRequest request, ServerCallContext context)
    {
        return await _mediator.Send(new GetProtoFileContentQuery { FileName = request.FileName }, context.CancellationToken);
    }

    public override async Task<GetErrorCodesResponse> GetErrorCodes(GetErrorCodesRequest request, ServerCallContext context)
    {
        return await _mediator.Send(new GetErrorCodesQuery(), context.CancellationToken);
    }
}
