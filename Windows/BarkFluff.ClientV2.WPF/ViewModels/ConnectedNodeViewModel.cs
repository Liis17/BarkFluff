using BarkFluff.ClientV2.WPF.Models;
using BarkFluff.ClientV2.WPF.Services;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BarkFluff.ClientV2.WPF.ViewModels;

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
