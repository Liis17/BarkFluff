using System.Globalization;
using BarkFluff.Client.Core.Infrastructure.Localization;
using BarkFluff.Client.Core.Services.Calls;
using BarkFluff.Client.Core.ViewModels;
using BarkFluff.Client.WinUI.Infrastructure.Calls;
using BarkFluff.Proto.Calls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace BarkFluff.Client.WinUI;

public sealed partial class MainWindow
{
    private AudioCallService _calls = null!;
    private ILocalizationService _callStrings = null!;
    private DispatcherQueueTimer _callTimer = null!;
    private IReadOnlyList<CallAudioDevice>? _callDevices;
    private Guid? _ringNotification;
    private bool _bindingCallDevices;
    private bool _bindingCallQuality;
    private Guid? _deviceCallId;
    private IReadOnlyList<long>? _participants;

    private void InitializeCalls()
    {
        _calls = App.Services.GetRequiredService<AudioCallService>();
        _callStrings = App.Services.GetRequiredService<ILocalizationService>();
        var media = App.Services.GetRequiredService<WebViewCallMedia>();
        media.Host = CallMediaHost;
        _callTimer = DispatcherQueue.CreateTimer();
        _callTimer.Interval = TimeSpan.FromSeconds(1);
        _callTimer.Tick += (_, _) => UpdateCallTimer();
        _calls.Changed += RenderCall;
        Closed += (_, _) =>
        {
            _calls.Stop();
            _calls.Changed -= RenderCall;
            media.Host = null;
            _callTimer.Stop();
        };
    }

    private void RenderCall()
    {
        var call = _calls.Current;
        var outcome = _calls.Outcome;
        CallPanel.Visibility = Show(call is not null || outcome is not null);
        CallPeer.Text = call is null ? string.Empty : call.PeerName ??
            string.Format(CultureInfo.CurrentCulture, _callStrings.GetString("Calls_Peer"), call.PeerId);
        CallPeer.Visibility = Show(call is not null);
        CallStatus.Text = _callStrings.GetString(call?.Phase switch
        {
            AudioCallPhase.Incoming => IncomingCallKey(call),
            AudioCallPhase.Active when call?.MediaType == CallMediaType.CallMediaVideo => "Calls_Video",
            AudioCallPhase.Outgoing => "Calls_Outgoing",
            AudioCallPhase.Connecting => "Calls_Connecting",
            AudioCallPhase.Active => "Calls_Active",
            _ => outcome ?? "Calls_Ended"
        });
        CallAccept.Visibility = Show(call?.Phase == AudioCallPhase.Incoming);
        CallEnd.Visibility = Show(call is not null);
        CallEnd.Content = _callStrings.GetString(call?.Phase == AudioCallPhase.Incoming ? "Calls_Reject" :
            call?.ChatId is not null ? "Calls_Leave" : "Calls_End");
        CallDismiss.Visibility = Show(call is null);
        var active = call?.Phase == AudioCallPhase.Active;
        CallControls.Visibility = Show(active);
        CallMediaHost.Height = active ? (call?.VideoVisible == true ? 164 : 44) : 1;
        CallMediaHost.Visibility = Show(active || call?.Phase == AudioCallPhase.Connecting);
        CallCameraToggle.IsChecked = call?.CameraOn == true;
        CallCameraToggle.Content = _callStrings.GetString(call?.CameraOn == true ? "Calls_CameraOff" : "Calls_CameraOn");
        CallNotice.Text = active && outcome is not null ? _callStrings.GetString(outcome) : string.Empty;
        CallNotice.Visibility = Show(CallNotice.Text.Length != 0);
        var participants = call?.ChatId is not null ? call.Participants : null;
        if (!ReferenceEquals(_participants, participants))
        {
            _participants = participants;
            CallParticipants.Text = participants is { Count: > 0 } users
                ? string.Join(", ", users.Select(ResolveCallParticipantName))
                : string.Empty;
        }
        CallParticipants.Visibility = Show(active && _participants is { Count: > 0 });
        _bindingCallQuality = true;
        try
        {
            CallAudioQuality.SelectedIndex = (int)(call?.AudioQuality ?? 0);
            CallVideoQuality.SelectedIndex = call?.VideoQuality ?? 0;
        }
        finally
        {
            _bindingCallQuality = false;
        }
        CallMute.IsChecked = call?.Muted == true;
        CallMute.Content = _callStrings.GetString(call?.Muted == true ? "Calls_Unmute" : "Calls_Mute");
        CallDuration.Visibility = Show(active);
        if (active && !_callTimer.IsRunning)
            _callTimer.Start();
        else if (!active && _callTimer.IsRunning)
            _callTimer.Stop();
        UpdateCallTimer();

        if (_deviceCallId != call?.Id || !ReferenceEquals(_callDevices, call?.Devices))
        {
            _callDevices = call?.Devices;
            _bindingCallDevices = true;
            try
            {
                if (_deviceCallId != call?.Id)
                {
                    CallMicrophone.SelectedItem = null;
                    CallSpeaker.SelectedItem = null;
                    CallCamera.SelectedItem = null;
                    _deviceCallId = call?.Id;
                }
                BindDevices(CallCamera, "videoinput", "Calls_Camera");
                BindDevices(CallMicrophone, "audioinput", "Calls_Microphone");
                BindDevices(CallSpeaker, "audiooutput", "Calls_Speaker");
            }
            finally
            {
                _bindingCallDevices = false;
            }
        }
        UpdateCallNotification();
    }

    private string ResolveCallParticipantName(long userId)
    {
        var messenger = App.Services.GetRequiredService<MessengerViewModel>();
        var name = messenger.Chats.FirstOrDefault(chat => chat.PeerUserId == userId)?.Title;
        if (string.IsNullOrWhiteSpace(name))
            name = messenger.SearchResults.FirstOrDefault(user => user.UserId == userId)?.DisplayName;
        return string.IsNullOrWhiteSpace(name)
            ? string.Format(CultureInfo.CurrentCulture, _callStrings.GetString("Calls_Peer"), userId)
            : name;
    }

    private void BindDevices(ComboBox control, string kind, string fallbackLabel)
    {
        var previous = (control.SelectedItem as DeviceChoice)?.Device.Id;
        var choices = (_callDevices ?? []).Where(device => device.Kind == kind)
            .Select((device, i) => new DeviceChoice(device, string.IsNullOrWhiteSpace(device.Label)
                ? _callStrings.GetString(fallbackLabel) + " " + (i + 1) : device.Label)).ToArray();
        control.ItemsSource = choices;
        control.SelectedItem = choices.FirstOrDefault(choice => choice.Device.Id == previous);
        control.IsEnabled = choices.Length > 0;
    }

    private sealed record DeviceChoice(CallAudioDevice Device, string Label);

    private void UpdateCallTimer()
    {
        if (_calls.Current?.ActiveSince is { } start)
        {
            var duration = DateTimeOffset.UtcNow - start;
            CallDuration.Text = duration.ToString(duration.TotalHours >= 1 ? @"h\:mm\:ss" : @"mm\:ss", CultureInfo.InvariantCulture);
        }
    }

    private void UpdateCallNotification()
    {
        var next = !_isWindowActive && _calls.Current is { Phase: AudioCallPhase.Incoming } call ? call.Id : (Guid?)null;
        if (next == _ringNotification)
            return;
        var app = (App)Application.Current;
        if (_ringNotification is { } old)
            app.RemoveCallNotification(old);
        _ringNotification = next;
        if (next is { } id)
            app.ShowCallNotification(id, _callStrings.GetString(IncomingCallKey(_calls.Current)));
    }

    private static string IncomingCallKey(AudioCallStatus? call) => call?.ChatId is not null
        ? call.MediaType == CallMediaType.CallMediaVideo ? "Calls_IncomingGroupVideo" : "Calls_IncomingGroup"
        : call?.MediaType == CallMediaType.CallMediaVideo ? "Calls_IncomingVideo" : "Calls_Incoming";

    private void OnCallCamera(object sender, RoutedEventArgs args)
    {
        _calls.ToggleCamera();
        CallCameraToggle.IsChecked = _calls.Current?.CameraOn == true;
    }

    private async void OnCallQualityChanged(object sender, SelectionChangedEventArgs args)
    {
        if (_bindingCallQuality || _calls is null || sender is not ComboBox { SelectedIndex: >= 0 } picker)
            return;
        if (picker == CallAudioQuality)
            await _calls.SetAudioQualityAsync((BarkFluff.Proto.Calls.CallAudioQuality)picker.SelectedIndex);
        else
            _calls.SetVideoQuality(picker.SelectedIndex);
        RenderCall();
    }

    private static Visibility Show(bool show) => show ? Visibility.Visible : Visibility.Collapsed;
    private async void OnCallAccept(object sender, RoutedEventArgs args) => await _calls.AcceptAsync();
    private async void OnCallEnd(object sender, RoutedEventArgs args) => await _calls.HangUpAsync();
    private void OnCallDismiss(object sender, RoutedEventArgs args) => _calls.DismissOutcome();
    private void OnCallMute(object sender, RoutedEventArgs args) => _calls.ToggleMute();
    private void OnCallDeviceChanged(object sender, SelectionChangedEventArgs args)
    {
        if (!_bindingCallDevices && sender is ComboBox { SelectedItem: DeviceChoice choice })
            _calls.SelectDevice(choice.Device);
    }
}
