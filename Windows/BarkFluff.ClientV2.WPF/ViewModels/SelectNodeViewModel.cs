using BarkFluff.ClientV2.WPF.Infrastructure.Localization;
using BarkFluff.ClientV2.WPF.Models;
using BarkFluff.ClientV2.WPF.Services;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using System.Collections.ObjectModel;

namespace BarkFluff.ClientV2.WPF.ViewModels;

public sealed partial class SelectNodeViewModel : ObservableObject
{
    private readonly INodeConnectionService _nodeConnectionService;
    private readonly IApplicationDataStore _dataStore;
    private readonly IOnboardingNavigationService _navigation;
    private readonly ILocalizationService _localization;

    public SelectNodeViewModel(
        INodeConnectionService nodeConnectionService,
        IApplicationDataStore dataStore,
        IOnboardingNavigationService navigation,
        ILocalizationService localization)
    {
        _nodeConnectionService = nodeConnectionService;
        _dataStore = dataStore;
        _navigation = navigation;
        _localization = localization;
    }

    public ObservableCollection<PublicNode> PublicNodes { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectManualCommand))]
    private string _manualAddress = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectSelectedNodeCommand))]
    private PublicNode? _selectedPublicNode;

    [ObservableProperty]
    private bool _isLoadingPublicNodes;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectManualCommand))]
    [NotifyCanExecuteChangedFor(nameof(ConnectSelectedNodeCommand))]
    private bool _isConnecting;

    [ObservableProperty]
    private string? _errorMessage;

    public async Task LoadPublicNodesAsync()
    {
        if (IsLoadingPublicNodes)
        {
            return;
        }

        IsLoadingPublicNodes = true;
        try
        {
            var nodes = await _nodeConnectionService.GetPublicNodesAsync();
            PublicNodes.Clear();
            foreach (var node in nodes)
            {
                PublicNodes.Add(node);
            }
        }
        finally
        {
            IsLoadingPublicNodes = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanConnectManual))]
    private Task ConnectManualAsync() => ConnectAsync(ManualAddress);

    [RelayCommand(CanExecute = nameof(CanConnectSelectedNode))]
    private Task ConnectSelectedNodeAsync() => ConnectAsync(SelectedPublicNode!.Address);

    private bool CanConnectManual() => !IsConnecting && !string.IsNullOrWhiteSpace(ManualAddress);

    private bool CanConnectSelectedNode() => !IsConnecting && SelectedPublicNode is not null;

    private async Task ConnectAsync(string address)
    {
        ErrorMessage = null;
        IsConnecting = true;
        try
        {
            var result = await _nodeConnectionService.ConnectAsync(address);
            if (!result.IsSuccess)
            {
                ErrorMessage = _localization.GetString(result.ErrorResourceKey!);
                return;
            }

            await _dataStore.SaveNodeServiceConfigurationAsync(result.Connection!);
            _navigation.ShowLogin();
        }
        catch (Exception)
        {
            ErrorMessage = _localization.GetString("Error_NodeConnectionFailed");
        }
        finally
        {
            IsConnecting = false;
        }
    }
}
