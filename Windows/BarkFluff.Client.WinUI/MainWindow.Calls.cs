using System.Globalization;
using BarkFluff.Client.Core.Infrastructure.Localization;
using BarkFluff.Client.Core.Services.Calls;
using BarkFluff.Client.WinUI.Infrastructure.Calls;
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
            AudioCallPhase.Incoming => "Calls_Incoming",
            AudioCallPhase.Outgoing => "Calls_Outgoing",
            AudioCallPhase.Connecting => "Calls_Connecting",
            AudioCallPhase.Active => "Calls_Active",
            _ => outcome ?? "Calls_Ended"
        });
        CallAccept.Visibility = Show(call?.Phase == AudioCallPhase.Incoming);
        CallEnd.Visibility = Show(call is not null);
        CallEnd.Content = _callStrings.GetString(call?.Phase == AudioCallPhase.Incoming ? "Calls_Reject" : "Calls_End");
        CallDismiss.Visibility = Show(call is null);
        var active = call?.Phase == AudioCallPhase.Active;
        CallControls.Visibility = Show(active);
        CallMute.Content = _callStrings.GetString(call?.Muted == true ? "Calls_Unmute" : "Calls_Mute");
        CallDuration.Visibility = Show(active);
        if (active && !_callTimer.IsRunning)
            _callTimer.Start();
        else if (!active && _callTimer.IsRunning)
            _callTimer.Stop();
        UpdateCallTimer();

        if (!ReferenceEquals(_callDevices, call?.Devices))
        {
            _callDevices = call?.Devices;
            _bindingCallDevices = true;
            try
            {
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
            app.ShowCallNotification(id, _callStrings.GetString("Calls_Incoming"));
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
