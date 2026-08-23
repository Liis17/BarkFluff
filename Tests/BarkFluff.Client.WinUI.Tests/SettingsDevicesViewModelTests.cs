using BarkFluff.Client.Core.Services;
using BarkFluff.Client.Core.ViewModels.Settings;
using BarkFluff.Proto.Identity;
using BarkFluff.Proto.Users;

namespace BarkFluff.Client.WinUI.Tests;

public sealed class SettingsDevicesViewModelTests
{
    [Fact]
    public async Task LoadAsync_WhenCurrentDevicePayloadIsMissing_ShowsLoadErrorInsteadOfThrowing()
    {
        var viewModel = new SettingsDevicesViewModel(new FakeDeviceSettingsService
        {
            CurrentDevice = null,
            Sessions = []
        });

        await viewModel.LoadAsync();

        Assert.Equal("Error_SettingsLoadFailed", viewModel.ErrorMessage);
    }

    [Fact]
    public async Task LoadAsync_WhenSessionsPayloadIsMissing_ShowsLoadErrorInsteadOfThrowing()
    {
        var viewModel = new SettingsDevicesViewModel(new FakeDeviceSettingsService
        {
            CurrentDevice = new Device { DeviceId = "current" },
            Sessions = null
        });

        await viewModel.LoadAsync();

        Assert.Equal("Error_SettingsLoadFailed", viewModel.ErrorMessage);
    }
}

internal sealed class FakeDeviceSettingsService : IDeviceSettingsService
{
    public Device? CurrentDevice { get; set; } = new() { DeviceId = "current" };

    public List<GetActiveSessionsResponse.Types.Session>? Sessions { get; set; } = [];

    public Task<(string? ErrorKey, Device? Device)> GetCurrentDeviceAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<(string?, Device?)>((null, CurrentDevice));

    public Task<(string? ErrorKey, List<GetActiveSessionsResponse.Types.Session>? Sessions)> GetDevicesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<(string?, List<GetActiveSessionsResponse.Types.Session>?)>((null, Sessions));

    public Task<string?> RemoveSessionAsync(string deviceId, CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(null);

    public Task<string?> SetNotificationsEnabledAsync(bool enabled, CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(null);
}
