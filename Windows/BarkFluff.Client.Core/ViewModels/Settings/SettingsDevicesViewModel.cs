using BarkFluff.Client.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
namespace BarkFluff.Client.Core.ViewModels.Settings;
public sealed partial class SettingsDevicesViewModel(IDeviceSettingsService service):ObservableObject
{
 [ObservableProperty] private string _currentDeviceName=string.Empty; [ObservableProperty] private string _currentDeviceDetails=string.Empty; [ObservableProperty] private string? _errorMessage;
 public ObservableCollection<DeviceSessionItem> OtherDevices {get;}=[];
 public bool HasOtherDevices => OtherDevices.Count > 0;
 public async Task LoadAsync(){var (a,current)=await service.GetCurrentDeviceAsync(); var (b,sessions)=await service.GetDevicesAsync(); if(a is not null||b is not null||current is null||sessions is null){ErrorMessage=a??b??"Error_SettingsLoadFailed";return;} CurrentDeviceName=string.IsNullOrWhiteSpace(current.CustomName)?current.OriginalName:current.CustomName;CurrentDeviceDetails=$"{current.OperationSystem} · {current.AppName}";OtherDevices.Clear();foreach(var s in sessions.Where(s=>s.DeviceId!=current.DeviceId))OtherDevices.Add(new(s.DeviceId,string.IsNullOrWhiteSpace(s.CustomName)?s.OriginalName:s.CustomName,$"{s.OperationSystem} · {s.AppName}"));OnPropertyChanged(nameof(HasOtherDevices));}
 public async Task RemoveAsync(DeviceSessionItem item){if(await service.RemoveSessionAsync(item.DeviceId) is null){OtherDevices.Remove(item);OnPropertyChanged(nameof(HasOtherDevices));}}
 public async Task RemoveAllAsync(){foreach(var item in OtherDevices.ToArray()){if(await service.RemoveSessionAsync(item.DeviceId) is null)OtherDevices.Remove(item);}OnPropertyChanged(nameof(HasOtherDevices));}
}
public sealed record DeviceSessionItem(string DeviceId,string Name,string Details);
