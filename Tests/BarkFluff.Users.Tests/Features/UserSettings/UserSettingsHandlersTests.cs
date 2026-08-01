using BarkFluff.Users.Features.Personalization.UpdatePersonalization;
using BarkFluff.Users.Features.UserSettings.GetUserSettings;
using BarkFluff.Users.Features.UserSettings.SetChatBackground;
using BarkFluff.Users.Features.UserSettings.SetGlobalChatBackground;

using FluentAssertions;

namespace BarkFluff.Users.Tests.Features.UserSettings;

public class UserSettingsHandlersTests : IAsyncDisposable
{
    private readonly TestHelper _h = new();

    [Fact]
    public async Task Get_CreatesEmptySettings()
    {
        var user = await _h.SeedUser();
        var handler = new GetUserSettingsQueryHandler(_h.CreateUserContext(user.Id), _h.UserSettingsStorage);

        var result = await handler.Handle(new GetUserSettingsQuery(), CancellationToken.None);

        result.Settings.GlobalChatBackgroundFileId.Should().BeEmpty();
        result.Settings.ChatBackgrounds.Should().BeEmpty();
        (await _h.UserSettingsStorage.Get(user.Id)).Should().NotBeNull();
    }

    [Fact]
    public async Task SetGlobalAndChatBackground_ReturnsBothSettings()
    {
        var user = await _h.SeedUser();
        var context = _h.CreateUserContext(user.Id);
        var chatId = Guid.NewGuid();

        await _h.PersonalizationStorage.Update(user.Id, null, ["global", "chat"]);

        await new SetGlobalChatBackgroundCommandHandler(context, _h.UserSettingsStorage, _h.PersonalizationStorage)
            .Handle(new SetGlobalChatBackgroundCommand { FileId = "global" }, CancellationToken.None);
        await new SetChatBackgroundCommandHandler(context, _h.UserSettingsStorage, _h.PersonalizationStorage)
            .Handle(new SetChatBackgroundCommand { ChatId = chatId, FileId = "chat" }, CancellationToken.None);

        var result = await new GetUserSettingsQueryHandler(context, _h.UserSettingsStorage)
            .Handle(new GetUserSettingsQuery(), CancellationToken.None);

        result.Settings.GlobalChatBackgroundFileId.Should().Be("global");
        result.Settings.ChatBackgrounds.Should().ContainSingle(x => x.ChatId == chatId.ToString()
            && x.ChatBackgroundFileId == "chat");
    }

    [Fact]
    public async Task SetChatBackground_EmptyFileId_RemovesOverride()
    {
        var user = await _h.SeedUser();
        var context = _h.CreateUserContext(user.Id);
        var chatId = Guid.NewGuid();
        await _h.PersonalizationStorage.Update(user.Id, null, ["chat"]);
        var handler = new SetChatBackgroundCommandHandler(context, _h.UserSettingsStorage, _h.PersonalizationStorage);

        await handler.Handle(new SetChatBackgroundCommand { ChatId = chatId, FileId = "chat" }, CancellationToken.None);
        await handler.Handle(new SetChatBackgroundCommand { ChatId = chatId, FileId = null }, CancellationToken.None);

        (await _h.UserSettingsStorage.GetChatSettings(user.Id)).Should().BeEmpty();
    }

    [Fact]
    public async Task SetBackground_RejectsFileOutsidePersonalizationGallery()
    {
        var user = await _h.SeedUser();
        var context = _h.CreateUserContext(user.Id);
        var handler = new SetGlobalChatBackgroundCommandHandler(
            context,
            _h.UserSettingsStorage,
            _h.PersonalizationStorage);

        var action = () => handler.Handle(
            new SetGlobalChatBackgroundCommand { FileId = "not-in-gallery" },
            CancellationToken.None);

        await action.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task UpdatePersonalization_RemovedBackground_ClearsGlobalAndChatReferences()
    {
        var user = await _h.SeedUser();
        var context = _h.CreateUserContext(user.Id);
        await _h.PersonalizationStorage.Update(user.Id, null, ["removed", "kept"]);
        await _h.UserSettingsStorage.SetGlobalChatBackground(user.Id, "removed");
        await _h.UserSettingsStorage.SetChatBackground(user.Id, Guid.NewGuid(), "removed");
        await _h.UserSettingsStorage.SetChatBackground(user.Id, Guid.NewGuid(), "kept");
        var handler = new UpdatePersonalizationCommandHandler(
            context,
            _h.PersonalizationStorage,
            _h.UserSettingsStorage,
            TestHelper.CreateLogger<UpdatePersonalizationCommandHandler>());

        await handler.Handle(new UpdatePersonalizationCommand
        {
            Personalization = new Proto.Users.UserPersonalizationData { ChatBackgroundFileIds = { "kept" } },
        }, CancellationToken.None);

        (await _h.UserSettingsStorage.Get(user.Id))!.GlobalChatBackgroundFileId.Should().BeNull();
        var chatSettings = await _h.UserSettingsStorage.GetChatSettings(user.Id);
        chatSettings.Should().ContainSingle(x => x.ChatBackgroundFileId == "kept");
    }

    public ValueTask DisposeAsync() => _h.DbContext.DisposeAsync();
}
