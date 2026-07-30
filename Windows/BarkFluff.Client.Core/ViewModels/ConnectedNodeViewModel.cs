using BarkFluff.Client.Core.Models;
using BarkFluff.Client.Core.Services;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BarkFluff.Client.Core.ViewModels;

public sealed partial class ConnectedNodeViewModel : ObservableObject
{
    private readonly IOnboardingNavigationService _navigation;

    public ConnectedNodeViewModel(IOnboardingNavigationService navigation)
    {
        _navigation = navigation;
    }

    [ObservableProperty]
    private NodeProfile? _node;

    public void SetNode(NodeProfile node)
    {
        Node = node;
    }

    [RelayCommand]
    private void ChooseAnotherNode()
    {
        _navigation.ShowSelectNode();
    }
}
