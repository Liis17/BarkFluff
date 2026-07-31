using BarkFluff.Client.Core.Models;
using BarkFluff.Client.Core.Services;

namespace BarkFluff.Client.WinUI.Tests;

public sealed class SettingsPreferencesTests
{
    [Fact]
    public async Task GetInterfacePreferencesAsync_WithoutStoredValue_ReturnsDefaults()
    {
        var service = new SettingsPreferences(new TestApplicationDataStore());

        var preferences = await service.GetInterfacePreferencesAsync();

        Assert.Equal(20, preferences.ChatCornerRadius);
        Assert.Equal(10, preferences.ChatBackgroundBlurRadius);
        Assert.True(preferences.RelativeOnlineTime);
        Assert.Equal(160, preferences.ChatStickerSizeDp);
    }

    [Fact]
    public async Task SaveInterfacePreferencesAsync_ClampsValues()
    {
        var service = new SettingsPreferences(new TestApplicationDataStore());

        await service.SaveInterfacePreferencesAsync(new InterfacePreferences
        {
            ChatCornerRadius = 100,
            ChatBackgroundBlurRadius = 0,
            ChatBackgroundDim = -1,
            ChatStickerSizeDp = 500
        });

        var preferences = await service.GetInterfacePreferencesAsync();

        Assert.Equal(30, preferences.ChatCornerRadius);
        Assert.Equal(1, preferences.ChatBackgroundBlurRadius);
        Assert.Equal(0, preferences.ChatBackgroundDim);
        Assert.Equal(240, preferences.ChatStickerSizeDp);
    }
}
