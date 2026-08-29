using BarkFluff.Proto.Developers;
using Barkfluff.Developers.Features.GetProtoFileContent;
using Barkfluff.Developers.Features.GetProtoFiles;
using Barkfluff.Developers.Persistence.Services;
using Grpc.Core;

namespace Barkfluff.Developers.Tests;

public class ProtoQueryHandlerTests
{
    [Fact]
    public async Task Direct_request_for_an_internal_proto_returns_not_found()
    {
        await using var context = TestInfrastructure.CreateContext();
        context.ProtoMetadata.Add(TestInfrastructure.ProtoMetadata("configuration_api.proto"));
        await context.SaveChangesAsync();

        var handler = new GetProtoFileContentQueryHandler(
            new TestInfrastructure.TestPublishedProtoCatalog(),
            new ProtoMetadataStorage(context));

        var action = () => handler.Handle(
            new GetProtoFileContentQuery { FileName = "configuration_api.proto" },
            CancellationToken.None);

        var exception = await action.Should().ThrowAsync<RpcException>();
        exception.Which.StatusCode.Should().Be(StatusCode.NotFound);
    }

    [Fact]
    public async Task Published_proto_requires_db_metadata_and_returns_content_and_metadata()
    {
        await using var context = TestInfrastructure.CreateContext();
        context.ProtoMetadata.Add(TestInfrastructure.ProtoMetadata("shared.proto"));
        await context.SaveChangesAsync();

        var handler = new GetProtoFileContentQueryHandler(
            new TestInfrastructure.TestPublishedProtoCatalog(),
            new ProtoMetadataStorage(context));

        var response = await handler.Handle(
            new GetProtoFileContentQuery { FileName = "shared.proto" },
            CancellationToken.None);

        response.Content.Should().Be("syntax = \"proto3\";");
        response.Metadata.FileName.Should().Be("shared.proto");
    }

    [Fact]
    public async Task Proto_list_filters_hidden_and_missing_files_and_sorts_deterministically()
    {
        await using var context = TestInfrastructure.CreateContext();
        context.ProtoMetadata.AddRange(
            TestInfrastructure.ProtoMetadata("users_api.proto", order: 2),
            TestInfrastructure.ProtoMetadata("shared.proto", order: 1),
            TestInfrastructure.ProtoMetadata("configuration_api.proto", order: 0),
            TestInfrastructure.ProtoMetadata("identity_api.proto", order: 2));
        await context.SaveChangesAsync();

        var handler = new GetProtoFilesQueryHandler(
            new ProtoMetadataStorage(context),
            new TestInfrastructure.TestPublishedProtoCatalog(["users_api.proto"]));

        var response = await handler.Handle(new GetProtoFilesQuery(), CancellationToken.None);

        response.Files.Select(file => file.FileName)
            .Should().Equal("shared.proto", "identity_api.proto");
    }
}
