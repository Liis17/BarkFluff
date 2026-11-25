using System.Text.Json;
using BarkFluff.Client.Core.Services.Calls;
using BarkFluff.Proto.Calls;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace BarkFluff.Client.WinUI.Infrastructure.Calls;

internal sealed class WebViewCallMedia : ICallMedia
{
    private const string DocumentHost = "call.barkfluff.invalid";
    private const string DocumentOrigin = "https://" + DocumentHost;
    private const string DocumentUrl = DocumentOrigin + "/room.html";
    private const string AssetHost = "call-assets.barkfluff.invalid";
    private const string AssetOrigin = "https://" + AssetHost;

    private WebView2? _view;
    private Guid _callId;
    public Panel? Host { get; set; }
    public event Action<CallMediaEvent>? Received;

    public async Task ConnectAsync(Guid callId, Uri url, string token, CancellationToken ct, bool video, CallAudioQuality quality)
    {
        Disconnect();
        var host = Host ?? throw new InvalidOperationException();
        var view = _view = new WebView2 { IsTabStop = true, DefaultBackgroundColor = Microsoft.UI.Colors.Transparent };
        _callId = callId;
        host.Children.Add(view);
        try
        {
            if (string.IsNullOrEmpty(CoreWebView2Environment.GetAvailableBrowserVersionString(null)))
                throw new CallRuntimeException();
            await view.EnsureCoreWebView2Async();
        }
        catch (Exception ex) when (ex.HResult is unchecked((int)0x80070002) or unchecked((int)0x80040154) or unchecked((int)0x80070490))
        {
            throw new CallRuntimeException();
        }
        if (_view != view || ct.IsCancellationRequested)
            return;

        var core = view.CoreWebView2;
        // ScreenCaptureStarting доступен начиная со стабильного WebView2 Runtime 131.
        var canShare = Version.TryParse(core.Environment.BrowserVersionString.Split(' ', 2)[0], out var browserVersion) &&
            browserVersion >= new Version(131, 0, 2903, 40);
        var directory = Path.Combine(AppContext.BaseDirectory, "Resources", "Calls");
        var html = File.ReadAllBytes(Path.Combine(directory, "room.html"));
        // Документ и виртуальная папка используют разные хосты: WebView2 не
        // перехватывает запросы к SetVirtualHostNameToFolderMapping.
        core.SetVirtualHostNameToFolderMapping(AssetHost, directory, CoreWebView2HostResourceAccessKind.DenyCors);
        var wss = url.GetLeftPart(UriPartial.Authority);
        var https = new UriBuilder(url) { Scheme = "https" }.Uri.GetLeftPart(UriPartial.Authority);
        var headers = "Content-Type: text/html; charset=utf-8\r\n" +
            $"Permissions-Policy: camera=(self), microphone=(self), display-capture={(canShare ? "(self)" : "()")}\r\n" +
            "Cache-Control: no-store\r\n" +
            "X-Content-Type-Options: nosniff\r\n" +
            $"Content-Security-Policy: default-src 'none'; script-src {AssetOrigin}; style-src {AssetOrigin}; connect-src {wss} {https}; " +
            "media-src blob:; base-uri 'none'; form-action 'none'; frame-ancestors 'none'; object-src 'none'";

#if !DEBUG
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.AreBrowserAcceleratorKeysEnabled = false;
#endif
        core.Settings.AreHostObjectsAllowed = false;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.AreDefaultScriptDialogsEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.AddWebResourceRequestedFilter(DocumentUrl, CoreWebView2WebResourceContext.Document,
            CoreWebView2WebResourceRequestSourceKinds.Document);
        core.WebResourceRequested += (_, args) =>
        {
            if (args.Request.Uri == DocumentUrl)
                args.Response = core.Environment.CreateWebResourceResponse(
                    new MemoryStream(html, writable: false).AsRandomAccessStream(), 200, "OK", headers);
        };

        var allowInitialNavigation = true;
        core.NavigationStarting += (_, args) =>
        {
            args.Cancel = !allowInitialNavigation || args.Uri != DocumentUrl || args.IsRedirected;
            if (!args.Cancel)
                allowInitialNavigation = false;
        };
        core.FrameNavigationStarting += (_, args) => args.Cancel = true;
        core.NewWindowRequested += (_, args) => args.Handled = true;
        core.DownloadStarting += (_, args) => args.Cancel = true;
        core.PermissionRequested += (_, args) =>
        {
            // Разрешаем воспроизведение, инициированное кнопкой WinUI вне документа.
            args.State = _view == view && core.Source == DocumentUrl &&
                args.PermissionKind is (CoreWebView2PermissionKind.Microphone or CoreWebView2PermissionKind.Camera or CoreWebView2PermissionKind.Autoplay) &&
                CallMediaBridge.MatchesOrigin(args.Uri, DocumentOrigin)
                ? CoreWebView2PermissionState.Allow : CoreWebView2PermissionState.Deny;
            args.SavesInProfile = false;
        };
        if (canShare)
        {
            core.ScreenCaptureStarting += (_, args) =>
            {
                args.Cancel = _view != view || core.Source != DocumentUrl ||
                    args.OriginalSourceFrameInfo?.Source != DocumentUrl;
            };
        }
        core.WebMessageReceived += (_, args) =>
        {
            if (_view == view && args.Source == DocumentUrl &&
                CallMediaBridge.TryRead(args.WebMessageAsJson, callId, out var message))
                Received?.Invoke(message!);
        };
        core.ProcessFailed += (_, _) => Fail(view, callId);
        core.NavigationCompleted += OnNavigated;
        core.Navigate(DocumentUrl);

        void OnNavigated(CoreWebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
        {
            sender.NavigationCompleted -= OnNavigated;
            if (_view != view)
                return;
            if (!args.IsSuccess || sender.Source != DocumentUrl)
            {
                Fail(view, callId);
                return;
            }
            try
            {
                sender.PostWebMessageAsJson(JsonSerializer.Serialize(new
                {
                    type = "connect", call_id = callId, url = url.AbsoluteUri, token, video, canShare,
                    audioQuality = (int)quality,
                    lang = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ru" ? "ru" : "en"
                }));
            }
            catch (Exception)
            {
                Fail(view, callId);
            }
        }
    }

    public void Send(Guid callId, string type, string? deviceId = null)
    {
        if (_view is not { CoreWebView2: { } core } view || _callId != callId || core.Source != DocumentUrl)
            return;
        try
        {
            var json = deviceId is null
                ? JsonSerializer.Serialize(new { type, call_id = callId })
                : JsonSerializer.Serialize(new { type, call_id = callId, deviceId });
            core.PostWebMessageAsJson(json);
        }
        catch (Exception)
        {
            Fail(view, callId);
        }
    }

    public void Disconnect()
    {
        if (_view is not { } view)
            return;
        var id = _callId;
        _view = null;
        _callId = Guid.Empty;
        try
        {
            if (view.CoreWebView2 is { Source: DocumentUrl } core)
                core.PostWebMessageAsJson(JsonSerializer.Serialize(new { type = "disconnect", call_id = id }));
        }
        catch (Exception)
        {
            // Закрытие WebView освобождает устройства и после сбоя процесса.
        }
        finally
        {
            Host?.Children.Remove(view);
            try
            {
                view.Close();
            }
            catch (Exception)
            {
                // Контрол уже мог закрыться при завершении окна.
            }
        }
    }

    private void Fail(WebView2 view, Guid id)
    {
        if (_view == view)
            Received?.Invoke(new(id, "error"));
    }
}
