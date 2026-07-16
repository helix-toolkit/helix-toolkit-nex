using Avalonia.Controls;
using HelixToolkit.Nex;
using HelixToolkit.Nex.Engine;
using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Interop;
using Microsoft.Extensions.Logging;

namespace HelixToolkit.Nex.Avalonia;

/// <summary>
/// Avalonia host-specific portion of the <see cref="HelixViewport"/> partial class. It supplies the
/// members the shared <c>ViewportCommon.cs</c> / <c>ViewportProperties.cs</c> logic (compiled under
/// the <c>HxAvalonia</c> symbol) expects but that are platform specific:
/// <list type="bullet">
///   <item><description>the <c>ActualWidth</c>/<c>ActualHeight</c> surface the shared render loop reads (mapped onto the Avalonia <see cref="Control.Bounds"/>);</description></item>
///   <item><description>the engine lifecycle (<c>SetEngine</c>/<c>SetClient</c>) that creates and releases the per-viewport <see cref="RenderContext"/>;</description></item>
///   <item><description>the per-frame synchronization info handed to <c>Engine.Submit</c> (<see cref="_frameSyncInfo"/>).</description></item>
/// </list>
/// The actual GPU interop output resources (composition surface, platform bridge) are created and
/// released by later composition/bridge wiring; the hooks here (<see cref="ReleaseResources"/>) let
/// that code plug in without changing the engine lifecycle.
/// </summary>
public partial class HelixViewport : IDisposable
{
    private static readonly ILogger _logger = LogManager.Create<HelixViewport>();

    /// <summary>Timestamp of the previous frame, used by the shared render loop to compute delta time.</summary>
    private long _lastTimestamp;

    /// <summary>
    /// Per-frame synchronization info passed to <c>Engine.Submit</c>. On Windows this carries the
    /// keyed-mutex keys/handles for the shared D3D11 texture; on Linux it carries the exported binary
    /// semaphore handles the engine signals/waits to serialize its write against the compositor read.
    /// Populated each frame from the platform bridge's <c>EngineSyncInfo</c>.
    /// </summary>
    private KeyedMutexSyncInfo _frameSyncInfo;

    private bool _disposed;

    /// <summary>
    /// Rendered width of the control. The shared render loop is written against the WPF/WinUI
    /// <c>ActualWidth</c> vocabulary; Avalonia exposes the rendered size through <see cref="Control.Bounds"/>.
    /// </summary>
    private double ActualWidth => Bounds.Width;

    /// <summary>
    /// Rendered height of the control. The shared render loop is written against the WPF/WinUI
    /// <c>ActualHeight</c> vocabulary; Avalonia exposes the rendered size through <see cref="Control.Bounds"/>.
    /// </summary>
    private double ActualHeight => Bounds.Height;

    /// <summary>
    /// Assigns (or clears) the externally-owned engine. Reassigning releases the previous
    /// <see cref="RenderContext"/> and interop resources before creating and initializing a new
    /// render context for the new engine. The engine itself is never disposed here because it is
    /// externally owned and shared across viewports.
    /// </summary>
    private void SetEngine(Engine.Engine? engine)
    {
        if (_engine == engine)
        {
            return;
        }

        ReleaseResources();
        Disposer.DisposeAndRemove(ref _renderContext);

        _engine = engine;
        if (_engine is null)
        {
            return;
        }

        _renderContext = _engine.CreateRenderContext();
        _renderContext.Initialize();
        _renderContext.RenderParams.EnableGammaCorrection = true; // Must enable gamma correction.
        _renderArgs = new ViewportRenderingEventArgs(_renderContext);
    }

    /// <summary>Assigns the per-frame data/camera source for this viewport.</summary>
    private void SetClient(IViewportClient? client)
    {
        _viewportClient = client;
    }

    /// <summary>
    /// Releases the GPU interop output resources created by the composition bridge. The bridge/
    /// composition wiring (added in later tasks) implements <see cref="OnReleaseResources"/>; the
    /// engine lifecycle above calls through here so it stays independent of the interop layer.
    /// </summary>
    private void ReleaseResources()
    {
        OnReleaseResources();
    }

    /// <summary>
    /// Hook implemented by the composition/bridge partial to tear down interop output resources.
    /// Intentionally a partial method so the Linux and Windows composition paths can supply the
    /// implementation without the engine lifecycle depending on either.
    /// </summary>
    partial void OnReleaseResources();

    /// <summary>
    /// Releases the per-viewport render context and all interop resources this control created. The
    /// externally-owned <c>Engine</c> is intentionally left undisposed.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        // Stop driving frames, then release every interop resource the bridge/presenter created
        // (Requirement 8.5). StopRenderLoop and _presenter live on the composition partial
        // (HelixViewport.Render.cs) but share this instance's state.
        StopRenderLoop();
        ReleaseResources();
        _presenter?.Cleanup();
        _presenter = null;

        Disposer.DisposeAndRemove(ref _renderContext);

        // The Engine is externally owned and shared across viewports; never dispose it here
        // (Requirement 3.5).
        GC.SuppressFinalize(this);
    }
}
