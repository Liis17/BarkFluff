using BarkFluff.ClientV2.WPF.ViewModels;

using Wpf.Ui.Controls;

namespace BarkFluff.ClientV2.WPF;

public partial class MainWindow : FluentWindow
{
    public MainWindow(MainWindowViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
    }
}
