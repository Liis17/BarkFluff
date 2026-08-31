using BarkFluff.Settings.Domain;
using BarkFluff.Settings.Persistence.Contexts;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

using Xunit;

namespace BarkFluff.Settings.Tests.Persistence;

public sealed class SettingsContextModelTests
{
    private static readonly string[] ExpectedSettingsTables =
    [
        "GlobalSettings",
        "IdentitySettings",
        "UsersSettings",
        "BeaconSettings",
        "NotificationsSettings",
        "FilesSettings",
        "MessagesSettings",
        "FastAuthSettings",
        "UpdatesSettings",
        "OnlinerSettings",
        "CloudMessagingSettings",
        "WebSettings",
        "DevelopersSettings",
        "CallsSettings",
        "BotsSettings",
        "FederationSettings"
    ];

    [Fact]
    public void Model_contains_one_four_column_table_per_settings_scope()
    {
        using var context = CreateContext();

        var settingEntities = context.Model.GetEntityTypes()
            .Where(entity => entity.ClrType == typeof(SettingRow))
            .OrderBy(entity => entity.GetTableName(), StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(ExpectedSettingsTables.Order(StringComparer.Ordinal), settingEntities.Select(x => x.GetTableName()));

        foreach (var entity in settingEntities)
        {
            Assert.Equal(["EditedAt", "EditedBy", "Key", "Value"], entity.GetProperties().Select(x => x.Name).Order(StringComparer.Ordinal));
            Assert.Equal("Key", Assert.Single(entity.FindPrimaryKey()!.Properties).Name);
        }
    }

    [Fact]
    public void History_and_reserved_names_do_not_persist_service_id()
    {
        using var context = CreateContext();

        var history = context.Model.FindEntityType(typeof(SettingRevision))!;
        Assert.Equal("SettingsHistory", history.GetTableName());
        Assert.DoesNotContain(history.GetProperties(), property => property.Name == "ServiceId");
        Assert.Equal(
            ["ChangeKind", "ChangedAt", "ChangedBy", "ChangedFrom", "Id", "Key", "NewValue", "PreviousValue", "SettingsTable", "SourceRevisionId"],
            history.GetProperties().Select(x => x.Name).Order(StringComparer.Ordinal));

        var reservedName = context.Model.FindEntityType(typeof(ReservedName))!;
        Assert.Equal("ReservedNames", reservedName.GetTableName());
        Assert.Equal("Name", Assert.Single(reservedName.FindPrimaryKey()!.Properties).Name);

        var setupState = context.Model.FindEntityType(typeof(SetupState))!;
        Assert.Equal("SetupState", setupState.GetTableName());
        Assert.Equal("Id", Assert.Single(setupState.FindPrimaryKey()!.Properties).Name);
    }

    private static SettingsContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<SettingsContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new SettingsContext(options);
    }
}
