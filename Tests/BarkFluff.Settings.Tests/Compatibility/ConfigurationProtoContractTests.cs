using BarkFluff.Settings.Host;

using Google.Protobuf.Reflection;

using Xunit;

namespace BarkFluff.Settings.Tests.Compatibility;

public sealed class ConfigurationProtoContractTests
{
    [Fact]
    public void Entire_legacy_descriptor_is_unchanged()
    {
        var reflectionType = typeof(SettingsApiService).Assembly
            .GetType("BarkFluff.Proto.Configuration.ConfigurationApiReflection", throwOnError: true)!;
        var descriptor = (FileDescriptor)reflectionType.GetProperty("Descriptor")!.GetValue(null)!;
        var service = Assert.Single(descriptor.Services);
        Assert.Equal("barkfluff.configuration.ConfigurationApi", service.FullName);
        Assert.Equal(
        [
            "GetConfiguration|barkfluff.configuration.GetConfigurationRequest|barkfluff.configuration.GetConfigurationResponse|client_streaming=False|server_streaming=False",
            "GetAllConfigurations|barkfluff.configuration.GetAllConfigurationsRequest|barkfluff.configuration.GetAllConfigurationsResponse|client_streaming=False|server_streaming=False",
            "UpdateConfiguration|barkfluff.configuration.UpdateConfigurationRequest|barkfluff.configuration.UpdateConfigurationResponse|client_streaming=False|server_streaming=False",
            "GetConfigurationHistory|barkfluff.configuration.GetConfigurationHistoryRequest|barkfluff.configuration.GetConfigurationHistoryResponse|client_streaming=False|server_streaming=False",
            "RollbackConfiguration|barkfluff.configuration.RollbackConfigurationRequest|barkfluff.configuration.RollbackConfigurationResponse|client_streaming=False|server_streaming=False",
            "GetReservedNames|barkfluff.configuration.GetReservedNamesRequest|barkfluff.configuration.GetReservedNamesResponse|client_streaming=False|server_streaming=False",
            "AddReservedName|barkfluff.configuration.AddReservedNameRequest|barkfluff.configuration.AddReservedNameResponse|client_streaming=False|server_streaming=False",
            "UpdateReservedName|barkfluff.configuration.UpdateReservedNameRequest|barkfluff.configuration.UpdateReservedNameResponse|client_streaming=False|server_streaming=False",
            "DeleteReservedName|barkfluff.configuration.DeleteReservedNameRequest|barkfluff.configuration.DeleteReservedNameResponse|client_streaming=False|server_streaming=False"
        ],
            service.Methods.Select(method =>
                $"{method.Name}|{method.InputType.FullName}|{method.OutputType.FullName}|client_streaming={method.IsClientStreaming}|server_streaming={method.IsServerStreaming}"));

        Assert.Equal(
        [
            "GetConfigurationRequest|oneofs=-",
            "GetConfigurationResponse|oneofs=-",
            "ConfigurationItem|oneofs=-",
            "GetAllConfigurationsRequest|oneofs=-",
            "GetAllConfigurationsResponse|oneofs=-",
            "UpdateConfigurationRequest|oneofs=-",
            "UpdateConfigurationResponse|oneofs=-",
            "GetConfigurationHistoryRequest|oneofs=-",
            "GetConfigurationHistoryResponse|oneofs=-",
            "ConfigurationRevision|oneofs=-",
            "RollbackConfigurationRequest|oneofs=-",
            "RollbackConfigurationResponse|oneofs=-",
            "GetReservedNamesRequest|oneofs=-",
            "GetReservedNamesResponse|oneofs=-",
            "AddReservedNameRequest|oneofs=-",
            "AddReservedNameResponse|oneofs=-",
            "UpdateReservedNameRequest|oneofs=-",
            "UpdateReservedNameResponse|oneofs=-",
            "DeleteReservedNameRequest|oneofs=-",
            "DeleteReservedNameResponse|oneofs=-"
        ],
            descriptor.MessageTypes.Select(message =>
                $"{message.Name}|oneofs={Format(message.Oneofs.Select(oneof => oneof.Name))}"));

        var fields = descriptor.MessageTypes
            .SelectMany(message => message.Fields.InDeclarationOrder()
                .Select(field => $"{message.Name}.{field.Name}={field.FieldNumber}|{field.FieldType}|{(field.IsRepeated ? "repeated" : "singular")}|type={GetTypeName(field)}|oneof={field.ContainingOneof?.Name ?? "-"}"))
            .ToArray();
        Assert.Equal(
        [
            "GetConfigurationRequest.service_id=1|Int32|singular|type=-|oneof=-",
            "GetConfigurationResponse.configurations=1|Message|repeated|type=barkfluff.configuration.ConfigurationItem|oneof=-",
            "ConfigurationItem.section=1|String|singular|type=-|oneof=-", "ConfigurationItem.key=2|String|singular|type=-|oneof=-", "ConfigurationItem.value=3|String|singular|type=-|oneof=-",
            "ConfigurationItem.edited_at=4|Message|singular|type=google.protobuf.Timestamp|oneof=-", "ConfigurationItem.edited_by=5|String|singular|type=-|oneof=-", "ConfigurationItem.edited_from=6|String|singular|type=-|oneof=-", "ConfigurationItem.service_id=7|Int32|singular|type=-|oneof=-",
            "GetAllConfigurationsResponse.configurations=1|Message|repeated|type=barkfluff.configuration.ConfigurationItem|oneof=-",
            "UpdateConfigurationRequest.section=1|String|singular|type=-|oneof=-", "UpdateConfigurationRequest.key=2|String|singular|type=-|oneof=-", "UpdateConfigurationRequest.value=3|String|singular|type=-|oneof=-",
            "UpdateConfigurationRequest.service_id=4|Int32|singular|type=-|oneof=-", "UpdateConfigurationRequest.edited_by=5|String|singular|type=-|oneof=-", "UpdateConfigurationRequest.edited_from=6|String|singular|type=-|oneof=-",
            "UpdateConfigurationResponse.success=1|Bool|singular|type=-|oneof=-", "UpdateConfigurationResponse.message=2|String|singular|type=-|oneof=-",
            "GetConfigurationHistoryRequest.section=1|String|singular|type=-|oneof=-", "GetConfigurationHistoryRequest.key=2|String|singular|type=-|oneof=-", "GetConfigurationHistoryRequest.service_id=3|Int32|singular|type=-|oneof=-", "GetConfigurationHistoryRequest.count=4|Int32|singular|type=-|oneof=-",
            "GetConfigurationHistoryResponse.revisions=1|Message|repeated|type=barkfluff.configuration.ConfigurationRevision|oneof=-",
            "ConfigurationRevision.id=1|Int64|singular|type=-|oneof=-", "ConfigurationRevision.section=2|String|singular|type=-|oneof=-", "ConfigurationRevision.key=3|String|singular|type=-|oneof=-", "ConfigurationRevision.service_id=4|Int32|singular|type=-|oneof=-",
            "ConfigurationRevision.previous_value=5|String|singular|type=-|oneof=-", "ConfigurationRevision.new_value=6|String|singular|type=-|oneof=-", "ConfigurationRevision.changed_at=7|Message|singular|type=google.protobuf.Timestamp|oneof=-",
            "ConfigurationRevision.changed_by=8|String|singular|type=-|oneof=-", "ConfigurationRevision.changed_from=9|String|singular|type=-|oneof=-", "ConfigurationRevision.change_kind=10|String|singular|type=-|oneof=-", "ConfigurationRevision.source_revision_id=11|Int64|singular|type=-|oneof=-",
            "RollbackConfigurationRequest.revision_id=1|Int64|singular|type=-|oneof=-", "RollbackConfigurationRequest.edited_by=2|String|singular|type=-|oneof=-", "RollbackConfigurationRequest.edited_from=3|String|singular|type=-|oneof=-",
            "RollbackConfigurationResponse.success=1|Bool|singular|type=-|oneof=-", "RollbackConfigurationResponse.message=2|String|singular|type=-|oneof=-",
            "GetReservedNamesResponse.names=1|String|repeated|type=-|oneof=-",
            "AddReservedNameRequest.name=1|String|singular|type=-|oneof=-", "AddReservedNameResponse.success=1|Bool|singular|type=-|oneof=-", "AddReservedNameResponse.message=2|String|singular|type=-|oneof=-",
            "UpdateReservedNameRequest.old_name=1|String|singular|type=-|oneof=-", "UpdateReservedNameRequest.new_name=2|String|singular|type=-|oneof=-", "UpdateReservedNameResponse.success=1|Bool|singular|type=-|oneof=-", "UpdateReservedNameResponse.message=2|String|singular|type=-|oneof=-",
            "DeleteReservedNameRequest.name=1|String|singular|type=-|oneof=-", "DeleteReservedNameResponse.success=1|Bool|singular|type=-|oneof=-", "DeleteReservedNameResponse.message=2|String|singular|type=-|oneof=-"
        ], fields);
    }

    private static string Format(IEnumerable<string> values)
    {
        var items = values.ToArray();
        return items.Length == 0 ? "-" : string.Join(',', items);
    }

    private static string GetTypeName(FieldDescriptor field) =>
        field.FieldType is FieldType.Message or FieldType.Group ? field.MessageType.FullName : "-";
}
