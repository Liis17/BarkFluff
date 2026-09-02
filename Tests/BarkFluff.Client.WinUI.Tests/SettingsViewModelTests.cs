using BarkFluff.Client.Core.Models;
using BarkFluff.Client.Core.Services;
using BarkFluff.Client.Core.ViewModels;

namespace BarkFluff.Client.WinUI.Tests;

public sealed class SettingsViewModelTests
{
    [Fact]
    public async Task LoadAsync_WithoutStoredValue_KeepsExitAndPersistsIt()
    {
        var dataStore = new TestApplicationDataStore();
        var viewModel = CreateViewModel(dataStore);

        await viewModel.LoadAsync();

        Assert.Equal(WindowClosingBehavior.Exit, viewModel.ClosingBehavior);
        Assert.True(viewModel.ExitsOnClose);
        Assert.False(viewModel.MinimizesToTray);
        Assert.Equal(WindowClosingBehavior.Exit, dataStore.StoredClosingBehavior);
        Assert.True(viewModel.RememberWindowSize);
        Assert.Equal(WindowPreferences.DefaultWidth, viewModel.WindowWidth);
        Assert.Equal(WindowPreferences.DefaultHeight, viewModel.WindowHeight);
        Assert.Equal("1000 × 800", viewModel.WindowSizeDescription);
        Assert.Equal("1000 × 800", viewModel.WindowBoundsDescription);
    }

    [Fact]
    public async Task LoadAsync_WithStoredValue_RestoresIt()
    {
        var dataStore = new TestApplicationDataStore { StoredClosingBehavior = WindowClosingBehavior.MinimizeToTray };
        var viewModel = CreateViewModel(dataStore);

        await viewModel.LoadAsync();

        Assert.True(viewModel.MinimizesToTray);
        Assert.Equal(0, dataStore.ClosingBehaviorSaveCount);
    }

    [Fact]
    public void MinimizesToTray_WhenSelected_PersistsBehaviourOnce()
    {
        var dataStore = new TestApplicationDataStore();
        var viewModel = CreateViewModel(dataStore);

        viewModel.MinimizesToTray = true;
        viewModel.MinimizesToTray = true;

        Assert.Equal(WindowClosingBehavior.MinimizeToTray, viewModel.ClosingBehavior);
        Assert.Equal(WindowClosingBehavior.MinimizeToTray, dataStore.StoredClosingBehavior);
        Assert.Equal(1, dataStore.ClosingBehaviorSaveCount);
    }

    [Fact]
    public async Task LoadAsync_WithStoredWindowPreferences_RestoresThem()
    {
        var dataStore = new TestApplicationDataStore();
        var preferences = new SettingsPreferences(dataStore);
        await preferences.SaveWindowPreferencesAsync(new WindowPreferences
        {
            RememberSize = false,
            Width = 1280,
            Height = 720,
            PositionX = -100,
            PositionY = 200
        });

        var viewModel = CreateViewModel(dataStore);

        await viewModel.LoadAsync();

        Assert.False(viewModel.RememberWindowSize);
        Assert.Equal(1280, viewModel.WindowWidth);
        Assert.Equal(720, viewModel.WindowHeight);
        Assert.Equal(-100, viewModel.WindowPositionX);
        Assert.Equal(200, viewModel.WindowPositionY);
        Assert.Equal("1280 × 720 (X: -100, Y: 200)", viewModel.WindowBoundsDescription);
    }

    [Fact]
    public async Task SaveWindowSizeAsync_WhenRememberingIsEnabled_PersistsNewSize()
    {
        var dataStore = new TestApplicationDataStore();
        var viewModel = CreateViewModel(dataStore);

        await viewModel.LoadAsync();
        await viewModel.SaveWindowSizeAsync(1280, 720);

        var stored = await new SettingsPreferences(dataStore).GetWindowPreferencesAsync();
        Assert.Equal(1280, stored.Width);
        Assert.Equal(720, stored.Height);
        Assert.Equal("1280 × 720", viewModel.WindowSizeDescription);
    }

    [Fact]
    public async Task SaveWindowBoundsAsync_PersistsSizeAndPosition()
    {
        var dataStore = new TestApplicationDataStore();
        var viewModel = CreateViewModel(dataStore);

        await viewModel.LoadAsync();
        await viewModel.SaveWindowBoundsAsync(1280, 720, -100, 200);

        var stored = await new SettingsPreferences(dataStore).GetWindowPreferencesAsync();
        Assert.Equal(1280, stored.Width);
        Assert.Equal(720, stored.Height);
        Assert.Equal(-100, stored.PositionX);
        Assert.Equal(200, stored.PositionY);
        Assert.Equal("1280 × 720 (X: -100, Y: 200)", viewModel.WindowBoundsDescription);
    }

    [Fact]
    public async Task SaveWindowSizeAsync_WhenRememberingIsDisabled_LeavesStoredSizeUnchanged()
    {
        var dataStore = new TestApplicationDataStore();
        var viewModel = CreateViewModel(dataStore);

        await viewModel.LoadAsync();
        await viewModel.SaveWindowSizeAsync(1280, 720);
        await viewModel.SetRememberWindowSizeAsync(false);
        await viewModel.SaveWindowBoundsAsync(1440, 900, -100, 200);

        var stored = await new SettingsPreferences(dataStore).GetWindowPreferencesAsync();
        Assert.False(stored.RememberSize);
        Assert.Equal(1280, stored.Width);
        Assert.Equal(720, stored.Height);
        Assert.Null(stored.PositionX);
        Assert.Null(stored.PositionY);
    }

    [Fact]
    public async Task ResetWindowSizeAsync_RestoresDefaultsAndRaisesEvent()
    {
        var dataStore = new TestApplicationDataStore();
        var viewModel = CreateViewModel(dataStore);
        var resetRaised = false;
        viewModel.WindowBoundsResetRequested += (_, _) => resetRaised = true;

        await viewModel.LoadAsync();
        await viewModel.SaveWindowBoundsAsync(1280, 720, -100, 200);
        await viewModel.ResetWindowSizeAsync();

        var stored = await new SettingsPreferences(dataStore).GetWindowPreferencesAsync();
        Assert.True(resetRaised);
        Assert.Equal(WindowPreferences.DefaultWidth, stored.Width);
        Assert.Equal(WindowPreferences.DefaultHeight, stored.Height);
        Assert.Null(stored.PositionX);
        Assert.Null(stored.PositionY);
        Assert.Equal(WindowPreferences.DefaultWidth, viewModel.WindowWidth);
        Assert.Equal(WindowPreferences.DefaultHeight, viewModel.WindowHeight);
        Assert.Null(viewModel.WindowPositionX);
        Assert.Null(viewModel.WindowPositionY);
    }

    private static SettingsViewModel CreateViewModel(TestApplicationDataStore dataStore) =>
        new(dataStore, new SettingsPreferences(dataStore));
}
