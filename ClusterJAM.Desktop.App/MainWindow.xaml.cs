using System.Windows;
using System.Windows.Threading;
using ClusterJAM.Desktop.App.ViewModels;

namespace ClusterJAM.Desktop.App;

public partial class MainWindow : Window
{
    private readonly AuthenticationViewModel _viewModel;
    private readonly DispatcherTimer _sessionTimer = new() { Interval = TimeSpan.FromSeconds(30) };

    public MainWindow(AuthenticationViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        _sessionTimer.Tick += OnSessionTick;
        Loaded += (_, _) => _sessionTimer.Start();
        Closed += OnClosed;
    }

    private async void OnSessionTick(object? sender, EventArgs args) => await _viewModel.MaintainSessionAsync();

    private void OnClosed(object? sender, EventArgs args)
    {
        _sessionTimer.Stop();
        _sessionTimer.Tick -= OnSessionTick;
        _viewModel.Dispose();
    }
}
