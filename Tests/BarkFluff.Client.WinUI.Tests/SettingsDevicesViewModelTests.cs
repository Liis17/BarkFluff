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

    [Fact]
    public async Task LoadAsync_ExcludesCurrentDeviceFromOtherDevices()
    {
        var service = new FakeDeviceSettingsService
        {
            Sessions =
            [
                new() { DeviceId = "current" },
                new() { DeviceId = "other-1", OriginalName = "Phone" },
                new() { DeviceId = "other-2", OriginalName = "Tablet" }
            ]
        };
        var viewModel = new SettingsDevicesViewModel(service);

        await viewModel.LoadAsync();

        Assert.Equal(["other-1", "other-2"], viewModel.OtherDevices.Select(item => item.DeviceId));
        Assert.True(viewModel.HasOtherDevices);
    }

    [Fact]
    public async Task RemoveAllAsync_RemovesAllOtherDevicesWithoutTouchingCurrentDevice()
    {
        var service = new FakeDeviceSettingsService
        {
            Sessions =
            [
                new() { DeviceId = "current" },
                new() { DeviceId = "other-1" },
                new() { DeviceId = "other-2" }
            ]
        };
        var viewModel = new SettingsDevicesViewModel(service);
        await viewModel.LoadAsync();

        await viewModel.RemoveAllAsync();

        Assert.Equal(["other-1", "other-2"], service.AttemptedDeviceIds);
        Assert.Empty(viewModel.OtherDevices);
        Assert.False(viewModel.HasOtherDevices);
    }

    [Fact]
    public async Task RemoveAllAsync_WhenThereAreNoOtherDevices_DoesNotCallService()
    {
        var service = new FakeDeviceSettingsService
        {
            Sessions = [new() { DeviceId = "current" }]
        };
        var viewModel = new SettingsDevicesViewModel(service);
        await viewModel.LoadAsync();

        await viewModel.RemoveAllAsync();

        Assert.Empty(service.AttemptedDeviceIds);
        Assert.False(viewModel.HasOtherDevices);
    }

    [Fact]
    public async Task RemoveAllAsync_WhenOneRemovalFails_ContinuesAndKeepsFailedDevice()
    {
        var service = new FakeDeviceSettingsService
        {
            Sessions =
            [
                new() { DeviceId = "current" },
                new() { DeviceId = "other-1" },
                new() { DeviceId = "other-2" }
            ]
        };
        service.FailedDeviceIds.Add("other-1");
        var viewModel = new SettingsDevicesViewModel(service);
        await viewModel.LoadAsync();

        await viewModel.RemoveAllAsync();

        Assert.Equal(["other-1", "other-2"], service.AttemptedDeviceIds);
        Assert.Equal(["other-1"], viewModel.OtherDevices.Select(item => item.DeviceId));
        Assert.True(viewModel.HasOtherDevices);
    }
}

internal sealed class FakeDeviceSettingsService : IDeviceSettingsService
{
    public Device? CurrentDevice { get; set; } = new() { DeviceId = "current" };

    public List<GetActiveSessionsResponse.Types.Session>? Sessions { get; set; } = [];

    public List<string> AttemptedDeviceIds { get; } = [];

    public HashSet<string> FailedDeviceIds { get; } = [];

    public Task<(string? ErrorKey, Device? Device)> GetCurrentDeviceAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<(string?, Device?)>((null, CurrentDevice));

    public Task<(string? ErrorKey, List<GetActiveSessionsResponse.Types.Session>? Sessions)> GetDevicesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<(string?, List<GetActiveSessionsResponse.Types.Session>?)>((null, Sessions));

    public Task<string?> RemoveSessionAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        AttemptedDeviceIds.Add(deviceId);
        return Task.FromResult<string?>(FailedDeviceIds.Contains(deviceId) ? "Error_SettingsSaveFailed" : null);
    }

    public Task<string?> SetNotificationsEnabledAsync(bool enabled, CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(null);
}
