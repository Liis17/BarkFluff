using BarkFluff.Client.Core.Models;
using BarkFluff.Client.Core.ViewModels;

namespace BarkFluff.Client.WinUI.Tests;

public sealed class SettingsViewModelTests
{
    [Fact]
    public async Task LoadAsync_WithoutStoredValue_KeepsExitAndPersistsIt()
    {
        var dataStore = new TestApplicationDataStore();
        var viewModel = new SettingsViewModel(dataStore);

        await viewModel.LoadAsync();

        Assert.Equal(WindowClosingBehavior.Exit, viewModel.ClosingBehavior);
        Assert.True(viewModel.ExitsOnClose);
        Assert.False(viewModel.MinimizesToTray);
        Assert.Equal(WindowClosingBehavior.Exit, dataStore.StoredClosingBehavior);
    }

    [Fact]
    public async Task LoadAsync_WithStoredValue_RestoresIt()
    {
        var dataStore = new TestApplicationDataStore { StoredClosingBehavior = WindowClosingBehavior.MinimizeToTray };
        var viewModel = new SettingsViewModel(dataStore);

        await viewModel.LoadAsync();

        Assert.True(viewModel.MinimizesToTray);
        Assert.Equal(0, dataStore.ClosingBehaviorSaveCount);
    }

    [Fact]
    public void MinimizesToTray_WhenSelected_PersistsBehaviourOnce()
    {
        var dataStore = new TestApplicationDataStore();
        var viewModel = new SettingsViewModel(dataStore);

        viewModel.MinimizesToTray = true;
        viewModel.MinimizesToTray = true;

        Assert.Equal(WindowClosingBehavior.MinimizeToTray, viewModel.ClosingBehavior);
        Assert.Equal(WindowClosingBehavior.MinimizeToTray, dataStore.StoredClosingBehavior);
        Assert.Equal(1, dataStore.ClosingBehaviorSaveCount);
    }
}
