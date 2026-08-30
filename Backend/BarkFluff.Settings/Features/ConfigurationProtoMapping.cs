using BarkFluff.Proto.Configuration;
using BarkFluff.Settings.Catalog;
using BarkFluff.Settings.Domain;
using BarkFluff.Settings.Persistence.Services;
using BarkFluff.Shared.Identity;

using Google.Protobuf.WellKnownTypes;

namespace BarkFluff.Settings.Features;

internal static class ConfigurationProtoMapping
{
    public static ConfigurationItem ToProto(StoredSetting setting) => new()
    {
        Section = setting.Section,
        Key = setting.Key,
        Value = setting.Value,
        EditedAt = Timestamp.FromDateTime(setting.EditedAt),
        EditedBy = setting.EditedBy,
        EditedFrom = setting.EditedFrom,
        ServiceId = (int)setting.ServiceId
    };

    public static BarkFluff.Proto.Configuration.ConfigurationRevision ToProto(SettingRevision revision, ServiceId serviceId)
    {
        var entry = SettingsCatalog.Resolve(serviceId, revision.Key);
        return new BarkFluff.Proto.Configuration.ConfigurationRevision
        {
            Id = revision.Id,
            Section = entry.Section,
            Key = entry.Key,
            ServiceId = (int)serviceId,
            PreviousValue = revision.PreviousValue,
            NewValue = revision.NewValue,
            ChangedAt = Timestamp.FromDateTime(DateTime.SpecifyKind(revision.ChangedAt, DateTimeKind.Utc)),
            ChangedBy = revision.ChangedBy,
            ChangedFrom = revision.ChangedFrom,
            ChangeKind = revision.ChangeKind,
            SourceRevisionId = revision.SourceRevisionId ?? 0
        };
    }
}
