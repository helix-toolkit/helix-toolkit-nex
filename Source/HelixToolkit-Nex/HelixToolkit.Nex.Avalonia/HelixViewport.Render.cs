using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using HelixToolkit.Nex.Graphics;
using Microsoft.Extensions.Logging;

namespace HelixToolkit.Nex.Avalonia;

/// <summary>
/// Avalonia composition/presentation portion of the <see cref="HelixViewport"/> partial class. It
/// composes the <see cref="CompositionSurfacePresenter"/> as the control's visual child, owns the
/// per-viewport <see cref="IEngineOutputBridge"/> (Windows shared D3D11 texture or Linux Vulkan
/// external memory, selected by <see cref="OperatingSystem.IsWindows"/>), and drives the render tick
/// from the Avalonia compositor (<c>TopLevel.RequestAnimationFrame</c>, with a
/// <see cref="DispatcherTimer"/> fallback). Each tick calls <see cref="EnsureSize"/>, the shared
/// <c>Render(...)</c>, then <see cref="CompositionSurfacePresenter.PresentAsync"/>.
/// </summary>
/// <remarks>
/// <para>
/// Scope of this file (task 11.1): render tick wiring, presenter composition, bridge ownership /
/// selection, the <see cref="OnReleaseResources"/> hook that disposes the bridge, and a first-cut
/// <see cref="EnsureSize"/> plus size tracking. The members established here
/// (<see cref="_presenter"/>, <see cref="_bridge"/>, <see cref="_sizeChanged"/>,
/// <see cref="EnsureSize"/>, <see cref="OnSizeChanged"/>, <see cref="CreateBridge"/>) are the
/// coordination surface for the remaining render-loop tasks.
/// </para>
/// <para>
/// <b>Task 11.2</b> (resize / zero-size handling): <see cref="EnsureSize"/>
/// creates the bridge lazily on the first nonzero-size tick, and on a subsequent size change waits
/// for engine idle and releases+recreates the bridge output at the new size (the bridge's Resize
/// performs the release+recreate of its shared-texture/external-memory internals - Requirement 8.2),
/// then updates the camera-controller viewport width/height via UpdateViewportSize (Requirement
/// 8.3). Both EnsureSize and TickAsync short-circuit before creating any interop resource or
/// presenting when the width or height is zero (Requirement 8.4).
/// </para>
/// <para>
/// <b>Deferred to task 11.3</b> (teardown): this file disposes the bridge through
/// <see cref="OnReleaseResources"/> (invoked by the engine lifecycle in
/// <c>HelixViewport.Lifecycle.cs</c>). Task 11.3 completes full teardown, including presenter
/// disposal and releasing every interop resource while leaving the externally-owned engine intact.
/// </para>
/// </remarks>
public partial class HelixViewport
{
    /// <summary>
    /// Number of rotating shared-output buffers. Sized to <see cref="MaxPresentsInFlight"/> plus one:
    /// the extra buffer is the one the engine is currently rendering into while up to
    /// <see cref="MaxPresentsInFlight"/> previously rendered buffers are still being read by the
    /// compositor, so the engine never overwrites a buffer a present is still consuming.
    /// </summary>
    private const int OutputBufferCount = MaxPresentsInFlight + 1;

    /// <summary>
    /// Number of presents kept in flight before the render loop applies backpressure. The Avalonia
    /// compositor takes about two display refreshes to complete a surface update
    /// (<c>UpdateWithSemaphoresAsync</c>), so serializing on a single present caps throughput at half
    /// the refresh rate. Keeping two presents in flight hides that latency and lets the frame rate reach
    /// the display refresh. Requires <see cref="OutputBufferCount"/> to be at least this plus one (the
    /// buffer currently being rendered), so the engine never overwrites a buffer the compositor is still
    /// reading.
    /// </summary>
    private const int MaxPresentsInFlight = 2;

    /// <summary>
    /// Composition presenter that hosts the shared engine output through a
    /// <c>CompositionDrawingSurface</c>; created once and added as this control's visual child.
    /// </summary>
    private CompositionSurfacePresenter? _presenter;

    /// <summary>
    /// The per-viewport engine-output bridge (Windows shared texture or Linux external memory).
    /// Created lazily by <see cref="EnsureSize"/> once a valid engine context and a nonzero size are
    /// available, and disposed by <see cref="OnReleaseResources"/>.
    /// </summary>
    private IEngineOutputBridge? _bridge;

    /// <summary>
    /// The in-flight present tasks, oldest at the front. Each tick issues its present without awaiting
    /// it and enqueues it here; once more than <see cref="MaxPresentsInFlight"/> are outstanding the
    /// tick awaits (and dequeues) the oldest before continuing. This pipelines several presents so the
    /// compositor's multi-vsync present latency is overlapped across frames rather than paid serially,
    /// while the multi-buffered bridge gives each in-flight frame its own output texture so they do not
    /// contend.
    /// </summary>
    private readonly Queue<Task> _pendingPresents = new();

    /// <summary>
    /// Set when the control size changes; consumed by <see cref="EnsureSize"/> on the next nonzero-
    /// size tick to resize the bridge output. Starts <see langword="true"/> so the first tick sizes
    /// the output.
    /// </summary>
    private bool _sizeChanged = true;

    /// <summary>The top-level the control is attached to, used to request compositor animation frames.</summary>
    private TopLevel? _topLevel;

    /// <summary>Fallback render clock used when the compositor animation-frame path is unavailable.</summary>
    private DispatcherTimer? _fallbackTimer;

    /// <summary>Guards against requesting overlapping compositor animation frames.</summary>
    private bool _frameRequested;

    /// <summary>Reentrancy guard so a new tick does not start while an async present is in flight.</summary>
    private bool _rendering;

    // --- Frame-timing instrumentation (aggregated over a ~1 second window) ---
    private long _statsWindowStart;
    private int _statsFrames;
    private double _statsRenderMs;
    private double _statsPresentMs;

    /// <summary>
    /// Raised about once per second on the UI thread with aggregated frame statistics (frames per
    /// second and the average per-frame render and present times). Useful for diagnosing the
    /// engine-render vs compositor-present split; the same numbers are also logged at Information
    /// level.
    /// </summary>
    public event EventHandler<FrameStatistics>? FrameStatisticsUpdated;

    /// <summary>
    /// Creates the control, composes the composition presenter as its visual child, and subscribes to
    /// size changes so the render loop can resize the shared output.
    /// </summary>
    public HelixViewport()
    {
        _presenter = new CompositionSurfacePresenter();
        VisualChildren.Add(_presenter);

        SizeChanged += OnSizeChanged;
    }

    /// <summary>Measures the composition presenter to fill the available space.</summary>
    protected override Size MeasureOverride(Size availableSize)
    {
        _presenter?.Measure(availableSize);
        return _presenter?.DesiredSize ?? default;
    }

    /// <summary>Arranges the composition presenter to fill the control's bounds.</summary>
    protected override Size ArrangeOverride(Size finalSize)
    {
        _presenter?.Arrange(new Rect(finalSize));
        return finalSize;
    }

    /// <summary>
    /// Fills the control's bounds with a transparent brush so the whole viewport participates in
    /// Avalonia hit-testing and therefore receives pointer events (press/move/release/wheel) that the
    /// input overrides in <c>HelixViewport.Input.cs</c> forward to the camera controller.
    /// <para>
    /// This is required because the 3D output is presented through an attached composition child
    /// visual (<see cref="CompositionSurfacePresenter"/>), which is not part of the standard visual
    /// hit-test geometry. Without any rendered content of its own, an Avalonia <see cref="Control"/>
    /// is not hit-testable, so pointer events would never reach the control and the camera would not
    /// respond to the mouse. The transparent fill registers the bounds for hit-testing without drawing
    /// anything visible over the composited scene.
    /// </para>
    /// </summary>
    /// <param name="context">The drawing context for the control's own (non-composition) content.</param>
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
    }

    /// <summary>Starts the render loop once the control is attached to a top-level/compositor.</summary>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _topLevel = TopLevel.GetTopLevel(this);
        _sizeChanged = true;
        StartRenderLoop();
    }

    /// <summary>
    /// Stops the render loop when the control leaves the visual tree and releases the bridge/interop
    /// output so no GPU work continues off-tree. The per-viewport <see cref="RenderContext"/> and the
    /// externally-owned <see cref="Engine"/> are left intact so the control can be re-attached and
    /// resume rendering; full <see cref="RenderContext"/> release happens in <see cref="Dispose"/>.
    /// </summary>
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        StopRenderLoop();
        // Requirement 8.5: release every interop resource the bridge created on unload. Leave the
        // RenderContext and Engine intact (Requirement 3.5). Re-attach recreates the bridge lazily.
        ReleaseResources();
        _sizeChanged = true;
        _topLevel = null;
        base.OnDetachedFromVisualTree(e);
    }

    /// <summary>Marks that the shared output needs resizing on the next nonzero-size tick.</summary>
    private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (_disposed || _engine is null)
        {
            return;
        }
        _sizeChanged = true;
    }

    /// <summary>
    /// Begins driving frames. Prefers the compositor animation-frame mechanism
    /// (<c>TopLevel.RequestAnimationFrame</c>); when no top-level is available it falls back to a
    /// <see cref="DispatcherTimer"/> at ~60 Hz.
    /// </summary>
    private void StartRenderLoop()
    {
        if (_topLevel is not null)
        {
            RequestNextFrame();
            return;
        }

        _fallbackTimer ??= new DispatcherTimer(
            TimeSpan.FromMilliseconds(1000.0 / 60.0),
            DispatcherPriority.Render,
            (_, _) => _ = TickAsync()
        );
        _fallbackTimer.Start();
    }

    /// <summary>Stops both the compositor animation-frame loop and the fallback timer.</summary>
    private void StopRenderLoop()
    {
        _frameRequested = false;
        _fallbackTimer?.Stop();
    }

    /// <summary>
    /// Requests the next compositor animation frame, unless one is already pending or the control is
    /// no longer attached/disposed. The callback runs one tick and re-requests to form a continuous
    /// render loop.
    /// </summary>
    private void RequestNextFrame()
    {
        if (_disposed || _topLevel is null || _frameRequested)
        {
            return;
        }

        _frameRequested = true;
        _topLevel.RequestAnimationFrame(OnAnimationFrame);
    }

    /// <summary>Compositor animation-frame callback: runs a tick then schedules the next frame.</summary>
    private async void OnAnimationFrame(TimeSpan _)
    {
        _frameRequested = false;

        // Request the next animation frame immediately, rather than after awaiting the present. This
        // keeps the compositor's frame cadence independent of how long the present takes, so the loop
        // does not lose a vsync waiting for the previous frame's compositor read to complete. The
        // _rendering guard in TickAsync still prevents overlapping renders.
        RequestNextFrame();

        try
        {
            await TickAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "HelixViewport render tick failed.");
        }
    }

    /// <summary>
    /// Runs a single render tick: guards on context validity (Requirement 8.1), ensures the shared
    /// output is sized, renders the offscreen frame into the bridge target, and presents it through
    /// the composition surface (Requirement 4.3).
    /// </summary>
    private async Task TickAsync()
    {
        // Requirement 8.1: only render when the engine/render-context are valid and a viewport
        // client is present.
        if (_disposed || _rendering || !(IsContextValid && ViewportClient is not null))
        {
            return;
        }

        _rendering = true;
        try
        {
            // Before a resize recreates (and disposes) the output buffers, drain every in-flight
            // present so the compositor is not still reading a texture that is about to be destroyed.
            if (_sizeChanged)
            {
                await DrainPendingPresentsAsync();
            }

            EnsureSize();

            if (_bridge is null || _presenter is null)
            {
                return;
            }

            float width = (float)ActualWidth;
            float height = (float)ActualHeight;

            // Requirement 8.4: skip presentation on zero size (EnsureSize also short-circuits).
            if (width <= 0f || height <= 0f)
            {
                return;
            }

            // Hand the current write buffer's sync to the shared submit path before rendering.
            _frameSyncInfo = _bridge.EngineSyncInfo;

            // Measure the engine render (CPU record + submit) separately from the present wait so the
            // two costs can be compared when diagnosing the frame rate.
            long renderStart = System.Diagnostics.Stopwatch.GetTimestamp();
            bool rendered = Render(width, height, _bridge.EngineTarget);
            double renderMs = System
                .Diagnostics.Stopwatch.GetElapsedTime(renderStart)
                .TotalMilliseconds;

            if (!rendered)
            {
                return;
            }

            // Capture this frame's present inputs from the CURRENT buffer, then rotate the write target
            // so the next tick renders into a different buffer while this frame is still being read by
            // the compositor.
            SharedImageDescription description = _bridge.CreateImportDescription();
            ISurfaceUpdateSync surfaceSync = _bridge.CreateSurfaceSync();
            _bridge.AdvanceFrame();

            // Issue this frame's present immediately (early in the tick, so it lands in the compositor's
            // next commit) WITHOUT awaiting it, then enqueue it. Only once more than
            // MaxPresentsInFlight presents are outstanding do we await the oldest, applying backpressure
            // that paces the loop to the compositor while keeping several presents pipelined. This hides
            // the compositor's ~2-vsync present latency so throughput reaches the display refresh rather
            // than stalling at half of it. The multi-buffered bridge gives each in-flight present its own
            // output texture, and the engine's read-finished semaphore wait keeps buffer reuse correct.
            Task present = _presenter.PresentAsync(description, surfaceSync);
            _pendingPresents.Enqueue(present);

            double presentMs = 0;
            if (_pendingPresents.Count > MaxPresentsInFlight)
            {
                long presentWaitStart = System.Diagnostics.Stopwatch.GetTimestamp();
                await _pendingPresents.Dequeue();
                presentMs = System
                    .Diagnostics.Stopwatch.GetElapsedTime(presentWaitStart)
                    .TotalMilliseconds;
            }

            AccumulateFrameStatistics(renderMs, presentMs);
        }
        finally
        {
            _rendering = false;
        }
    }

    /// <summary>
    /// Aggregates per-frame render/present timings and, about once per second, logs them and raises
    /// <see cref="FrameStatisticsUpdated"/>. This runs on the UI thread (the tick thread), so the
    /// event can update UI directly.
    /// </summary>
    private void AccumulateFrameStatistics(double renderMs, double presentMs)
    {
        if (_statsWindowStart == 0)
        {
            _statsWindowStart = System.Diagnostics.Stopwatch.GetTimestamp();
        }

        _statsRenderMs += renderMs;
        _statsPresentMs += presentMs;
        _statsFrames++;

        TimeSpan elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(_statsWindowStart);
        if (elapsed.TotalSeconds < 1.0 || _statsFrames == 0)
        {
            return;
        }

        var stats = new FrameStatistics(
            _statsFrames / elapsed.TotalSeconds,
            _statsRenderMs / _statsFrames,
            _statsPresentMs / _statsFrames
        );

        _logger.LogInformation(
            "HelixViewport perf: {Fps:F1} FPS | render {Render:F2} ms | present {Present:F2} ms",
            stats.Fps,
            stats.AverageRenderMs,
            stats.AveragePresentMs
        );

        FrameStatisticsUpdated?.Invoke(this, stats);

        _statsFrames = 0;
        _statsRenderMs = 0;
        _statsPresentMs = 0;
        _statsWindowStart = System.Diagnostics.Stopwatch.GetTimestamp();
    }

    /// <summary>
    /// Ensures the shared output bridge exists and matches the current control size (task 11.2).
    /// <para>
    /// Zero size: when the width or height is zero, no interop resource is created and the tick is
    /// skipped (Requirement 8.4); <see cref="TickAsync"/> additionally skips presentation for the
    /// same tick.
    /// </para>
    /// <para>
    /// First nonzero-size tick: the bridge is created lazily once a valid engine context and a
    /// nonzero size are both available, and the camera-controller viewport size is initialized.
    /// </para>
    /// <para>
    /// Size change (<see cref="_sizeChanged"/>): waits for engine idle, then releases and recreates
    /// the bridge output at the new size (Requirement 8.2) and updates the camera-controller viewport
    /// width/height (Requirement 8.3). The bridge's <c>Resize</c> releases and recreates its shared
    /// texture / external-memory internals; waiting for engine idle first avoids destroying a target
    /// the engine or compositor is still using.
    /// </para>
    /// </summary>
    private void EnsureSize()
    {
        if (_disposed || _engine is null)
        {
            return;
        }

        var width = (uint)ActualWidth;
        var height = (uint)ActualHeight;

        // Requirement 8.4: never create or recreate interop resources at zero size. Leave
        // _sizeChanged set so the resize is applied on the next nonzero-size tick.
        if (width == 0 || height == 0)
        {
            return;
        }

        // First nonzero-size tick: lazily construct the bridge now that a valid engine context and a
        // nonzero size are both available, then seed the camera-controller viewport size.
        if (_bridge is null)
        {
            _bridge = CreateBridge(_engine.Context, width, height);
            UpdateViewportSize(width, height);
            _sizeChanged = false;
            return;
        }

        if (!_sizeChanged)
        {
            return;
        }

        // Size changed to a nonzero size: wait for the GPU to go idle, then release+recreate the
        // shared output at the new size (Requirement 8.2). The bridge's Resize performs the internal
        // ReleaseResources()+CreateResources() (and no-ops when the size is unchanged).
        _engine.WaitForIdle();
        _bridge.Resize(width, height);
        // The bridge recreated its shared output at the new size; the presenter re-imports lazily
        // because the descriptions' handles/size changed.
        // Requirement 8.3: propagate the new size to the camera controller.
        UpdateViewportSize(width, height);
        _sizeChanged = false;
    }

    /// <summary>
    /// Creates the platform engine-output bridge for the given engine context and size, selecting the
    /// Windows shared-texture bridge on Windows and the Linux external-memory bridge otherwise.
    /// </summary>
    /// <param name="context">The engine's Vulkan context.</param>
    /// <param name="width">Nonzero output width in pixels.</param>
    /// <param name="height">Nonzero output height in pixels.</param>
    /// <returns>The platform-appropriate bridge.</returns>
    private static IEngineOutputBridge CreateBridge(IContext context, uint width, uint height)
    {
        // Multi-buffer the shared output (OutputBufferCount buffers) so the engine can render the next
        // frame into a free buffer while the compositor still reads the presented ones, decoupling the
        // two and avoiding the single-texture keyed-mutex/semaphore stall that otherwise halves the
        // frame rate.
        var buffers = new IEngineOutputBridge[OutputBufferCount];
        for (int i = 0; i < buffers.Length; i++)
        {
            buffers[i] = CreateSingleBridge(context, width, height);
        }
        return new BufferedEngineOutputBridge(buffers);
    }

    /// <summary>Creates one single-buffer platform bridge (Windows shared texture or Linux external memory).</summary>
    private static IEngineOutputBridge CreateSingleBridge(IContext context, uint width, uint height)
    {
        if (OperatingSystem.IsWindows())
        {
            return new WindowsSharedTextureBridge(context, width, height);
        }
        return new LinuxExternalMemoryBridge(context, width, height);
    }

    /// <summary>
    /// Engine-lifecycle hook (declared in <c>HelixViewport.Lifecycle.cs</c>) that tears down the
    /// engine-tied interop output resources: it disposes the platform bridge, releases the presenter's
    /// last imported GPU image (which references the current engine output), and resets the per-frame
    /// sync info. Invoked on engine reassignment, on unload (<see cref="OnDetachedFromVisualTree"/>),
    /// and from <see cref="Dispose"/>. The externally-owned engine is never disposed here
    /// (Requirement 3.5), and the composition surface/visual are left intact so presentation can
    /// resume after a new engine output is created. Idempotent and safe to call repeatedly.
    /// </summary>
    partial void OnReleaseResources()
    {
        _frameSyncInfo = default;

        // Drop every in-flight present. They cannot be awaited synchronously here, so observe each to
        // avoid an unobserved-task exception if it faults once the imported images are disposed below.
        while (_pendingPresents.Count > 0)
        {
            _pendingPresents.Dequeue().ContinueWith(static t => _ = t.Exception, TaskScheduler.Default);
        }

        Disposer.DisposeAndRemove(ref _bridge);
        _presenter?.ReleaseImportedImage();
    }

    /// <summary>
    /// Awaits and clears every in-flight present so the compositor is no longer reading any shared
    /// output texture. Used before a resize recreates the buffers. Faults are logged and swallowed so a
    /// present that failed does not abort the drain.
    /// </summary>
    private async Task DrainPendingPresentsAsync()
    {
        while (_pendingPresents.Count > 0)
        {
            try
            {
                await _pendingPresents.Dequeue();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Pending present faulted while draining before resize.");
            }
        }
    }
}
