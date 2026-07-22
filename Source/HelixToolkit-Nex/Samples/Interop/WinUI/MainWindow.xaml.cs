using HelixToolkit.Nex.Engine;
using Interop.Common;
using Microsoft.UI.Xaml;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace WinUIInterop
{
    /// <summary>
    /// An empty window that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainWindow : Window
    {
        private readonly MainViewModel _viewModel;

        public MainWindow()
        {
            InitializeComponent();
            _viewModel = new MainViewModel(EngineInteropTarget.WinUI);
            MainGrid.DataContext = _viewModel;
            Closed += (_, _) => DisposeOwnedResources();
        }

        /// <summary>
        /// Releases the WinUI viewports before their externally owned engine.
        /// </summary>
        private void DisposeOwnedResources()
        {
            ViewportFly.Dispose();
            ViewportOverhead.Dispose();
            _viewModel.Dispose();
        }
    }
}
