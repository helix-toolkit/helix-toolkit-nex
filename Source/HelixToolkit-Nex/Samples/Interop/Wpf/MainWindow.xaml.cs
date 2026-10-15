using System.Windows;
using Interop.Common;

namespace WpfInterop;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel(HelixToolkit.Nex.Engine.EngineInteropTarget.WPF);
        DataContext = _viewModel;
        Closed += (_, _) => DisposeOwnedResources();
    }

    /// <summary>
    /// Releases the WPF viewports before their externally owned engine.
    /// </summary>
    private void DisposeOwnedResources()
    {
        ViewportFly.Dispose();
        ViewportOverhead.Dispose();
        _viewModel.Dispose();
    }
}
