using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using HelixToolkit.Nex.Avalonia;

namespace AvaloniaInterop;

/// <summary>
/// Main application window that hosts the <c>HelixToolkit.Nex.Avalonia.HelixViewport</c>.
/// </summary>
/// <remarks>
/// <para>
/// The <see cref="MainViewModel"/> builds a full headless Vulkan engine and scene. That construction
/// is performed on a background thread (<see cref="InitializeAsync"/>) rather than in a field
/// initializer, for two reasons:
/// </para>
/// <list type="number">
/// <item><description>
/// It avoids a UI-thread deadlock: the scene build goes through <c>IScene.Build</c>, which is a
/// synchronous-over-asynchronous wrapper (<c>BuildAsync(...).Result</c>). Blocking on that from the
/// Avalonia UI thread would deadlock, because the awaited continuation needs the same UI thread that
/// is blocked waiting for the result. Running the build on a thread-pool thread (no captured UI
/// <c>SynchronizationContext</c>) lets it complete.
/// </description></item>
/// <item><description>
/// It lets the window appear immediately instead of freezing for the multi-second engine/scene
/// build. Once the build finishes, the view model is assigned as the <see cref="Control.DataContext"/>
/// on the UI thread so the viewport's <c>Engine</c>, <c>ViewportClient</c>, <c>CameraController</c>,
/// and <c>PointerRingEnabled</c> bindings resolve and the render loop begins.
/// </description></item>
/// </list>
/// </remarks>
public partial class MainWindow : Window
{
    private MainViewModel? _viewModel;
    private HelixViewport? _viewport;
    private TextBlock? _fpsText;
    private TextBlock? _frameTimeText;

    public MainWindow()
    {
        InitializeComponent();

        // Resolve the named controls from the XAML name scope rather than relying on the auto-generated
        // x:Name fields. In this sample those generated fields are not populated at runtime (the
        // compiled-XAML field-population step is not active, so the runtime XAML loader builds the tree
        // and registers the name scope but leaves the code-behind fields null). FindControl reads the
        // registered name scope and works regardless.
        _viewport = this.FindControl<HelixViewport>("ViewportFly");
        _fpsText = this.FindControl<TextBlock>("FpsText");
        _frameTimeText = this.FindControl<TextBlock>("FrameTimeText");

        if (_viewport is not null)
        {
            _viewport.FrameStatisticsUpdated += OnFrameStatisticsUpdated;
        }

        Closed += OnClosed;

        _ = InitializeAsync();
    }

    private void OnFrameStatisticsUpdated(object? sender, FrameStatistics stats)
    {
        if (_fpsText is not null)
        {
            _fpsText.Text = $"FPS: {stats.Fps:F1}";
        }

        if (_frameTimeText is not null)
        {
            _frameTimeText.Text =
                $"render {stats.AverageRenderMs:F2} ms  present {stats.AveragePresentMs:F2} ms";
        }
    }

    /// <summary>
    /// Builds the view model (headless Vulkan engine + scene + camera) on a background thread, then
    /// assigns it as the <see cref="Control.DataContext"/> on the UI thread so the viewport bindings
    /// resolve. See the class remarks for why the build must not run on the UI thread.
    /// </summary>
    private async Task InitializeAsync()
    {
        MainViewModel viewModel = await Task.Run(() => new MainViewModel());

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            _viewModel = viewModel;
            DataContext = viewModel;
        });
    }

    private void OnClosed(object? sender, EventArgs e) => _viewModel?.Dispose();

    /// <summary>
    /// Resets the fly camera to its initial framing. Demonstrates that overlay controls receive input
    /// (hit-testing) over the 3D viewport without the drag being forwarded to the camera controller.
    /// </summary>
    private void OnResetCamera(object? sender, RoutedEventArgs e)
        => _viewModel?.FlyCameraController.Reset();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
